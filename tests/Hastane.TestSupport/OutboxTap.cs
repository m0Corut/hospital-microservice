using MassTransit;

namespace Hastane.TestSupport;

// ÖĞRETİM: MassTransit test harness'ı "Published" listesini bus'ın yayın gözlemcisinden doldurur. EF "bus outbox" açıkken
// Publish çağrısı mesajı yalnız OutboxMessage tablosuna yazar; teslim servisi (BusOutboxDeliveryService) onu sonradan
// önceden serileştirilmiş gövdeyle taşımaya GÖNDERİR (Send). Bu yüzden harness.Published boş kalır (MassTransit'te bilinen
// sınırlama, bkz. MassTransit discussion #4874). Çözüm: outbox'ı kapatmak yerine olay türüne bağlı boş bir "dinleyici" eklemek.
// Teslim edilen olay bu dinleyiciye ulaşır ve harness.Consumed'da TÜRÜYLE görünür: olay gerçekten
// iş verisiyle aynı transaction'da outbox'a yazılmış, commit sonrası teslim edilmiş ve taşımadan geçmiş olur.
public sealed class OutboxTapConsumer<T> : IConsumer<T>
    where T : class
{
    public Task Consume(ConsumeContext<T> context) => Task.CompletedTask;
}

public static class OutboxTapExtensions
{
    // Testte outbox teslim aralığı: üretim varsayılanı (saniyeler) harness'ın hareketsizlik süresini (1,2 sn) aşabilir.
    public static readonly TimeSpan TestQueryDelay = TimeSpan.FromMilliseconds(50);

    // T olayı için ayrı kuyruklu dinleyici kaydeder (kuyruk adı türe özgü: farklı olaylar aynı kuyrukta karışmaz).
    public static IBusRegistrationConfigurator AddOutboxTap<T>(this IBusRegistrationConfigurator x)
        where T : class
    {
        x.AddConsumer<OutboxTapConsumer<T>>().Endpoint(e => e.Name = $"test-outbox-tap-{typeof(T).Name}");
        return x;
    }
}
