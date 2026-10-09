using System.Globalization;
using Hastane.Payment.Api.Data;
using Hastane.Payment.Api.Dtos;
using Hastane.Payment.Api.Entities;
using Hastane.Payment.Api.Providers;
using Hastane.Payment.Api.Settings;
using MapsterMapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PaymentEntity = Hastane.Payment.Api.Entities.Payment;

namespace Hastane.Payment.Api.Services;

public class PaymentService : IPaymentService
{
    private const string AlreadyPaidMessage = "Bu randevu için zaten ödeme alınmış.";
    private const string KeyReusedMessage = "Bu Idempotency-Key farklı bir istekle kullanılmış.";
    private const string DefaultDescription = "Muayene ve hizmet bedeli";
    private const string PaidIndexName = "UX_Payments_Appointment_Paid";
    private const string NotProcessedMessage = "Ödeme işlenemedi.";

    private readonly PaymentDbContext _db;
    private readonly IMapper _mapper;
    private readonly IPaymentProvider _provider;
    private readonly BillingOptions _billing;
    private readonly ILogger<PaymentService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public PaymentService(
        PaymentDbContext db, IMapper mapper, IPaymentProvider provider,
        IOptions<BillingOptions> billing, ILogger<PaymentService> logger, IServiceScopeFactory scopeFactory)
    {
        _db = db;
        _mapper = mapper;
        _provider = provider;
        _billing = billing.Value;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    // SQL Server: 2601 = tekil indeks ihlali, 2627 = tekil kısıt ihlali.
    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is SqlException { Number: 2601 or 2627 };

    // Hangi indeks? Yalnız "aynı randevuya ikinci Paid" gerçek mükerrer ödemedir; başka bir tekil ihlal (ör. fatura numarası) kayıt hatasıdır.
    private static bool IsPaidIndexViolation(DbUpdateException ex)
        => IsUniqueViolation(ex) && ex.InnerException!.Message.Contains(PaidIndexName, StringComparison.Ordinal);

    public async Task<OperationResult<PaymentResultDto>> CreateAsync(
        string? idempotencyKey, CreatePaymentDto dto, CancellationToken ct)
    {
        // İkinci savunma hattı: DTO öznitelikleri (controller) atlansa bile kurallar burada da korunur.
        var error = ValidateRequest(idempotencyKey, dto);
        if (error is not null) return OperationResult<PaymentResultDto>.Validation(error);

        var key = idempotencyKey!;
        var hash = RequestFingerprint.Compute(dto);

        // 1) Bu anahtar daha önce geldiyse sağlayıcıya GİTMEDEN ilk sonucu döndür.
        var existing = await FindByKeyAsync(key, ct);
        if (existing is not null) return await ReplayAsync(existing, hash, ct);

        // 2) Katman 1 (güzel hata mesajı): randevu zaten ödenmiş mi?
        if (await _db.Payments.AnyAsync(p => p.AppointmentId == dto.AppointmentId && p.Status == PaymentStatus.Paid, ct))
            return OperationResult<PaymentResultDto>.Conflict(AlreadyPaidMessage);

        // İstemci buraya kadar vazgeçebilir; para hareketi başladıktan sonra iptal artık dikkate alınmaz (aşağıda CancellationToken.None).
        ct.ThrowIfCancellationRequested();

        // 3) Parayı çekmeden ÖNCE anahtarı Pending satırla rezerve et. Aynı anahtarla 8 istek aynı anda gelse
        //    tekil indeks yalnız birine izin verir; kazanan sağlayıcıyı çağırır, kalanlar kazananın sonucunu bekler.
        var payment = new PaymentEntity
        {
            Id = Guid.CreateVersion7(),
            AppointmentId = dto.AppointmentId,
            PatientId = dto.PatientId,
            DoctorId = dto.DoctorId,
            Amount = dto.Amount,
            Currency = dto.Currency,
            Status = PaymentStatus.Pending,
            IdempotencyKey = key,
            RequestHash = hash,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Payments.Add(payment);
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Pending satırda yalnız anahtar indeksi devrede olabilir: başka bir istek anahtarı bizden önce aldı.
            _db.Entry(payment).State = EntityState.Detached;
            var winner = await FindByKeyAsync(key, ct);
            return winner is null
                ? OperationResult<PaymentResultDto>.Conflict("Aynı anahtarla eşzamanlı istek; tekrar deneyin.")
                : await ReplayAsync(winner, hash, ct);
        }

        // 4) Sağlayıcı. İstemci ct'si BİLEREK verilmiyor: bağlantı kopsa da para çekme işlemi yarım bırakılmaz.
        ChargeResult charge;
        try
        {
            charge = await _provider.ChargeAsync(new ChargeRequest(dto.Amount, dto.Currency, new CardDetails
            {
                Number = dto.Card.Number,
                ExpMonth = dto.Card.ExpMonth,
                ExpYear = dto.Card.ExpYear,
                Cvv = dto.Card.Cvv,
                HolderName = dto.Card.HolderName,
            }), CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Yalnız tür + mesaj loglanır; istek içeriği (kart) asla.
            _logger.LogError("Ödeme sağlayıcısı hata verdi, ödeme {PaymentId}: {ExceptionType}: {ExceptionMessage}",
                payment.Id, ex.GetType().Name, ex.Message);
            await MarkFailedAsync(payment.Id, PaymentFailureKind.ProviderError, "Ödeme sağlayıcısına ulaşılamadı.", null);
            return OperationResult<PaymentResultDto>.BadGateway(NotProcessedMessage);
        }

        payment.CardBrand = charge.CardBrand;
        payment.CardLast4 = charge.CardLast4;

        if (!charge.Success)
        {
            // Denetim izi: reddedilen deneme de kaydedilir (fatura yok).
            payment.Status = PaymentStatus.Failed;
            payment.FailureKind = PaymentFailureKind.Declined;
            payment.FailureReason = Truncate(charge.FailureReason ?? "Ödeme reddedildi", 200);
            await _db.SaveChangesAsync(CancellationToken.None);
            return OperationResult<PaymentResultDto>.PaymentRequired(_mapper.Map<PaymentResultDto>(payment), payment.FailureReason);
        }

        // 5) PARA ÇEKİLDİ. Buradan sonraki HER hata telafi gerektirir: dış sistemle (sağlayıcı) veritabanı tek
        //    transaction'a girmez, bu yüzden "çekildi ama kaydedilemedi" penceresi vardır; telafi iadesiyle kapatılır.
        try
        {
            var now = DateTime.UtcNow;
            payment.Status = PaymentStatus.Paid;
            payment.PaidAt = now;
            payment.ProviderReference = charge.ProviderReference;

            var sequence = await NextInvoiceSequenceAsync(CancellationToken.None);
            var invoice = BuildInvoice(payment, dto.Description, sequence, now);
            payment.Invoice = invoice;
            _db.Invoices.Add(invoice);

            // Payment(Paid) + Invoice + InvoiceLine TEK SaveChanges, tek veritabanı transaction'ı.
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException ex) when (IsPaidIndexViolation(ex))
        {
            // Katman 2: iki istek katman-1 kontrolünü aynı anda geçti; filtreli tekil indeks ikincisini reddetti.
            _logger.LogWarning("Aynı randevuya eşzamanlı ödeme, ödeme {PaymentId} telafi ediliyor.", payment.Id);
            return await CompensateAsync(payment, charge, PaymentFailureKind.Duplicate, null);
        }
        catch (Exception ex)
        {
            return await CompensateAsync(payment, charge, PaymentFailureKind.RecordingFailed, ex);
        }

        return OperationResult<PaymentResultDto>.Created(_mapper.Map<PaymentResultDto>(payment));
    }

    // Para çekildi ama kayıt yazılamadı: sağlayıcıdan iade al, satırı YENİ bir DbContext ile Failed yap.
    private async Task<OperationResult<PaymentResultDto>> CompensateAsync(
        PaymentEntity payment, ChargeResult charge, PaymentFailureKind kind, Exception? cause)
    {
        if (cause is not null)
        {
            _logger.LogError("Ödeme kaydı yazılamadı, ödeme {PaymentId}: {ExceptionType}: {ExceptionMessage}",
                payment.Id, cause.GetType().Name, cause.Message);
        }

        var refunded = false;
        try
        {
            var refund = await _provider.RefundAsync(charge.ProviderReference!, payment.Amount, payment.Currency, CancellationToken.None);
            refunded = refund.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError("Telafi iadesi hata verdi, ödeme {PaymentId}: {ExceptionType}: {ExceptionMessage}",
                payment.Id, ex.GetType().Name, ex.Message);
        }

        if (!refunded)
        {
            _logger.LogCritical("TELAFİ İADESİ BAŞARISIZ, para çekildi: ödeme {PaymentId}, sağlayıcı referansı {ProviderReference}",
                payment.Id, charge.ProviderReference);
        }

        var reason = kind == PaymentFailureKind.Duplicate
            ? (refunded ? "Mükerrer ödeme; ücret iade edildi." : "Mükerrer ödeme; iade edilemedi, elle incelenmeli.")
            : (refunded ? "Ödeme kaydı tamamlanamadı; ücret iade edildi." : "Ödeme kaydı tamamlanamadı; iade edilemedi, elle incelenmeli.");

        await MarkFailedAsync(payment.Id, kind, reason, charge);

        return kind == PaymentFailureKind.Duplicate
            ? OperationResult<PaymentResultDto>.Conflict(AlreadyPaidMessage)
            : OperationResult<PaymentResultDto>.BadGateway(NotProcessedMessage);
    }

    // Bozulmuş olabilecek _db yerine TAZE bir kapsam/DbContext ile tek UPDATE. O da patlarsa LogCritical.
    private async Task MarkFailedAsync(Guid paymentId, PaymentFailureKind kind, string reason, ChargeResult? charge)
    {
        var brand = charge?.CardBrand;
        var last4 = charge?.CardLast4;
        var providerReference = charge?.ProviderReference;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            await db.Payments.Where(p => p.Id == paymentId).ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PaymentStatus.Failed)
                .SetProperty(p => p.FailureKind, (PaymentFailureKind?)kind)
                .SetProperty(p => p.FailureReason, reason)
                .SetProperty(p => p.CardBrand, brand)
                .SetProperty(p => p.CardLast4, last4)
                .SetProperty(p => p.ProviderReference, providerReference), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogCritical("Failed durumu yazılamadı, ödeme {PaymentId}, sağlayıcı referansı {ProviderReference}: {ExceptionType}: {ExceptionMessage}",
                paymentId, providerReference, ex.GetType().Name, ex.Message);
        }
    }

    // Aynı anahtar ikinci kez geldi: gövde aynıysa ilk sonucu döndür (sağlayıcı çağrılmaz), farklıysa 409.
    // Gövde "aynı" sayılırken KART bilgisi karşılaştırılmaz (saklamıyoruz): farklı kartla aynı anahtar da ilk sonucu alır.
    private async Task<OperationResult<PaymentResultDto>> ReplayAsync(PaymentEntity existing, string hash, CancellationToken ct)
    {
        if (existing.RequestHash != hash) return OperationResult<PaymentResultDto>.Conflict(KeyReusedMessage);

        if (existing.Status == PaymentStatus.Pending)
        {
            var staleAfter = TimeSpan.FromSeconds(_billing.PendingStaleSeconds);
            if (DateTime.UtcNow - existing.CreatedAt > staleAfter)
            {
                // Süreç çökmüş olabilir; beklemek anlamsız. (İleride arka plan uzlaştırma işi bunları sağlayıcıyla eşleştirecek.)
                _logger.LogWarning("Bayat Pending ödeme {PaymentId} (yaş > {StaleSeconds} sn).", existing.Id, _billing.PendingStaleSeconds);
                return OperationResult<PaymentResultDto>.Conflict("Önceki işlem sonuçlanmadı; destekle iletişime geçin.");
            }

            // İlk istek hâlâ sağlayıcıda olabilir: sınırlı süre bekleyip sonucunu al.
            var deadline = DateTime.UtcNow.AddSeconds(_billing.PendingWaitSeconds);
            while (existing.Status == PaymentStatus.Pending && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100, ct);
                existing = await FindByKeyAsync(existing.IdempotencyKey, ct) ?? existing;
            }

            if (existing.Status == PaymentStatus.Pending)
                return OperationResult<PaymentResultDto>.Conflict("Bu anahtarla işlem hâlâ sürüyor; biraz sonra tekrar deneyin.");
        }

        var result = _mapper.Map<PaymentResultDto>(existing);
        if (existing.Status != PaymentStatus.Failed) return OperationResult<PaymentResultDto>.Ok(result);

        // Failed satırı ilk cevapla AYNI HTTP durumunu üretir.
        return existing.FailureKind switch
        {
            PaymentFailureKind.Duplicate => OperationResult<PaymentResultDto>.Conflict(AlreadyPaidMessage),
            PaymentFailureKind.ProviderError or PaymentFailureKind.RecordingFailed => OperationResult<PaymentResultDto>.BadGateway(NotProcessedMessage),
            _ => OperationResult<PaymentResultDto>.PaymentRequired(result, existing.FailureReason),
        };
    }

    private Task<PaymentEntity?> FindByKeyAsync(string key, CancellationToken ct)
        => _db.Payments.AsNoTracking().Include(p => p.Invoice).FirstOrDefaultAsync(p => p.IdempotencyKey == key, ct);

    // SELECT NEXT VALUE FOR InvoiceNumberSeq: sıra numarasını SQL Server verir, her çağrıya benzersiz bir sayı.
    // SqlQuery<int> kullanılmaz: EF sorguyu alt sorguya sarar, NEXT VALUE FOR alt sorguda yasak. Düz komut çalıştırılır.
    private async Task<int> NextInvoiceSequenceAsync(CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT NEXT VALUE FOR {PaymentDbContext.InvoiceSequenceName}";
            return Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
    }

    private Invoice BuildInvoice(PaymentEntity payment, string? description, int sequence, DateTime now)
    {
        var (subtotal, tax) = InvoiceCalculator.SplitTaxInclusive(payment.Amount, _billing.TaxPercent);
        var invoiceId = Guid.CreateVersion7();
        return new Invoice
        {
            Id = invoiceId,
            InvoiceNumber = InvoiceCalculator.FormatNumber(now.Year, sequence),
            PaymentId = payment.Id,
            Payment = payment,
            PatientId = payment.PatientId,
            DoctorId = payment.DoctorId,
            IssuedAt = now,
            Currency = payment.Currency,
            Subtotal = subtotal,
            TaxPercent = _billing.TaxPercent,
            TaxAmount = tax,
            Total = subtotal + tax,
            Lines =
            [
                new InvoiceLine
                {
                    Id = Guid.CreateVersion7(),
                    InvoiceId = invoiceId,
                    Description = string.IsNullOrWhiteSpace(description) ? DefaultDescription : description.Trim(),
                    Quantity = 1,
                    UnitPrice = subtotal,
                    LineTotal = subtotal,
                },
            ],
        };
    }

    private string? ValidateRequest(string? key, CreatePaymentDto dto)
    {
        if (string.IsNullOrWhiteSpace(key)) return "Idempotency-Key başlığı zorunlu.";
        if (!IdempotencyKeyRules.IsValid(key)) return IdempotencyKeyRules.Message;
        if (dto is null) return "İstek gövdesi boş.";
        if (dto.AppointmentId == Guid.Empty) return "AppointmentId boş olamaz.";
        if (string.IsNullOrWhiteSpace(dto.PatientId) || dto.PatientId.Length > 64) return "PatientId zorunlu, en çok 64 karakter.";
        if (string.IsNullOrWhiteSpace(dto.DoctorId) || dto.DoctorId.Length > 64) return "DoctorId zorunlu, en çok 64 karakter.";
        if (dto.Amount is < 0.01m or > 1_000_000m) return "Tutar 0,01 ile 1.000.000 arasında olmalı.";
        if (decimal.Round(dto.Amount, 2) != dto.Amount) return "Tutar en çok 2 ondalık basamak içerebilir.";
        if (string.IsNullOrEmpty(dto.Currency) || !_billing.AllowedCurrencies.Contains(dto.Currency, StringComparer.Ordinal))
            return $"Para birimi şunlardan biri olmalı: {string.Join(", ", _billing.AllowedCurrencies)}.";
        if (dto.Description is { Length: > 200 }) return "Açıklama en çok 200 karakter olabilir.";
        if (dto.Card is null) return "Kart bilgisi zorunlu.";
        if (string.IsNullOrWhiteSpace(dto.Card.HolderName) || dto.Card.HolderName.Length > 100) return "Kart sahibi adı zorunlu, en çok 100 karakter.";
        if (dto.Card.Number is null || dto.Card.Cvv is null) return "Kart bilgisi eksik.";

        // Kart hataları kart verisini ASLA mesaja koymaz.
        return CardValidator.Validate(dto.Card.Number, dto.Card.ExpMonth, dto.Card.ExpYear, dto.Card.Cvv, DateTime.UtcNow);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    public async Task<OperationResult<PagedResult<PaymentResultDto>>> GetAllAsync(PaymentListQuery query, CancellationToken ct)
    {
        if (query.Page < 1 || query.PageSize is < 1 or > 50)
            return OperationResult<PagedResult<PaymentResultDto>>.Validation("page >= 1 ve pageSize 1-50 arasında olmalı.");

        // Sorgu burada yalnız tarif edilir; ToListAsync'e kadar SQL'e gitmez. Her filtre WHERE'e bir AND koşulu ekler.
        IQueryable<PaymentEntity> payments = _db.Payments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.PatientId)) payments = payments.Where(p => p.PatientId == query.PatientId);
        if (!string.IsNullOrWhiteSpace(query.DoctorId)) payments = payments.Where(p => p.DoctorId == query.DoctorId);
        if (query.AppointmentId is { } appointmentId) payments = payments.Where(p => p.AppointmentId == appointmentId);
        if (query.Status is { } status) payments = payments.Where(p => p.Status == status);

        var total = await payments.CountAsync(ct);
        var items = await payments
            .Include(p => p.Invoice)
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ToListAsync(ct);

        return OperationResult<PagedResult<PaymentResultDto>>.Ok(new PagedResult<PaymentResultDto>
        {
            Items = _mapper.Map<List<PaymentResultDto>>(items),
            TotalCount = total,
            Page = query.Page,
            PageSize = query.PageSize,
        });
    }

    public async Task<PaymentResultDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var payment = await _db.Payments.AsNoTracking().Include(p => p.Invoice).FirstOrDefaultAsync(p => p.Id == id, ct);
        return payment is null ? null : _mapper.Map<PaymentResultDto>(payment);
    }

    // İade akışı (yarışsız): Status iade BİTENE kadar Paid kalır, böylece "bir randevu = bir Paid" indeksi aynı randevuya
    // yeni ödeme girmesini engellemeye devam eder. Talep, RefundRequestedAt işaretiyle tek atomik UPDATE'te alınır.
    public async Task<OperationResult<PaymentResultDto>> RefundAsync(Guid id, CancellationToken ct)
    {
        var payment = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (payment is null) return OperationResult<PaymentResultDto>.NotFound();

        if (payment.Status != PaymentStatus.Paid)
            return OperationResult<PaymentResultDto>.Conflict($"{payment.Status} durumundaki ödeme iade edilemez.");

        ct.ThrowIfCancellationRequested();
        var now = DateTime.UtcNow;

        // 1) Talep işareti: UPDATE Payments SET RefundRequestedAt=@now WHERE Id=@id AND Status=N'Paid' AND RefundRequestedAt IS NULL
        var marked = await _db.Payments
            .Where(p => p.Id == id && p.Status == PaymentStatus.Paid && p.RefundRequestedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.RefundRequestedAt, (DateTime?)now), CancellationToken.None);
        if (marked == 0)
            return OperationResult<PaymentResultDto>.Conflict("İade zaten talep edildi ya da ödeme artık Paid değil.");

        // 2) Sağlayıcı iadesi (istemci iptali taşınmaz). İstisna da red sayılır.
        RefundResult refund;
        var providerFailed = false;
        try
        {
            refund = await _provider.RefundAsync(payment.ProviderReference ?? string.Empty, payment.Amount, payment.Currency, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError("Sağlayıcı iadesi hata verdi, ödeme {PaymentId}: {ExceptionType}: {ExceptionMessage}",
                id, ex.GetType().Name, ex.Message);
            refund = new RefundResult(false, "Sağlayıcıya ulaşılamadı");
            providerFailed = true;
        }

        if (!refund.Success)
        {
            // İşareti geri al: Status hiç değişmediği için indeks boyunca korundu, geri alma çakışamaz.
            await ClearRefundMarkerAsync(id, payment.ProviderReference);
            return providerFailed
                ? OperationResult<PaymentResultDto>.BadGateway("İade işlenemedi.")
                : OperationResult<PaymentResultDto>.Conflict("Sağlayıcı iadeyi reddetti: " + (refund.FailureReason ?? "bilinmeyen neden"));
        }

        // 3) Sağlayıcı iade etti: durumu kapat.
        try
        {
            var completed = await _db.Payments
                .Where(p => p.Id == id && p.Status == PaymentStatus.Paid && p.RefundRequestedAt != null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, PaymentStatus.Refunded)
                    .SetProperty(p => p.RefundedAt, (DateTime?)DateTime.UtcNow), CancellationToken.None);
            if (completed == 0)
            {
                _logger.LogCritical("İade sağlayıcıda yapıldı ama durum güncellenemedi (0 satır), ödeme {PaymentId}, sağlayıcı referansı {ProviderReference}",
                    id, payment.ProviderReference);
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical("İade sağlayıcıda yapıldı ama durum güncellenemedi, ödeme {PaymentId}, sağlayıcı referansı {ProviderReference}: {ExceptionType}: {ExceptionMessage}",
                id, payment.ProviderReference, ex.GetType().Name, ex.Message);
            return OperationResult<PaymentResultDto>.BadGateway("İade yapıldı ancak kayıt güncellenemedi; elle incelenmeli.");
        }

        // Fatura SİLİNMEZ: yasal belge. İade yalnız ödemenin durumunu değiştirir.
        var updated = await GetByIdAsync(id, CancellationToken.None);
        return OperationResult<PaymentResultDto>.Ok(updated!);
    }

    private async Task ClearRefundMarkerAsync(Guid id, string? providerReference)
    {
        try
        {
            await _db.Payments.Where(p => p.Id == id && p.Status == PaymentStatus.Paid)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.RefundRequestedAt, (DateTime?)null), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogCritical("İade işareti temizlenemedi, ödeme {PaymentId}, sağlayıcı referansı {ProviderReference}: {ExceptionType}: {ExceptionMessage}",
                id, providerReference, ex.GetType().Name, ex.Message);
        }
    }
}
