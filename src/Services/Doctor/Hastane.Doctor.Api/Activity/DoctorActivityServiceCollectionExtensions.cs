namespace Hastane.Doctor.Api.Activity;

public static class DoctorActivityServiceCollectionExtensions
{
    // Kafka:BootstrapServers boşsa yayıncı etkisiz (TryPublish false) ve üretici hiç başlamaz: servis Kafka'sız çalışır.
    public static IServiceCollection AddDoctorActivity(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<KafkaOptions>(configuration.GetSection(KafkaOptions.SectionName));
        services.AddSingleton<ChannelDoctorActivityPublisher>();
        services.AddSingleton<IDoctorActivityPublisher>(sp => sp.GetRequiredService<ChannelDoctorActivityPublisher>());
        services.AddHostedService<DoctorActivityProducerService>();
        return services;
    }
}
