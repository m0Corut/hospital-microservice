using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using MySql.EntityFrameworkCore.Extensions;

namespace Hastane.Order.Api.Data;

// Oracle sağlayıcısının migration kilidi (GET_LOCK) MariaDB 11.4'te NULL dönüyor ve sağlayıcı sonucu Int64'e
// çevirirken InvalidCastException atıyor (DBNull -> Int64): açılıştaki Migrate() düşüyordu. Sağlayıcının
// MySQLHistoryRepository sınıfı internal olduğundan alt sınıf yazılamaz; bu yüzden DEKORATÖR: her şeyi içteki
// sağlayıcı nesnesine devreder, yalnız kilidi NULL-güvenli (GET_LOCK, pozitif zaman aşımı) yeniden yazar.
// Kilit oturum kapsamlıdır: iki Order örneği aynı anda açılırsa migration'ları sırayla uygular.
public class MariaDbHistoryRepository : IHistoryRepository
{
    private const string LockName = "hastane_order_migrations";
    private const int TimeoutSeconds = 60;

    private readonly IHistoryRepository _inner;
    private readonly IRelationalConnection _connection;

    public MariaDbHistoryRepository(HistoryRepositoryDependencies dependencies)
    {
        _connection = dependencies.Connection;
        var innerType = System.Reflection.Assembly.Load("MySql.EntityFrameworkCore")
            .GetType("MySql.EntityFrameworkCore.Migrations.Internal.MySQLHistoryRepository", throwOnError: true)!;
        _inner = (IHistoryRepository)Activator.CreateInstance(innerType, dependencies)!;
    }

    public LockReleaseBehavior LockReleaseBehavior => _inner.LockReleaseBehavior;

    public bool Exists() => _inner.Exists();
    public Task<bool> ExistsAsync(CancellationToken cancellationToken = default) => _inner.ExistsAsync(cancellationToken);
    public void Create() => _inner.Create();
    public Task CreateAsync(CancellationToken cancellationToken = default) => _inner.CreateAsync(cancellationToken);
    public bool CreateIfNotExists() => _inner.CreateIfNotExists();
    public Task<bool> CreateIfNotExistsAsync(CancellationToken cancellationToken = default) => _inner.CreateIfNotExistsAsync(cancellationToken);
    public IReadOnlyList<HistoryRow> GetAppliedMigrations() => _inner.GetAppliedMigrations();
    public Task<IReadOnlyList<HistoryRow>> GetAppliedMigrationsAsync(CancellationToken cancellationToken = default) => _inner.GetAppliedMigrationsAsync(cancellationToken);
    public string GetCreateScript() => _inner.GetCreateScript();
    public string GetCreateIfNotExistsScript() => _inner.GetCreateIfNotExistsScript();
    public string GetInsertScript(HistoryRow row) => _inner.GetInsertScript(row);
    public string GetDeleteScript(string migrationId) => _inner.GetDeleteScript(migrationId);
    public string GetBeginIfNotExistsScript(string migrationId) => _inner.GetBeginIfNotExistsScript(migrationId);
    public string GetBeginIfExistsScript(string migrationId) => _inner.GetBeginIfExistsScript(migrationId);
    public string GetEndIfScript() => _inner.GetEndIfScript();

    public IMigrationsDatabaseLock AcquireDatabaseLock()
    {
        _connection.Open();
        Take();
        return new MariaDbLock(this);
    }

    public async Task<IMigrationsDatabaseLock> AcquireDatabaseLockAsync(CancellationToken cancellationToken = default)
    {
        await _connection.OpenAsync(cancellationToken);
        await TakeAsync(cancellationToken);
        return new MariaDbLock(this);
    }
    private void Take()
    {
        using var command = Command(_connection.DbConnection);
        Check(command.ExecuteScalar());
    }

    private async Task TakeAsync(CancellationToken ct)
    {
        using var command = Command(_connection.DbConnection);
        Check(await command.ExecuteScalarAsync(ct));
    }

    private static DbCommand Command(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"SELECT GET_LOCK('{LockName}', {TimeoutSeconds})";
        command.CommandType = CommandType.Text;
        return command;
    }

    // 1 = alındı. NULL (hata) / 0 (zaman aşımı) = alınamadı: sessizce devam etmek yerine açık hata.
    private static void Check(object? result)
    {
        if (result is null or DBNull || Convert.ToInt64(result) != 1)
            throw new InvalidOperationException("Migration kilidi (GET_LOCK) alınamadı.");
    }

    private void Release()
    {
        try
        {
            using var command = _connection.DbConnection.CreateCommand();
            command.CommandText = $"SELECT RELEASE_LOCK('{LockName}')";
            command.ExecuteScalar();
        }
        finally
        {
            _connection.Close();
        }
    }

    private sealed class MariaDbLock(MariaDbHistoryRepository repository) : IMigrationsDatabaseLock
    {
        public IHistoryRepository HistoryRepository => repository;

        public void Dispose() => repository.Release();

        public ValueTask DisposeAsync()
        {
            repository.Release();
            return ValueTask.CompletedTask;
        }

        // Bağlantı yeniden açıldıysa oturum kilidi kaybolmuştur: tekrar al.
        public IMigrationsDatabaseLock ReacquireIfNeeded(bool connectionReopened, bool? createdTable)
        {
            if (connectionReopened) repository.Take();
            return this;
        }

        public async Task<IMigrationsDatabaseLock> ReacquireIfNeededAsync(bool connectionReopened, bool? createdTable, CancellationToken cancellationToken = default)
        {
            if (connectionReopened) await repository.TakeAsync(cancellationToken);
            return this;
        }
    }
}
