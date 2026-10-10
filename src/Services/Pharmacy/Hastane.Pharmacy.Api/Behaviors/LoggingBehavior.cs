using System.Diagnostics;
using MediatR;

namespace Hastane.Pharmacy.Api.Behaviors;

// En dış halka: istek adı + süre. İstek GÖVDESİ loglanmaz (ilaç/hasta verisi, ileride kişisel veri olabilir).
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        var started = Stopwatch.GetTimestamp();
        try
        {
            var response = await next(cancellationToken);
            logger.LogInformation("{RequestName} tamamlandı ({ElapsedMs} ms)", name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return response;
        }
        catch (RequestValidationException)
        {
            // Beklenen durum (kullanıcı hatası): hata değil uyarı; ayrıntı yok.
            logger.LogWarning("{RequestName} doğrulamada reddedildi ({ElapsedMs} ms)", name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Yalnız istisna TÜRÜ: mesajı ve yığın izi güvenlik/gizlilik için burada yazılmaz (üst katman 500'ü yazar).
            logger.LogError("{RequestName} başarısız oldu: {ExceptionType} ({ElapsedMs} ms)", name, ex.GetType().Name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }
}
