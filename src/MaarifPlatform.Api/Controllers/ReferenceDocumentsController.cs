using System.Security.Cryptography;
using MaarifPlatform.Api.Dtos;
using MaarifPlatform.Application.Storage;
using MaarifPlatform.Domain.Entities;
using MaarifPlatform.Infrastructure.Persistence;
using MaarifPlatform.Infrastructure.Rag;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MaarifPlatform.Api.Controllers;

/// <summary>§G/§13 Reference Library — MEB kaynak dokümanlarının yüklenmesi, chunk+embed edilmesi
/// ve RAG retrieval'ı. Auth/RBAC henüz yok (Sprint 3'te sonraki bir adımda eklenecek).</summary>
[ApiController]
[Route("api/reference-documents")]
public class ReferenceDocumentsController(
    MaarifDbContext db,
    IBookFileStorage storage,
    ReferenceIngestionService ingestionService,
    ReferenceSearchService searchService) : ControllerBase
{
    private static readonly string[] AllowedExtensions = [".pdf"];
    private const long MaxFileSizeBytes = 200 * 1024 * 1024; // 200 MB

    [HttpPost]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<ActionResult<ReferenceDocumentResponse>> Create(
        [FromForm] CreateReferenceDocumentRequest request, CancellationToken ct)
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

        byte[] fileBytes;
        await using (var buffer = new MemoryStream())
        {
            await request.File.CopyToAsync(buffer, ct);
            fileBytes = buffer.ToArray();
        }

        // §9: aynı dokümanın tekrar tekrar yüklenip gömülmesini önlemek için içerik hash'i.
        var documentHash = Convert.ToHexString(SHA256.HashData(fileBytes));

        var existing = await db.ReferenceDocuments.FirstOrDefaultAsync(d => d.DocumentHash == documentHash, ct);
        if (existing is not null)
        {
            return Conflict(new { message = "Bu içerik daha önce yüklenmiş.", existingDocumentId = existing.Id });
        }

        var document = new ReferenceDocument
        {
            Title = request.Title,
            DocumentType = request.DocumentType,
            Grade = request.Grade,
            Subject = request.Subject,
            Version = request.Version ?? string.Empty,
            PublicationDate = request.PublicationDate,
            Authority = request.Authority,
            DocumentHash = documentHash
        };

        await using (var stream = new MemoryStream(fileBytes))
        {
            document.StorageUri = await storage.SaveAsync(document.Id, request.File.FileName, stream, ct);
        }

        db.ReferenceDocuments.Add(document);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = document.Id }, ToResponse(document));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReferenceDocumentResponse>>> List(CancellationToken ct)
    {
        var documents = await db.ReferenceDocuments.OrderByDescending(d => d.CreatedAt).ToListAsync(ct);
        return documents.Select(ToResponse).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReferenceDocumentResponse>> GetById(Guid id, CancellationToken ct)
    {
        var document = await db.ReferenceDocuments.FirstOrDefaultAsync(d => d.Id == id, ct);
        return document is null ? NotFound() : ToResponse(document);
    }

    [HttpPost("{id:guid}/ingest")]
    public async Task<ActionResult<IngestionResult>> Ingest(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await ingestionService.IngestAsync(id, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<RetrievedChunkResponse>>> Search(
        [FromQuery] string query, [FromQuery] int topK = 5, [FromQuery] int? grade = null,
        [FromQuery] string? subject = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest("query parametresi zorunludur.");
        }

        var results = await searchService.SearchAsync(query, topK, grade, subject, ct);
        return results
            .Select(r => new RetrievedChunkResponse(r.ReferenceDocumentId, r.DocumentTitle, r.Page, r.SectionPath, r.ChunkText, r.Distance))
            .ToList();
    }

    private static ReferenceDocumentResponse ToResponse(ReferenceDocument d) => new(
        d.Id, d.Title, d.DocumentType, d.Grade, d.Subject, d.Version, d.Authority, d.Active, d.CreatedAt);
}
