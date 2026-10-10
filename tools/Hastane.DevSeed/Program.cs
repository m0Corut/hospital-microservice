using System.Text;

namespace Hastane.DevSeed;

public static class Program
{
    private const string Usage = """
        Kullanım:
          dotnet run --project tools/Hastane.DevSeed -- --admin-email <e> --admin-password <p> [--gateway url] [--password <seed şifresi>] [--web-base url]

          --admin-email / --admin-password  Önceden var olan Admin hesabı (araç oluşturmaz, yalnız giriş yapar)
          --gateway    Gateway adresi (varsayılan http://localhost:5000)
          --password   Doktor ve hasta seed hesaplarının ortak şifresi (varsayılan Hastane123!)
          --web-base   Galeri görsellerinin mutlak adresi için WebUI kökü (varsayılan http://localhost:5010)
        """;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-h" or "--help")
            {
                Console.WriteLine(Usage);
                return 0;
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                Console.Error.WriteLine($"Anlaşılmayan argüman: {arg}\n\n{Usage}");
                return 1;
            }

            values[arg[2..]] = args[++i];
        }

        if (!values.TryGetValue("admin-email", out var adminEmail) || !values.TryGetValue("admin-password", out var adminPassword))
        {
            Console.Error.WriteLine($"--admin-email ve --admin-password zorunlu.\n\n{Usage}");
            return 1;
        }

        var options = new SeedOptions(
            Gateway: values.GetValueOrDefault("gateway", "http://localhost:5000").TrimEnd('/'),
            AdminEmail: adminEmail,
            AdminPassword: adminPassword,
            SeedPassword: values.GetValueOrDefault("password", "Hastane123!"),
            WebBase: values.GetValueOrDefault("web-base", "http://localhost:5010").TrimEnd('/'));

        using var http = new HttpClient { BaseAddress = new Uri(options.Gateway + "/"), Timeout = TimeSpan.FromSeconds(60) };
        var report = new Report();
        var seeder = new Seeder(new GatewayApi(http), options, report);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            var loggedIn = await seeder.RunAsync(cts.Token);
            report.Print(options);
            return loggedIn && report.ErrorCount == 0 ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("İptal edildi.");
            return 1;
        }
    }
}

public sealed record SeedOptions(string Gateway, string AdminEmail, string AdminPassword, string SeedPassword, string WebBase);
