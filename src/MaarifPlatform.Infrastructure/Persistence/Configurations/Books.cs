using MaarifPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaarifPlatform.Infrastructure.Persistence.Configurations;

public class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> b)
    {
        b.ToTable("books");
        b.HasKey(e => e.Id);
        b.Property(e => e.Title).HasMaxLength(500).IsRequired();
        b.Property(e => e.Subject).HasMaxLength(100).IsRequired();
        b.Property(e => e.Publisher).HasMaxLength(200);
        b.Property(e => e.SourceType).HasConversion<string>().HasMaxLength(30);
        b.Property(e => e.StorageUri).HasMaxLength(1000);

        b.HasMany(e => e.Pages)
            .WithOne(e => e.Book)
            .HasForeignKey(e => e.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(e => e.Questions)
            .WithOne(e => e.Book)
            .HasForeignKey(e => e.BookId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class BookPageConfiguration : IEntityTypeConfiguration<BookPage>
{
    public void Configure(EntityTypeBuilder<BookPage> b)
    {
        b.ToTable("book_pages");
        b.HasKey(e => e.Id);
        b.Property(e => e.ImageUri).HasMaxLength(1000);
        b.HasIndex(e => new { e.BookId, e.PageNo }).IsUnique();
    }
}
