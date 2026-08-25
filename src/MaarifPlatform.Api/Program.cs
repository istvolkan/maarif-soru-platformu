using System.Text;
using MaarifPlatform.Infrastructure;
using MaarifPlatform.Infrastructure.Auth;
using MaarifPlatform.Infrastructure.Configuration;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Sprint 11: system_settings tablosundaki değerler appsettings.json'ın ÜZERİNE katman olarak
// eklenir (DB değeri varsa kazanır) — Admin Ayarlar ekranından (Web projesi) değiştirilen
// API anahtarları/sağlayıcılar burada da (Api projesinde) yeniden başlatma gerektirmeden
// etkili olur, çünkü ikisi aynı system_settings tablosunu okur/yazar.
var connStr = builder.Configuration.GetConnectionString("MaarifDb")
    ?? throw new InvalidOperationException("ConnectionStrings:MaarifDb tanımlı değil.");
var dbSettings = new DatabaseSettingsProvider(connStr);
((IConfigurationBuilder)builder.Configuration).Add(new DatabaseSettingsSource(dbSettings));
builder.Services.AddSingleton(dbSettings);

builder.Services.AddMaarifPlatformCore(builder.Configuration);

// Sprint 7 Auth/RBAC. JWT bearer, refresh token YOK (bilinçli MVP sınırı — bkz. README).
var jwt = builder.Configuration.GetSection("Auth:Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("Auth:Jwt tanımlı değil.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
        ClockSkew = TimeSpan.FromMinutes(1)
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// §L/§20 MVP: altyapı ayakta mı ve DB'ye ulaşılabiliyor mu diye hızlı kontrol.
app.MapGet("/health", async (MaarifDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return Results.Ok(new { status = "ok", database = canConnect ? "connected" : "unreachable" });
});

using (var scope = app.Services.CreateScope())
{
    await BootstrapAdminInitializer.EnsureSeededAsync(scope.ServiceProvider);
}

app.Run();
