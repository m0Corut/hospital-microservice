using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Shared.Security;

// Controller/servis "token'daki kullanıcı kim?" sorusunu HttpContext'e bulaşmadan sorsun.
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    string? UserId { get; }
    string? Email { get; }
    string? DoctorId { get; }
    IReadOnlyList<string> Roles { get; }
    bool IsAdmin { get; }
    bool IsInRole(string role);
}

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    public string? UserId => Principal?.FindFirst(HastaneClaims.Subject)?.Value;
    public string? Email => Principal?.FindFirst(HastaneClaims.Email)?.Value;
    public string? DoctorId => Principal?.FindFirst(HastaneClaims.DoctorId)?.Value;

    public IReadOnlyList<string> Roles =>
        Principal?.FindAll(HastaneClaims.Role).Select(c => c.Value).ToArray() ?? [];

    public bool IsAdmin => IsInRole(HastaneRoles.Admin);

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;
}

public static class CurrentUserExtensions
{
    public static IServiceCollection AddHastaneCurrentUser(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        return services;
    }
}
