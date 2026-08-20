namespace MaarifPlatform.Application.Storage;

/// <summary>MVP'de yerel disk, ileride blob storage ile değiştirilebilir (§L).</summary>
public interface IBookFileStorage
{
    Task<string> SaveAsync(Guid bookId, string fileName, Stream content, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string storageUri, CancellationToken ct = default);
}
