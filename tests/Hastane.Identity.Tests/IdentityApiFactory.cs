using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Hastane.Identity.Tests;

// Test sınıfı başına gerçek bir SQL Server konteyneri açar; bitince siler.
// Migration'lar ve rol tohumlama uygulama açılışında (Development) kendiliğinden çalışır.
public class IdentityApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async ValueTask InitializeAsync() => await _sql.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var cs = new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = "hastane_identity_test" };
        builder.UseSetting("ConnectionStrings:IdentityDb", cs.ConnectionString);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }
}
