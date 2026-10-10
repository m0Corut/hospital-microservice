using Hastane.Contracts;
using Hastane.Market.Api.Services;
using MassTransit;

namespace Hastane.Market.Api.Messaging;

// Sipariş iptal edilince stok iadesi. ÖĞRETİM: eskiden Order, Market'e senkron "release" HTTP çağrısı yapıyordu; Market kapalıysa
// iade "bekliyor" işaretiyle kalıyordu. Şimdi Order yalnız "OrderCancelled oldu" der (outbox'ta durur), Market hazır olunca iade eder.
// Idempotent: StockService.ReleaseAsync "Reserved -> Released" geçişini TEK koşullu UPDATE ile sahiplenir; olay iki kez gelse de
// stok bir kez eklenir (ikinci çağrı 0 satır etkiler). Rezervasyon hiç yoksa (stok yetmediği için iptal) zararsız no-op.
public class OrderCancelledConsumer(IStockService stock, ILogger<OrderCancelledConsumer> logger) : IConsumer<OrderCancelled>
{
    public async Task Consume(ConsumeContext<OrderCancelled> context)
    {
        var result = await stock.ReleaseAsync(context.Message.OrderId, context.CancellationToken);
        if (result.Status != OperationStatus.Ok)
        {
            // Yalnız boş orderId gibi KALICI hata: yeniden denemek boşuna, loglayıp geç.
            logger.LogWarning("OrderCancelled işlenemedi (sipariş {OrderId}): {Error}", context.Message.OrderId, result.Error);
        }
    }
}
