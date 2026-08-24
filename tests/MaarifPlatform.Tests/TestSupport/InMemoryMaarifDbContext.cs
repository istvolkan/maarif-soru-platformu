using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Tests.TestSupport;

/// <summary>ReferenceChunk.Embedding (Pgvector.Vector) yalnızca Npgsql'in UseVector() eklentisiyle
/// eşlenebilir; InMemory provider'da eşlemesi tanımsız kalır ve model doğrulaması başarısız olur.
/// Bu kolonla ilgilenmeyen testler (Auth vb.) için yalnızca bu property Ignore edilir.</summary>
public class InMemoryMaarifDbContext(DbContextOptions<MaarifDbContext> options) : MaarifDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ReferenceChunk>().Ignore(c => c.Embedding);
    }
}
