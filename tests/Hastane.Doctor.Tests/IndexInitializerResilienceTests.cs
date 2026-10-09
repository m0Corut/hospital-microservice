using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;

namespace Hastane.Doctor.Tests;

public class IndexInitializerResilienceTests
{
    [Fact]
    public async Task AppStarts_EvenWhenMongoClientCannotBeCreated()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(services =>
            {
                services.RemoveAll<IMongoClient>();
                services.AddSingleton<IMongoClient>(_ => throw new InvalidOperationException("bağlantı cümlesi geçersiz"));
            }));

        using var client = factory.CreateClient(); // host başlar; dizin hatası yalnız loglanır
        Assert.NotNull(client);
    }
}
