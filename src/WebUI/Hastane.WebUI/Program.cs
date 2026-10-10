using Hastane.WebUI.Infrastructure;
using Hastane.WebUI.Infrastructure.Gateway;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
{
    // Tüm POST/PUT/DELETE istekleri antiforgery belirteci ister (GET hariç). Tek tek [ValidateAntiForgeryToken] gerekmez.
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    // API'den gelen 401/403/404/servis-kapalı tek yerde kullanıcı davranışına çevrilir.
    options.Filters.Add<GatewayExceptionFilter>();
});

// Profil fotoğrafı yüklemesi (2 MB sınırlı) diske değil bellekte tamponlansın: eşik gövde sınırından büyük.
// Antiforgery denetimi formu eylemden önce okuduğu için ayar eylem özniteliğiyle değil burada, genel verilir.
builder.Services.Configure<FormOptions>(options =>
{
    options.MemoryBufferThreshold = 3 * 1024 * 1024;
    options.MultipartBodyLengthLimit = 3 * 1024 * 1024;
});

builder.Services.AddWebUiDataProtection(builder.Configuration, builder.Environment);
builder.Services.AddWebUiAuthentication(builder.Configuration);
builder.Services.AddWebUiGateway(builder.Configuration);

var app = builder.Build();

// Geliştirici hata sayfası bilerek YOK: Development'ta da kullanıcı yığın izi görmez; ayrıntı günlükte.
app.UseExceptionHandler("/Error/500");
app.UseStatusCodePagesWithReExecute("/Error/{0}");

if (!app.Environment.IsDevelopment()) app.UseHsts();

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Alanlar önce: /Admin, /Doctor, /Patient ve altı. Herkese açık sayfalar kök alanda (Controllers/).
app.MapControllerRoute(name: "areas", pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

// Test projesinin WebApplicationFactory<Program> kullanabilmesi için.
public partial class Program { }
