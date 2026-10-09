using Hastane.Identity.Api.Data;
using Hastane.Identity.Api.Entities;
using Hastane.Identity.Api.Mapping;
using Hastane.Identity.Api.Services;
using Hastane.Shared.Security;
using Mapster;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

TypeAdapterConfig.GlobalSettings.Scan(typeof(UserMappingConfig).Assembly);
builder.Services.AddMapster();

// DbContext Scoped: istek başına bir örnek (bir istekte yapılan değişiklikler ve sorgular aynı bağlamı paylaşır).
builder.Services.AddDbContext<HastaneIdentityDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("IdentityDb")));

// AddIdentityCore: yalnız kullanıcı/rol/şifre yönetimi (şifre hash'i, kilitleme). Giriş KARTI (token) ayrıca üretilir.
builder.Services.AddIdentityCore<AppUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = false;

        options.User.RequireUniqueEmail = true;

        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<HastaneIdentityDbContext>()
    .AddSignInManager();

// Gelen token'ı doğrulama (RS256, açık anahtar) + politikalar. Identity de kendi uçlarını bununla korur.
builder.Services.AddHastaneJwtAuthentication(builder.Configuration);
builder.Services.AddHastaneCurrentUser();

// Token ÜRETMEK Identity'ye özgü: özel anahtar zorunlu (yoksa açılışta durur).
builder.Services.AddSingleton<IValidateOptions<JwtOptions>, IdentityJwtOptionsValidator>();
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IAuthService, AuthService>();

var app = builder.Build();

// Yapılandırma hatası (özel anahtar yok/bozuk) veritabanına dokunmadan, anlaşılır mesajla burada patlasın.
_ = app.Services.GetRequiredService<IJwtTokenService>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Yalnız geliştirmede: veritabanı/tablolar yoksa kendisi kurulur. Üretimde şema değişikliği bilinçli yapılır.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<HastaneIdentityDbContext>().Database.MigrateAsync();
}

// Roller her ortamda garanti altına alınır (idempotent).
await RoleSeeder.SeedAsync(app.Services);
// İlk Admin: yalnız Seed:AdminEmail/AdminPassword verilmişse ve hiç Admin yoksa.
await AdminSeeder.SeedAsync(app.Services);

// Sıra önemli: önce "kimsin" (token'ı çöz), sonra "iznin var mı".
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Test projesinin WebApplicationFactory<Program> kullanabilmesi için.
public partial class Program { }
