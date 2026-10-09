using Hastane.Identity.Api.Data;
using Hastane.Identity.Api.Entities;
using Hastane.Identity.Api.Mapping;
using Hastane.Identity.Api.Services;
using Mapster;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

TypeAdapterConfig.GlobalSettings.Scan(typeof(UserMappingConfig).Assembly);
builder.Services.AddMapster();

// DbContext Scoped: istek başına bir örnek (bir istekte yapılan değişiklikler ve sorgular aynı bağlamı paylaşır).
builder.Services.AddDbContext<HastaneIdentityDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("IdentityDb")));

// AddIdentityCore: yalnız kullanıcı/rol/şifre yönetimi. Cookie ya da JWT şeması EKLENMEZ (JWT Dalga 3).
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
// SignInManager kimlik doğrulama altyapısını (şema sağlayıcısı) ister; şema eklemeden yalnız çekirdeği kaydederiz.
builder.Services.AddAuthenticationCore();

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAuthService, AuthService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Yalnız geliştirmede: veritabanı/tablolar yoksa kendisi kurulur. Üretimde şema değişikliği bilinçli yapılır.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<HastaneIdentityDbContext>().Database.MigrateAsync();
}

// Roller her ortamda garanti altına alınır (idempotent).
await RoleSeeder.SeedAsync(app.Services);

app.MapControllers();

app.Run();

// Test projesinin WebApplicationFactory<Program> kullanabilmesi için.
public partial class Program { }
