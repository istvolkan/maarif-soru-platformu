using MaarifPlatform.Domain.Enums;

namespace MaarifPlatform.Domain.Entities;

/// <summary>§13/§10 Book Management — eski müfredata göre hazırlanmış soru bankası kitabı.</summary>
public class Book : Entity
{
    public string Title { get; set; } = string.Empty;
    public int? Grade { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? Publisher { get; set; }
    public SourceType SourceType { get; set; } = SourceType.LegacyBook;
    public int? TotalPages { get; set; }
    public string StorageUri { get; set; } = string.Empty;

    public ICollection<BookPage> Pages { get; set; } = new List<BookPage>();
    public ICollection<Question> Questions { get; set; } = new List<Question>();
}

/// <summary>§10 PDF İşleme — sayfa bazlı çıkarılmış ham içerik.</summary>
public class BookPage : Entity
{
    public Guid BookId { get; set; }
    public Book? Book { get; set; }

    public int PageNo { get; set; }
    public string? RawText { get; set; }
    public bool OcrUsed { get; set; }
    public string? ImageUri { get; set; }
}
