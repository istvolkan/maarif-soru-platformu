using MaarifPlatform.Application.Storage;
using Microsoft.Extensions.Options;

namespace MaarifPlatform.Infrastructure.Storage;

public class LocalFileStorageOptions
{
    public string RootPath { get; set; } = "./data/books";
}

/// <summary>MVP dosya depolama — yerel disk. §L'de gerçek blob storage'a geçiş öngörülüyor;
/// bu implementasyon sadece <see cref="IBookFileStorage"/> arkasında değiştirilecek.</summary>
public class LocalFileStorage(IOptions<LocalFileStorageOptions> options) : IBookFileStorage
{
    private readonly string _rootPath = options.Value.RootPath;

    public async Task<string> SaveAsync(Guid bookId, string fileName, Stream content, CancellationToken ct = default)
    {
        var directory = Path.Combine(_rootPath, bookId.ToString());
        Directory.CreateDirectory(directory);

        var safeFileName = Path.GetFileName(fileName);
        var fullPath = Path.Combine(directory, safeFileName);

        await using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, ct);

        return fullPath;
    }

    public Task<Stream> OpenReadAsync(string storageUri, CancellationToken ct = default)
    {
        Stream stream = File.OpenRead(storageUri);
        return Task.FromResult(stream);
    }
}
