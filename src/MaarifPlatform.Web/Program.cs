using MaarifPlatform.Infrastructure;
using MaarifPlatform.Infrastructure.Auth;
using MaarifPlatform.Infrastructure.Configuration;
using MaarifPlatform.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Admin/Books ve Admin/ReferenceDocuments sayfalarındaki InputFile ile büyük PDF'ler (ders
// kitapları onlarca MB olabilir) yüklenebilsin diye SignalR circuit hub'ının varsayılan 32KB
// mesaj boyutu sınırı yükseltilir — BooksController.MaxFileSizeBytes (200MB) ile aynı üst sınır.
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
    options.MaximumReceiveMessageSize = 200 * 1024 * 1024);

// Sprint 11: system_settings tablosundaki değerler appsettings.json'ın ÜZERİNE katman olarak
// eklenir — bkz. MaarifPlatform.Api/Program.cs'teki aynı blok, ikisi de aynı tabloyu okur/yazar,
// bu yüzden Admin/Settings ekranından yapılan bir değişiklik her iki host'ta da (yeniden
// başlatma gerekmeden) etkili olur.
var connStr = builder.Configuration.GetConnectionString("MaarifDb")
    ?? throw new InvalidOperationException("ConnectionStrings:MaarifDb tanımlı değil.");
var dbSettings = new DatabaseSettingsProvider(connStr);
((IConfigurationBuilder)builder.Configuration).Add(new DatabaseSettingsSource(dbSettings));
builder.Services.AddSingleton(dbSettings);

builder.Services.AddMaarifPlatformCore(builder.Configuration);

// Bu proje JWT bearer katmanına hiç dokunmaz — kendi cookie auth şeması var, ama parola
// doğrulaması aynı AuthService.LoginAsync'i kullanır (bkz. Login.razor).
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/erisim-engellendi";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Sign-out da (SignIn gibi) gerçek bir HTTP isteği gerektirir — interaktif circuit içinden
// değil, NavMenu'deki plain <form method="post"> ile buraya post edilir.
app.MapPost("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/login");
});

using (var scope = app.Services.CreateScope())
{
    await BootstrapAdminInitializer.EnsureSeededAsync(scope.ServiceProvider);
}

app.Run();
