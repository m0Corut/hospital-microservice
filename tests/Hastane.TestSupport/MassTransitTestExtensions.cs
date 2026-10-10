using Microsoft.Extensions.DependencyInjection;

namespace Hastane.TestSupport;

// MassTransit 8.x'te AddMassTransit ikinci kez çağrılamaz ("may only be called once per container") ve RemoveMassTransit yoktur.
// Üretim kaydı (Program.cs) test harness'ı eklenmeden önce kaldırılır: MassTransit derlemelerinden gelen her kayıt silinir
// (hosted service'ler, seçenekler, bus, outbox teslim servisi). Sonra test AddMassTransitTestHarness ile kendi kaydını yapar.
public static class MassTransitTestExtensions
{
    public static IServiceCollection RemoveMassTransit(this IServiceCollection services)
    {
        foreach (var descriptor in services.Where(IsMassTransit).ToList())
            services.Remove(descriptor);
        return services;
    }

    private static bool IsMassTransit(ServiceDescriptor d)
        => IsMassTransitType(d.ServiceType)
           || IsMassTransitType(d.ImplementationType)
           || IsMassTransitType(d.ImplementationInstance?.GetType())
           || (d.IsKeyedService && (IsMassTransitType(d.KeyedImplementationType) || IsMassTransitType(d.KeyedImplementationInstance?.GetType())));

    // Tür kendisi ya da genel bağımsız değişkenlerinden biri MassTransit ad alanındaysa (ör. IConfigureOptions<MassTransitHostOptions>).
    private static bool IsMassTransitType(Type? type)
    {
        if (type is null) return false;
        if (type.Namespace?.StartsWith("MassTransit", StringComparison.Ordinal) == true) return true;
        return type.IsGenericType && type.GetGenericArguments().Any(IsMassTransitType);
    }
}
