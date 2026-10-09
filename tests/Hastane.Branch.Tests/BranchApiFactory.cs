using Hastane.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.MongoDb;

namespace Hastane.Branch.Tests;

// Test sınıfı başına gerçek bir MongoDB konteyneri açar; bitince siler.
public class BranchApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:7").Build();

    public async ValueTask InitializeAsync() => await _mongo.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Üretim açık anahtarı yerine test anahtarı: TestJwt ile üretilen token'lar doğrulanır.
        builder.UseTestJwt();
        builder.UseSetting("DatabaseSettings:ConnectionString", _mongo.GetConnectionString());
        builder.UseSetting("DatabaseSettings:DatabaseName", "hastane_branch_test");
        builder.UseSetting("DatabaseSettings:BranchCollectionName", "branches");
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _mongo.DisposeAsync();
    }
}
