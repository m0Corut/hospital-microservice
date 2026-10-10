using MassTransit.Testing;

namespace Hastane.TestSupport;

// ÖĞRETİM: harness'ın SelectAsync/Any metotları "bus hareketsiz kalana kadar" (TestInactivityTimeout, 1,2 sn) bekler; hareketsizlik
// belirteci bir kez dolunca (paylaşılan fixture'da ilk testten sonra) kalıcı olarak iptal kalır ve bu metotlar ANINDA döner.
// Outbox teslimi ise HTTP yanıtından SONRA, arka planda olur: yanıt geldiğinde olay henüz taşımada değildir.
// Bu yardımcılar hareketsizliğe değil, açık bir zaman aşımına göre yoklar: olay outbox'tan teslim edilene kadar bekler.
public static class OutboxHarnessExtensions
{
    // Pozitif bekleme üst sınırı: outbox teslim aralığının (TestQueryDelay) çok üstünde; olay gelince hemen döner.
    public static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);

    // "Olay YOK" iddiası için gözlem penceresi: bu sürede gelmeyen olay, outbox'a yazılmamış sayılır (teslim ~50 ms'de olur).
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    // Filtreye uyan, outbox'tan teslim edilip test dinleyicisine ulaşan ilk olay; süre içinde gelmezse null.
    public static async Task<IReceivedMessage<T>?> ConsumedEventAsync<T>(this ITestHarness harness,
        FilterDelegate<IReceivedMessage<T>> filter, CancellationToken cancellationToken, TimeSpan? timeout = null)
        where T : class
    {
        var deadline = DateTime.UtcNow + (timeout ?? EventTimeout);
        while (true)
        {
            var match = harness.Consumed.Select<T>(filter, cancellationToken).FirstOrDefault();
            if (match is not null || DateTime.UtcNow >= deadline) return match;
            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    // Tekillik (Assert.Single) iddiasından önce: ilk olay geldikten sonra olası bir kopyanın da teslim edilebileceği süre kadar bekle.
    // Böylece "tam bir olay" iddiası, geç gelen bir kopyayı kaçırmaz.
    public static Task WaitQuietPeriodAsync(CancellationToken cancellationToken) => Task.Delay(QuietPeriod, cancellationToken);

    public static async Task<bool> AnyConsumedAsync<T>(this ITestHarness harness,
        FilterDelegate<IReceivedMessage<T>> filter, CancellationToken cancellationToken, TimeSpan? timeout = null)
        where T : class
        => await harness.ConsumedEventAsync(filter, cancellationToken, timeout) is not null;
}
