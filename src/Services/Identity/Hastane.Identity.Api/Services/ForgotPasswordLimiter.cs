using System.Collections.Concurrent;

namespace Hastane.Identity.Api.Services;

public interface IForgotPasswordLimiter
{
    // true: gönderilebilir (sayaç artar). false: pencere dolu, GÖNDERİLMEZ.
    bool TryAcquire(string email);
}

// Aynı e-posta için 5 dk içinde en çok 3 e-posta. BELLEK İÇİ ve tek süreç: çok kopyalı kurulumda her kopya ayrı sayar
// (üretimde dağıtık sayaç/Redis gerekir). Var olmayan e-postalar da sayılır; yoksa sınırın kendisi kullanıcı var/yok sızdırırdı.
public sealed class ForgotPasswordLimiter(TimeProvider time) : IForgotPasswordLimiter
{
    private const int MaxPerWindow = 3;
    private const int MaxKeys = 10_000;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _hits = new();

    public bool TryAcquire(string email)
    {
        var now = time.GetUtcNow();
        var key = email.Trim().ToUpperInvariant();

        if (_hits.Count >= MaxKeys) Prune(now);
        // Hâlâ doluysa (10.000 farklı adresle doldurma saldırısı): e-posta GÖNDERİLMEZ. "Engelleme" seçilirse sayacı
        // olmayan bir kurbana sınırsız e-posta atılabilirdi; yanıt yine aynı 202, yalnız gönderim atlanır (pencere dolunca açılır).
        if (_hits.Count >= MaxKeys && !_hits.ContainsKey(key)) return false;

        var queue = _hits.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() >= Window) queue.Dequeue();
            if (queue.Count >= MaxPerWindow) return false;
            queue.Enqueue(now);
            return true;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var (key, queue) in _hits)
        {
            bool empty;
            lock (queue)
            {
                while (queue.Count > 0 && now - queue.Peek() >= Window) queue.Dequeue();
                empty = queue.Count == 0;
            }
            if (empty) _hits.TryRemove(key, out _);
        }
    }
}
