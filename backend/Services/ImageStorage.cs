using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace LojaVirtual.Services;

public sealed class CloudinaryOptions
{
    public string CloudName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
}

public sealed record StoredImage(string Url, string PublicId);

public interface IImageStorage
{
    Task<StoredImage> UploadAsync(IFormFile file, string folder, CancellationToken cancellationToken = default);
    Task DeleteAsync(string publicId, CancellationToken cancellationToken = default);
}

public sealed class CloudinaryImageStorage : IImageStorage
{
    private readonly Cloudinary? _cloudinary;
    private readonly CloudinaryOptions _options;

    public CloudinaryImageStorage(IOptions<CloudinaryOptions> options)
    {
        _options = options.Value;
        if (!string.IsNullOrWhiteSpace(_options.CloudName)
            && !string.IsNullOrWhiteSpace(_options.ApiKey)
            && !string.IsNullOrWhiteSpace(_options.ApiSecret))
            _cloudinary = new Cloudinary(new Account(_options.CloudName, _options.ApiKey, _options.ApiSecret));
    }

    public async Task<StoredImage> UploadAsync(
        IFormFile file,
        string folder,
        CancellationToken cancellationToken = default)
    {
        var cloudinary = EnsureConfigured();
        await using var stream = file.OpenReadStream();
        var upload = await cloudinary.UploadAsync(new ImageUploadParams
        {
            File = new FileDescription(file.FileName, stream),
            Folder = folder,
            UseFilename = false,
            UniqueFilename = true,
            Overwrite = false
        });

        cancellationToken.ThrowIfCancellationRequested();
        if (upload.Error is not null || string.IsNullOrWhiteSpace(upload.SecureUrl?.ToString())
            || string.IsNullOrWhiteSpace(upload.PublicId))
        {
            throw new ImageStorageException("O Cloudinary recusou o upload da imagem.");
        }

        return new StoredImage(upload.SecureUrl.ToString(), upload.PublicId);
    }

    public async Task DeleteAsync(string publicId, CancellationToken cancellationToken = default)
    {
        if (!IsManagedPublicId(publicId))
            return;

        var result = await EnsureConfigured().DestroyAsync(new DeletionParams(publicId)
        {
            ResourceType = ResourceType.Image,
            Invalidate = true
        });

        cancellationToken.ThrowIfCancellationRequested();
        if (result.Error is not null && !string.Equals(result.Result, "not found", StringComparison.OrdinalIgnoreCase))
            throw new ImageStorageException("Não foi possível remover a imagem do Cloudinary.");
    }

    private bool IsManagedPublicId(string publicId) =>
        publicId.StartsWith("angel-acessorios/products/", StringComparison.Ordinal)
        && publicId.Length > "angel-acessorios/products/".Length;

    private Cloudinary EnsureConfigured() => _cloudinary ?? throw new ImageStorageException(
        "Cloudinary não está configurado. Defina Cloudinary__CloudName, Cloudinary__ApiKey e Cloudinary__ApiSecret.");
}

public sealed class ImageStorageException(string message) : Exception(message);

public sealed class ProductImageService
{
    private static readonly HashSet<string> AllowedContentTypes =
        ["image/jpeg", "image/png", "image/webp", "image/gif"];
    private const long MaxFileSize = 10 * 1024 * 1024;
    private readonly IImageStorage _storage;

    public ProductImageService(IImageStorage storage) => _storage = storage;

    public async Task<IReadOnlyList<StoredImage>> UploadAsync(
        IEnumerable<IFormFile>? files,
        int productId,
        CancellationToken cancellationToken = default)
    {
        var validFiles = (files ?? []).Where(file => file.Length > 0).ToList();
        foreach (var file in validFiles)
        {
            if (file.Length > MaxFileSize || !AllowedContentTypes.Contains(file.ContentType))
                throw new ImageStorageException("Cada imagem deve ser JPG, PNG, WEBP ou GIF e ter no máximo 10 MB.");
        }

        var uploaded = new List<StoredImage>();
        try
        {
            foreach (var file in validFiles)
            {
                uploaded.Add(await _storage.UploadAsync(
                    file,
                    $"angel-acessorios/products/{productId}",
                    cancellationToken));
            }

            return uploaded;
        }
        catch
        {
            await DeleteQuietlyAsync(uploaded);
            throw;
        }
    }

    public Task DeleteAsync(StoredImage image, CancellationToken cancellationToken = default) =>
        _storage.DeleteAsync(image.PublicId, cancellationToken);

    public Task DeleteAsync(string publicId, CancellationToken cancellationToken = default) =>
        _storage.DeleteAsync(publicId, cancellationToken);

    public async Task DeleteQuietlyAsync(IEnumerable<StoredImage> images)
    {
        foreach (var image in images)
        {
            try { await DeleteAsync(image); }
            catch { /* compensação é best effort; não mascarar a falha original */ }
        }
    }
}
