namespace Hastane.Pharmacy.Api.Validation;

public sealed record ValidationFailure(string Property, string Message);

// Basit doğrulayıcı arayüzü (FluentValidation yok, bilerek). Her komut/sorgu için en çok bir sınıf yazılır;
// ValidationBehavior hepsini toplayıp çalıştırır, handler yalnız GEÇERLİ istek görür.
public interface IValidator<in T>
{
    IEnumerable<ValidationFailure> Validate(T request);
}

public static class ValidatorRegistration
{
    // Derlemedeki her IValidator<T> uygulamasını bulup kaydeder: yeni validator yazınca Program.cs'e dokunulmaz.
    public static IServiceCollection AddValidatorsFromAssembly(this IServiceCollection services, System.Reflection.Assembly assembly)
    {
        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            foreach (var contract in type.GetInterfaces()
                         .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>)))
            {
                services.AddTransient(contract, type);
            }
        }

        return services;
    }
}
