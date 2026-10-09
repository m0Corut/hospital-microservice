using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Shared.Security;

public static class HastanePolicies
{
    public const string AdminOnly = nameof(AdminOnly);
    public const string DoctorOnly = nameof(DoctorOnly);
    public const string PatientOnly = nameof(PatientOnly);
    public const string DoctorOrAdmin = nameof(DoctorOrAdmin);
    public const string AuthenticatedUser = nameof(AuthenticatedUser);

    // Politika = "bu kapıdan kim geçebilir" kuralının adı. Controller'da [Authorize(Policy = HastanePolicies.AdminOnly)].
    public static IServiceCollection AddHastaneAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(AdminOnly, p => p.RequireRole(HastaneRoles.Admin))
            .AddPolicy(DoctorOnly, p => p.RequireRole(HastaneRoles.Doctor))
            .AddPolicy(PatientOnly, p => p.RequireRole(HastaneRoles.Patient))
            .AddPolicy(DoctorOrAdmin, p => p.RequireRole(HastaneRoles.Doctor, HastaneRoles.Admin))
            .AddPolicy(AuthenticatedUser, p => p.RequireAuthenticatedUser());
        return services;
    }
}
