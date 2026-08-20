using MaarifPlatform.Application.Extraction;
using MaarifPlatform.Application.Rag;
using MaarifPlatform.Application.Storage;
using MaarifPlatform.Infrastructure.Extraction;
using MaarifPlatform.Infrastructure.Persistence;
using MaarifPlatform.Infrastructure.Rag;
using MaarifPlatform.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// §L/§20 MVP: altyapı ayakta mı ve DB'ye ulaşılabiliyor mu diye hızlı kontrol.
app.MapGet("/health", async (MaarifDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return Results.Ok(new { status = "ok", database = canConnect ? "connected" : "unreachable" });
});

app.Run();
