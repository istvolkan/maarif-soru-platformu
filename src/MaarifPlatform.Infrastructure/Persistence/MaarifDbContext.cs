using MaarifPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Infrastructure.Persistence;

public class MaarifDbContext(DbContextOptions<MaarifDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<MaarifStandardVersion> MaarifStandardVersions => Set<MaarifStandardVersion>();

    public DbSet<ReferenceDocument> ReferenceDocuments => Set<ReferenceDocument>();
    public DbSet<ReferenceChunk> ReferenceChunks => Set<ReferenceChunk>();

    public DbSet<Book> Books => Set<Book>();
    public DbSet<BookPage> BookPages => Set<BookPage>();

    public DbSet<Question> Questions => Set<Question>();
    public DbSet<QuestionVersion> QuestionVersions => Set<QuestionVersion>();
    public DbSet<QuestionDna> QuestionDnas => Set<QuestionDna>();
    public DbSet<QuestionVisualAsset> QuestionVisualAssets => Set<QuestionVisualAsset>();

    public DbSet<LearningOutcome> LearningOutcomes => Set<LearningOutcome>();
    public DbSet<AlignmentScore> AlignmentScores => Set<AlignmentScore>();
    public DbSet<Distractor> Distractors => Set<Distractor>();

    public DbSet<ReviewQueueItem> ReviewQueue => Set<ReviewQueueItem>();

    public DbSet<AiRun> AiRuns => Set<AiRun>();
    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // §G RAG: embedding sütunu için pgvector uzantısı.
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MaarifDbContext).Assembly);
    }
}
