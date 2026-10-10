using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Hastane.Doctor.Api.Activity;

// Controller'ın tek bağımlılığı: "bu olayı kaydet" der, GERİ DÖNER. Kafka'yı beklemez, hata fırlatmaz.
public interface IDoctorActivityPublisher
{
    /// <summary>Olayı bellek kanalına koyar. Kapalıysa ya da kanal doluysa false (olay düşer); asla fırlatmaz, asla beklemez.</summary>
    bool TryPublish(DoctorActivityEvent activity);
}

// İstek yolu ile Kafka arasındaki tampon: sınırlı bellek içi kanal. İstek yalnız TryWrite yapar (mikrosaniye);
// asıl Kafka yazımı arka plandaki DoctorActivityProducerService'te olur.
// Neden sınırlı: Kafka uzun süre kapalı kalırsa kanal sınırsız büyüyüp belleği doldurmasın; dolunca YENİ olay düşer.
// Bu bir analitik ölçümdür, kayıt defteri değil: birkaç olay kaybı doktor aramasını bozmaktan ucuzdur.
public sealed class ChannelDoctorActivityPublisher : IDoctorActivityPublisher
{
    private readonly Channel<DoctorActivityEvent> _channel;
    private readonly ILogger<ChannelDoctorActivityPublisher> _logger;
    private long _dropped;

    public ChannelDoctorActivityPublisher(IOptions<KafkaOptions> options, ILogger<ChannelDoctorActivityPublisher> logger)
    {
        _logger = logger;
        Enabled = !string.IsNullOrWhiteSpace(options.Value.BootstrapServers);
        var capacity = options.Value.ChannelCapacity > 0 ? options.Value.ChannelCapacity : 10_000;
        _channel = Channel.CreateBounded<DoctorActivityEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait, // TryWrite dolu kanalda BEKLEMEZ, false döner
            SingleReader = true,
        });
    }

    public bool Enabled { get; }

    public ChannelReader<DoctorActivityEvent> Reader => _channel.Reader;

    /// <summary>Kanal dolu olduğu için düşen toplam olay sayısı (süreç ömrü boyunca).</summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    public bool TryPublish(DoctorActivityEvent activity)
    {
        if (!Enabled) return false;
        try
        {
            if (_channel.Writer.TryWrite(activity)) return true;

            var dropped = Interlocked.Increment(ref _dropped);
            // Log yağmuru olmasın: ilk düşüşte ve sonra her 1000'de bir.
            if (dropped == 1 || dropped % 1000 == 0)
                _logger.LogWarning("Etkinlik kanalı dolu, olay düşürüldü. Toplam düşen: {Dropped}", dropped);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Etkinlik kanala yazılamadı.");
            return false;
        }
    }
}
