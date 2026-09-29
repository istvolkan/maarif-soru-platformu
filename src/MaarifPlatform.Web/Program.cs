using MaarifPlatform.Application.Storage;
using MaarifPlatform.Infrastructure;
using MaarifPlatform.Infrastructure.Auth;
using MaarifPlatform.Infrastructure.Configuration;
using MaarifPlatform.Infrastructure.Export;
using MaarifPlatform.Infrastructure.Persistence;
using MaarifPlatform.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
    builder.Configuration.AddJsonFile("appsettings.Development.local.json", optional: true, reloadOnChange: false);
// Deployment secrets take precedence over local development overrides.
builder.Configuration.AddEnvironmentVariables();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// InputFile streams files in chunks; the 200 MB file limit is independent of message size.
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
    options.MaximumReceiveMessageSize = 32 * 1024);

// Sprint 11: system_settings tablosundaki değerler appsettings.json'ın ÜZERİNE katman olarak
// eklenir — bkz. MaarifPlatform.Api/Program.cs'teki aynı blok, ikisi de aynı tabloyu okur/yazar,
// bu yüzden Admin/Settings ekranından yapılan bir değişiklik her iki host'ta da (yeniden
// başlatma gerekmeden) etkili olur.
var connStr = builder.Configuration.GetConnectionString("MaarifDb")
    ?? throw new InvalidOperationException("ConnectionStrings:MaarifDb tanımlı değil.");
var secrets = new SettingsSecretProtector(builder.Configuration["Security:SettingsEncryptionKey"]
    ?? throw new InvalidOperationException("Security:SettingsEncryptionKey is required; see SECURITY-REVISION.md."));
using var settingsLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
var dbSettings = new DatabaseSettingsProvider(connStr, secrets, settingsLoggerFactory.CreateLogger<DatabaseSettingsProvider>());
((IConfigurationBuilder)builder.Configuration).Add(new DatabaseSettingsSource(dbSettings));
builder.Services.AddSingleton(dbSettings);
builder.Services.AddSingleton(secrets);
builder.Services.AddSingleton<ISettingsReloader>(dbSettings);
builder.Services.AddHostedService<SettingsRefreshService>();

builder.Services.AddMaarifPlatformCore(builder.Configuration);

// Bu proje JWT bearer katmanına hiç dokunmaz — kendi cookie auth şeması var, ama parola
// doğrulaması ortak AuthService.ValidateCredentialsAsync'i kullanır.
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider,
    MaarifPlatform.Web.Security.RevalidatingSessionProvider>();
builder.Services.AddScoped<MaarifPlatform.Web.Security.OperationGuard>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/erisim-engellendi";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = async context =>
        {
            if (!await context.HttpContext.RequestServices.GetRequiredService<SessionValidator>()
                    .IsValidAsync(context.Principal!, context.HttpContext.RequestAborted))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });
// §18 Rol bazlı yetki matrisi — her Permission için, adı Permission.ToString() ile aynı bir
// policy tanımlanır; gerçek "bu rol bu yetkiye sahip mi" kontrolü PermissionAuthorizationHandler'da
// (Infrastructure/Auth) role_permissions tablosuna bakarak yapılır, [Authorize(Roles=...)]'daki
// gibi derleme zamanında sabitlenmez — Admin/Kullanıcılar > Yetkiler ekranından değiştirilebilir.
builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
builder.Services.AddLoginRateLimiting();

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
app.UseRateLimiter();
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

// PDF indirme de gerçek bir HTTP isteği gerektirir (dosya indirme, SignalR circuit üzerinden
// olmaz) — BookQuestions.razor'daki <a href> buraya doğrudan bağlanır, cookie auth ile korunur.
app.MapGet("/export/book/{bookId:guid}/pdf", async (Guid bookId, BookPdfExportService exportService, MaarifDbContext db, CancellationToken ct) =>
{
    var book = await db.Books.FirstOrDefaultAsync(b => b.Id == bookId, ct);
    if (book is null)
    {
        return Results.NotFound();
    }

    var pdfBytes = await exportService.GenerateAsync(bookId, ct);
    var fileName = $"{book.Title}-donusturulmus.pdf".Replace(' ', '-');
    return Results.File(pdfBytes, "application/pdf", fileName);
}).RequireAuthorization("QuestionPoolAccess");

// Maarif Uyum Puanı 50 altında kalıp otomatik Transform'a gönderilmeyen sorular için ayrı
// revizyon raporu (bkz. BookBatchTransformService.ProcessManualReviewAsync).
app.MapGet("/export/book/{bookId:guid}/revision-pdf", async (Guid bookId, BookPdfExportService exportService, MaarifDbContext db, CancellationToken ct) =>
{
    var book = await db.Books.FirstOrDefaultAsync(b => b.Id == bookId, ct);
    if (book is null)
    {
        return Results.NotFound();
    }

    var pdfBytes = await exportService.GenerateRevisionReportAsync(bookId, ct);
    var fileName = $"{book.Title}-revizyon-raporu.pdf".Replace(' ', '-');
    return Results.File(pdfBytes, "application/pdf", fileName);
}).RequireAuthorization("QuestionPoolAccess");

// Soru için PDF'ten çıkarılmış görsel (grafik/şekil sayfası) — QuestionDetail.razor'daki <img>
// buraya işaret eder. Görsel yoksa (RequiresVisual=false ya da hiç render edilmemişse) 404.
app.MapGet("/media/question/{questionId:guid}/visual", async (Guid questionId, MaarifDbContext db, IBookFileStorage storage, CancellationToken ct) =>
{
    var asset = await db.QuestionVisualAssets
        .Where(a => a.QuestionId == questionId)
        .OrderByDescending(a => a.CreatedAt)
        .FirstOrDefaultAsync(ct);
    if (asset is null)
    {
        return Results.NotFound();
    }

    var stream = await storage.OpenReadAsync(asset.StorageUri, ct);
    // ContentType Faz 1 öncesi kayıtlarda null'dır (o dönemde tek tür vardı: PDF-crop PNG'si) —
    // Faz 2'nin SVG'leri her zaman ContentType="image/svg+xml" ile kaydedilir.
    return Results.File(stream, asset.ContentType ?? "image/png");
}).RequireAuthorization("QuestionPoolAccess");

using (var scope = app.Services.CreateScope())
{
    await BootstrapAdminInitializer.EnsureSeededAsync(scope.ServiceProvider);
}

app.Run();
