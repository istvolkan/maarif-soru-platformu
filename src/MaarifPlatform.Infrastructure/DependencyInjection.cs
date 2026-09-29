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
using MaarifPlatform.Infrastructure.Configuration;
using MaarifPlatform.Infrastructure.Curriculum;
using MaarifPlatform.Infrastructure.Export;
using MaarifPlatform.Infrastructure.Generation;
using MaarifPlatform.Infrastructure.Extraction;
using MaarifPlatform.Infrastructure.Persistence;
using MaarifPlatform.Infrastructure.Rag;
using MaarifPlatform.Infrastructure.Storage;
using MaarifPlatform.Infrastructure.Vision;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MaarifPlatform.Infrastructure;

/// <summary>Sprint 11 — Api ve Web (Blazor Server) projelerinin ORTAK servis grafiği. Yalnızca
/// JWT-doğrulama middleware'i (AddAuthentication().AddJwtBearer()) ve ASP.NET-katmanı ayarları
/// (AddControllers/Swagger/AddRazorComponents/cookie auth) her host'un kendi Program.cs'inde
/// kalır — bunun dışındaki her şey burada, iki Program.cs'in birbirinden sürüklenmesini önlemek
/// için. AuthService/IJwtTokenService de buradadır (JWT ÜRETİMİ ASP.NET'e bağımlı değildir,
/// yalnızca JWT DOĞRULAMA middleware'i Api'ye özeldir) — Web projesi JwtToken alanını hiç
/// kullanmaz ama aynı parola doğrulama mantığını (AuthService.ValidateCredentialsAsync) tekrar yazmak yerine
/// paylaşır.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddMaarifPlatformCore(this IServiceCollection services, IConfiguration configuration)
    {
        // Zafer Koleji bir akademik kurum olduğu için QuestPDF Community lisansı gelir eşiğinden
        // muaf (bkz. https://www.questpdf.com/license/community.html).
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        var connectionString = configuration.GetConnectionString("MaarifDb")
            ?? throw new InvalidOperationException("ConnectionStrings:MaarifDb tanımlı değil.");

        services.AddDbContext<MaarifDbContext>(options =>
            options.UseNpgsql(connectionString, npg => npg.UseVector()));

        // §10 PDF İşleme pipeline'ı.
        services.Configure<LocalFileStorageOptions>(configuration.GetSection("Storage"));
        services.AddScoped<IBookFileStorage, LocalFileStorage>();
        services.AddScoped<IPdfTextExtractor, DocnetTextExtractor>();
        services.AddScoped<IQuestionSegmenter, HeuristicQuestionSegmenter>();
        services.AddScoped<BookExtractionService>();

        // §G RAG pipeline'ı. Embeddings:Provider varsayılanı "Local" — dış API anahtarı
        // gerektirmez (bkz. LocalDeterministicEmbeddingProvider'daki not).
        services.AddScoped<IReferenceChunker, ParagraphReferenceChunker>();
        services.Configure<OpenAIEmbeddingOptions>(configuration.GetSection("Embeddings:OpenAI"));
        services.AddHttpClient<OpenAIEmbeddingProvider>();

        services.AddScoped<LocalDeterministicEmbeddingProvider>();
        services.AddScoped<IEmbeddingProvider>(sp =>
            string.Equals(configuration["Embeddings:Provider"], "OpenAI", StringComparison.OrdinalIgnoreCase)
                ? sp.GetRequiredService<OpenAIEmbeddingProvider>()
                : sp.GetRequiredService<LocalDeterministicEmbeddingProvider>());

        services.AddScoped<ReferenceIngestionService>();
        services.AddScoped<ReferenceSearchService>();
        services.AddScoped<CurriculumExtractionService>();
        services.AddScoped<CurriculumQueryService>();
        services.AddScoped<QuestionSimilarityService>();

        // §4/§H/§8/§10 Analysis + Judge Provider Disagreement. Birincil/ikincil sağlayıcı seçimi
        // Sprint 11'den itibaren TAMAMEN çalışma-zamanlı (IOptionsMonitor + ILLMProviderFactory) —
        // Ai:Provider/Judge:SecondaryProvider ayarları uygulama yeniden başlatılmadan değişebilir.
        services.Configure<AiRoutingOptions>(configuration.GetSection("Ai"));
        services.Configure<AnthropicOptions>(configuration.GetSection("Ai:Anthropic"));
        services.AddScoped<AnthropicLLMProvider>();
        services.Configure<ClaudeCliOptions>(configuration.GetSection("Ai:ClaudeCli"));
        services.AddScoped<ClaudeCliLLMProvider>();
        services.AddScoped<LocalHeuristicLLMProvider>();
        services.Configure<OpenAiOptions>(configuration.GetSection("Judge:OpenAI"));
        services.AddScoped<OpenAiLLMProvider>();
        services.AddScoped<ILLMProviderFactory, LLMProviderFactory>();
        services.Configure<JudgeRoutingOptions>(configuration.GetSection("Judge"));
        services.Configure<GenerationRoutingOptions>(configuration.GetSection("Generation"));

        // §16 zorluk bazlı model yönlendirme — GenerateBatchAsync'in her slotu için kendi
        // Difficulty'sine göre Generator/CurriculumValidator/Judge aşamalarından her biri AYRI
        // bir (sağlayıcı, model) çiftine yönlendirilebilir (ör. Kolay→OpenAI gpt-6-luna,
        // Zor→Anthropic claude-opus-4-8). Aynı Dictionary<string,string?> şekli üç farklı config
        // bölümüne (adlandırılmış options) bağlanır; anahtar = DifficultyLevel.ToString()
        // ("Easy","Hard",...), değer = "Sağlayıcı:Model" veya boş (o zorlukta override yok,
        // mevcut varsayılana düşülür). services.Configure<T>(name, section) .NET'in yerleşik
        // adlandırılmış-options mekanizması — IOptionsMonitor<Dictionary<string,string?>>.Get(name).
        services.Configure<Dictionary<string, string?>>("Generation", configuration.GetSection("Generation:Routing"));
        services.Configure<Dictionary<string, string?>>("CurriculumValidation", configuration.GetSection("CurriculumValidation:Routing"));
        services.Configure<Dictionary<string, string?>>("Judge", configuration.GetSection("Judge:Routing"));

        services.AddScoped<AnalysisOrchestrationService>();
        services.AddScoped<TransformationOrchestrationService>();
        services.AddScoped<GenerationOrchestrationService>();
        services.AddScoped<BookBatchTransformService>();
        services.AddScoped<BookPdfExportService>();

        // §3/§7/§10 Vision mimarisi. Aynı şekilde birincil/ikincil seçim IOptionsMonitor üzerinden
        // çalışma-zamanlıdır.
        services.AddScoped<IPdfPageRenderer, DocnetPageRenderer>();
        services.AddScoped<IVisionRouter, HeuristicVisionRouter>();

        services.AddScoped<LocalMockVisionProvider>();

        services.Configure<GeminiOptions>(configuration.GetSection("Vision:Gemini"));
        services.AddHttpClient<GeminiVisionProvider>();

        services.Configure<AnthropicVisionOptions>(configuration.GetSection("Vision:Anthropic"));
        services.AddScoped<AnthropicVisionProvider>();

        services.AddScoped<IVisionProviderFactory, VisionProviderFactory>();
        services.Configure<VisionRoutingOptions>(configuration.GetSection("Vision"));

        services.AddScoped<VisionAnalysisService>();

        // Admin Ayarlar ekranındaki Model dropdown'ları için — sağlayıcıların canlı model
        // kataloğunu sorgular (bkz. ProviderModelCatalogService üstündeki gerekçe).
        services.AddHttpClient<ProviderModelCatalogService>();

        // Auth çekirdeği — JWT ÜRETİMİ (doğrulama middleware'i değil) ve parola doğrulama burada;
        // Her iki host ortak credential doğrulamasını kullanır; yalnızca API JWT üretir.
        services.Configure<JwtOptions>(configuration.GetSection("Auth:Jwt"));
        services.Configure<BootstrapAdminOptions>(configuration.GetSection("Auth:BootstrapAdmin"));
        services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<AuthService>();
        services.AddScoped<SessionValidator>();
        services.AddScoped<UserManagementService>();

        // §18 Rol bazlı yetki matrisi — [Authorize(Policy=nameof(Permission.X))] sayfaları için.
        // Politikaların kendisi (AddAuthorization/AddPolicy) Web'in Program.cs'inde kayıtlıdır
        // (yalnızca ASP.NET Core host projesinde mevcut olan AuthorizationOptions API'si
        // gerektirir); burada yalnızca handler + veri erişimi.
        services.AddScoped<PermissionService>();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddScoped<SystemSettingsService>();

        return services;
    }
}
