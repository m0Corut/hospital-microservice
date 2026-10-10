namespace Hastane.DevSeed;

// Adım bazında oluşturulan / atlanan sayıları ve hata sayısını tutar; sonda özet tabloyu yazar.
public sealed class Report
{
    private readonly Dictionary<string, (int Created, int Skipped, int Errors)> _steps = [];
    private readonly List<string> _order = [];

    public int ErrorCount { get; private set; }

    public void Created(string step, int count = 1) => Update(step, c => (c.Created + count, c.Skipped, c.Errors));

    public void Skipped(string step, int count = 1) => Update(step, c => (c.Created, c.Skipped + count, c.Errors));

    public void Note(string message) => Console.WriteLine($"  . {message}");

    public void Error(string step, string what, string? detail)
    {
        ErrorCount++;
        Update(step, c => (c.Created, c.Skipped, c.Errors + 1));
        Console.Error.WriteLine($"  [HATA] {step} / {what}: {detail}");
    }

    public void Error<T>(string step, string what, ApiResult<T> result) => Error(step, what, result.ToString());

    public void Print(SeedOptions options)
    {
        Console.WriteLine();
        Console.WriteLine("=== Özet ===");
        Console.WriteLine($"{"Adım",-22} {"Oluşturulan",11} {"Atlanan",8} {"Hata",5}");
        foreach (var step in _order)
        {
            var (created, skipped, errors) = _steps[step];
            Console.WriteLine($"{step,-22} {created,11} {skipped,8} {errors,5}");
        }

        Console.WriteLine();
        Console.WriteLine($"Toplam hata: {ErrorCount}");
        Console.WriteLine();
        Console.WriteLine("=== Giriş bilgileri (ortak şifre: " + options.SeedPassword + ") ===");
        Console.WriteLine("Doktorlar:");
        foreach (var doctor in SeedData.Doctors)
            Console.WriteLine($"  {doctor.Email,-38} {doctor.Title} {doctor.FullName} ({doctor.Branch})");
        Console.WriteLine("Hastalar:");
        foreach (var patient in SeedData.Patients)
            Console.WriteLine($"  {patient.Email,-38} {patient.FirstName} {patient.LastName}");
    }

    private void Update(string step, Func<(int Created, int Skipped, int Errors), (int, int, int)> change)
    {
        if (!_steps.TryGetValue(step, out var current))
        {
            current = (0, 0, 0);
            _order.Add(step);
        }

        _steps[step] = change(current);
    }
}
