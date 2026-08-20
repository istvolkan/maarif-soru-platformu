using System.Text.Json;
using MaarifPlatform.Api.Dtos;
using MaarifPlatform.Application.Storage;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Infrastructure.Extraction;
using MaarifPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Api.Controllers;

/// <summary>§13/§20 MVP — Book Management + Question Extraction + Question Database'in
/// ilk API yüzeyi. Auth/RBAC henüz yok (Sprint 3'te eklenecek, §O).</summary>
[ApiController]
[Route("api/books")]
public class BooksController(MaarifDbContext db, IBookFileStorage storage, BookExtractionService extractionService)
    : ControllerBase
{
    private static readonly string[] AllowedExtensions = [".pdf"];
    private const long MaxFileSizeBytes = 200 * 1024 * 1024; // 200 MB

    [HttpPost]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<ActionResult<BookResponse>> Create([FromForm] CreateBookRequest request, CancellationToken ct)
    {
        if (request.File.Length == 0)
        {
            return BadRequest("Dosya boş olamaz.");
        }

        var extension = Path.GetExtension(request.File.FileName);
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest("Yalnızca PDF dosyaları kabul edilir.");
        }

        var book = new Book
        {
            Title = request.Title,
            Grade = request.Grade,
            Subject = request.Subject,
            Publisher = request.Publisher
        };

        await using (var stream = request.File.OpenReadStream())
        {
            book.StorageUri = await storage.SaveAsync(book.Id, request.File.FileName, stream, ct);
        }

        db.Books.Add(book);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = book.Id }, ToResponse(book));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BookResponse>>> List(CancellationToken ct)
    {
        var books = await db.Books.OrderByDescending(b => b.CreatedAt).ToListAsync(ct);
        return books.Select(ToResponse).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BookResponse>> GetById(Guid id, CancellationToken ct)
    {
        var book = await db.Books.FirstOrDefaultAsync(b => b.Id == id, ct);
        return book is null ? NotFound() : ToResponse(book);
    }

    [HttpPost("{id:guid}/extract")]
    public async Task<ActionResult<BookExtractionResult>> Extract(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await extractionService.ExtractAsync(id, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpGet("{id:guid}/questions")]
    public async Task<ActionResult<IReadOnlyList<QuestionSummaryResponse>>> GetQuestions(Guid id, CancellationToken ct)
    {
        var bookExists = await db.Books.AnyAsync(b => b.Id == id, ct);
        if (!bookExists)
        {
            return NotFound();
        }

        var questions = await db.Questions
            .Where(q => q.BookId == id)
            .OrderBy(q => q.QuestionNo)
            .Select(q => new
            {
                q.Id,
                q.QuestionNo,
                Status = q.Status.ToString(),
                Dna = db.QuestionDnas
                    .Where(d => d.QuestionVersion!.QuestionId == q.Id)
                    .OrderByDescending(d => d.CreatedAt)
                    .Select(d => new { d.SourcePage, d.OriginalQuestion, d.OriginalOptionsJson, d.EditorRequired, d.QualityFlagsJson })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var response = questions.Select(q =>
        {
            var optionCount = 0;
            var flags = Array.Empty<string>();

            if (q.Dna?.OriginalOptionsJson is { } optionsJson)
            {
                optionCount = JsonSerializer.Deserialize<JsonElement[]>(optionsJson)?.Length ?? 0;
            }

            if (q.Dna?.QualityFlagsJson is { } flagsJson)
            {
                flags = JsonSerializer.Deserialize<string[]>(flagsJson) ?? Array.Empty<string>();
            }

            return new QuestionSummaryResponse(
                q.Id,
                q.QuestionNo,
                q.Dna?.SourcePage,
                q.Status,
                q.Dna?.OriginalQuestion,
                optionCount,
                q.Dna?.EditorRequired ?? false,
                flags);
        }).ToList();

        return response;
    }

    private static BookResponse ToResponse(Book book) => new(
        book.Id, book.Title, book.Grade, book.Subject, book.Publisher,
        book.SourceType.ToString(), book.TotalPages, book.CreatedAt);
}
