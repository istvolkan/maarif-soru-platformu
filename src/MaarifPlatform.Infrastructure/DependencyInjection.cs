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
/// kullanmaz ama aynı parola doğrulama mantığını (AuthService.LoginAsync) tekrar yazmak yerine
/// paylaşır.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddMaarifPlatformCore(this IServiceCollection services, IConfiguration configuration)
    {
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

        var embeddingProviderName = configuration["Embeddings:Provider"] ?? "Local";
        if (string.Equals(embeddingProviderName, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IEmbeddingProvider>(sp => sp.GetRequiredService<OpenAIEmbeddingProvider>());
        }
        else
        {
            services.AddScoped<IEmbeddingProvider, LocalDeterministicEmbeddingProvider>();
        }

        services.AddScoped<ReferenceIngestionService>();
        services.AddScoped<ReferenceSearchService>();

        // §4/§H/§8/§10 Analysis + Judge Provider Disagreement. Birincil/ikincil sağlayıcı seçimi
        // Sprint 11'den itibaren TAMAMEN çalışma-zamanlı (IOptionsMonitor + ILLMProviderFactory) —
        // Ai:Provider/Judge:SecondaryProvider ayarları uygulama yeniden başlatılmadan değişebilir.
        services.Configure<AiRoutingOptions>(configuration.GetSection("Ai"));
        services.Configure<AnthropicOptions>(configuration.GetSection("Ai:Anthropic"));
        services.AddScoped<AnthropicLLMProvider>();
        services.AddScoped<LocalHeuristicLLMProvider>();
        services.Configure<OpenAiOptions>(configuration.GetSection("Judge:OpenAI"));
        services.AddScoped<OpenAiLLMProvider>();
        services.AddScoped<ILLMProviderFactory, LLMProviderFactory>();
        services.Configure<JudgeRoutingOptions>(configuration.GetSection("Judge"));

        services.AddScoped<AnalysisOrchestrationService>();
        services.AddScoped<TransformationOrchestrationService>();
        services.AddScoped<GenerationOrchestrationService>();

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

        // Auth çekirdeği — JWT ÜRETİMİ (doğrulama middleware'i değil) ve parola doğrulama burada;
        // her iki host da aynı AuthService.LoginAsync'i kullanır.
        services.Configure<JwtOptions>(configuration.GetSection("Auth:Jwt"));
        services.Configure<BootstrapAdminOptions>(configuration.GetSection("Auth:BootstrapAdmin"));
        services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<AuthService>();

        services.AddScoped<SystemSettingsService>();

        return services;
    }
}
