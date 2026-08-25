using System.Text;
using MaarifPlatform.Application.Auth;
using MaarifPlatform.Application.Extraction;
using MaarifPlatform.Application.Providers;
using MaarifPlatform.Application.Rag;
using MaarifPlatform.Application.Storage;
using MaarifPlatform.Application.Vision;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Infrastructure.Ai;
using MaarifPlatform.Infrastructure.Analysis;
using MaarifPlatform.Infrastructure.Auth;
using MaarifPlatform.Infrastructure.Extraction;
using MaarifPlatform.Infrastructure.Persistence;
using MaarifPlatform.Infrastructure.Rag;
using MaarifPlatform.Infrastructure.Storage;
using MaarifPlatform.Infrastructure.Vision;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("MaarifDb")
    ?? throw new InvalidOperationException("ConnectionStrings:MaarifDb tanımlı değil.");

builder.Services.AddDbContext<MaarifDbContext>(options =>
    options.UseNpgsql(connectionString, npg => npg.UseVector()));

// §10 PDF İşleme pipeline'ı.
builder.Services.Configure<LocalFileStorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.AddScoped<IBookFileStorage, LocalFileStorage>();
builder.Services.AddScoped<IPdfTextExtractor, DocnetTextExtractor>();
builder.Services.AddScoped<IQuestionSegmenter, HeuristicQuestionSegmenter>();
builder.Services.AddScoped<BookExtractionService>();

// §G RAG pipeline'ı. Embeddings:Provider varsayılanı "Local" — dış API anahtarı gerektirmez,
// yalnızca borunun mekaniğini doğrulamak içindir (bkz. LocalDeterministicEmbeddingProvider'daki not).
// Gerçek semantik retrieval için appsettings/user-secrets'ta "OpenAI" seçilip ApiKey girilmelidir.
builder.Services.AddScoped<IReferenceChunker, ParagraphReferenceChunker>();
builder.Services.Configure<OpenAIEmbeddingOptions>(builder.Configuration.GetSection("Embeddings:OpenAI"));
builder.Services.AddHttpClient<OpenAIEmbeddingProvider>();

var embeddingProviderName = builder.Configuration["Embeddings:Provider"] ?? "Local";
if (string.Equals(embeddingProviderName, "OpenAI", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IEmbeddingProvider>(sp => sp.GetRequiredService<OpenAIEmbeddingProvider>());
}
else
{
    builder.Services.AddScoped<IEmbeddingProvider, LocalDeterministicEmbeddingProvider>();
}

builder.Services.AddScoped<ReferenceIngestionService>();
builder.Services.AddScoped<ReferenceSearchService>();

// §4/§H Analysis pipeline'ı. Ai:Provider varsayılanı "Local" — dış API anahtarı gerektirmez,
// yalnızca yapısal sinyallere dayanan bir mock'tur (bkz. LocalHeuristicLLMProvider'daki not).
// Gerçek pedagojik/matematiksel değerlendirme için Ai:Provider=Anthropic + Ai:Anthropic:ApiKey gerekir.
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection("Ai:Anthropic"));

var aiProviderName = builder.Configuration["Ai:Provider"] ?? "Local";
if (string.Equals(aiProviderName, "Anthropic", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<ILLMProvider, AnthropicLLMProvider>();
}
else
{
    builder.Services.AddScoped<ILLMProvider, LocalHeuristicLLMProvider>();
}

builder.Services.AddScoped<AnalysisOrchestrationService>();
builder.Services.AddScoped<TransformationOrchestrationService>();
builder.Services.AddScoped<GenerationOrchestrationService>();

// §3/§7/§10 Vision mimarisi. Vision:Provider (birincil) varsayılanı "Local" — dış API anahtarı
// gerektirmez, yalnızca boru mekaniğini doğrulamak içindir (bkz. LocalMockVisionProvider'daki
// not). §8.1 varsayılan öneri Gemini'dir. Vision:SecondaryProvider boşsa (varsayılan) provider
// disagreement/consensus akışı tamamen devre dışıdır — ek maliyet yalnızca açıkça
// yapılandırıldığında oluşur. Tüm somut sağlayıcılar kendi adlarıyla kaydedilir; hangisinin
// birincil/ikincil olduğuna VisionProviderFactory + VisionRoutingOptions karar verir.
builder.Services.AddScoped<IPdfPageRenderer, DocnetPageRenderer>();
builder.Services.AddScoped<IVisionRouter, HeuristicVisionRouter>();

builder.Services.AddScoped<LocalMockVisionProvider>();

builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("Vision:Gemini"));
builder.Services.AddHttpClient<GeminiVisionProvider>();

builder.Services.Configure<AnthropicVisionOptions>(builder.Configuration.GetSection("Vision:Anthropic"));
builder.Services.AddScoped<AnthropicVisionProvider>();

builder.Services.AddScoped<IVisionProviderFactory, VisionProviderFactory>();

builder.Services.AddSingleton(Options.Create(new VisionRoutingOptions
{
    PrimaryProvider = builder.Configuration["Vision:Provider"] ?? "Local",
    SecondaryProvider = builder.Configuration["Vision:SecondaryProvider"],
    ConsensusConfidenceThreshold = builder.Configuration.GetValue<decimal?>("Vision:ConsensusConfidenceThreshold") ?? 0.95m
}));

builder.Services.AddScoped<VisionAnalysisService>();

// Sprint 7 Auth/RBAC. JWT bearer, refresh token YOK (bilinçli MVP sınırı — bkz. README).
// İlk Admin kullanıcısı açık self-registration yerine BootstrapAdminInitializer ile seed edilir.
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Auth:Jwt"));
builder.Services.Configure<BootstrapAdminOptions>(builder.Configuration.GetSection("Auth:BootstrapAdmin"));
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<AuthService>();

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
