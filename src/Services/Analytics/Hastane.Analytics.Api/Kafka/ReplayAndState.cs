using Hastane.Analytics.Api.Dtos;

namespace Hastane.Analytics.Api.Kafka;

// Tüketicinin dışarıya gösterdiği anlık durum (status ucu okur). Tüketici döngüsü yazar.
public sealed class KafkaConnectionState
{
    private volatile bool _connected;

    public bool Enabled { get; set; }

    // Partition atandı ya da mesaj okundu = broker'a bağlı. "Tüm broker'lar kapalı" hatası gelince false olur.
    public bool Connected
    {
        get => _connected;
        set => _connected = value;
    }
}

public sealed record RebuildOutcome(bool Ok, string? Error, RebuildResultDto? Result);

// Yeniden oynatma isteği ile tüketici döngüsü arasındaki köprü.
// Neden köprü: Kafka tüketici nesnesi iş parçacığı güvenli DEĞİL; offset'i yalnız onu okuyan döngü değiştirebilir.
// HTTP isteği "başa sar" talebini bırakır, döngü bir sonraki turunda alıp uygular ve sonucu bildirir.
public sealed class ReplayCoordinator
{
    private readonly object _gate = new();
    private TaskCompletionSource<RebuildOutcome>? _pending;

    /// <summary>Talep bırakır. Zaten bekleyen bir talep varsa null (aynı anda tek yeniden hesaplama).</summary>
    public Task<RebuildOutcome>? Request()
    {
        lock (_gate)
        {
            if (_pending is not null) return null;
            _pending = new TaskCompletionSource<RebuildOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _pending.Task;
        }
    }

    /// <summary>Tüketici döngüsü çağırır: bekleyen talep varsa alır.</summary>
    public bool TryTake(out TaskCompletionSource<RebuildOutcome> pending)
    {
        lock (_gate)
        {
            if (_pending is null)
            {
                pending = null!;
                return false;
            }
            pending = _pending;
            _pending = null;
            return true;
        }
    }

    /// <summary>İstek zaman aşımına uğradıysa talebi geri çek (hâlâ alınmamışsa).</summary>
    public void Withdraw(Task<RebuildOutcome> task)
    {
        lock (_gate)
        {
            if (_pending is not null && ReferenceEquals(_pending.Task, task)) _pending = null;
        }
    }
}
