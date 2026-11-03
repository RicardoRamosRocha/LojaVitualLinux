using LojaVirtual.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace LojaVirtual.Tests;

public sealed class ProductImageServiceTests
{
    [Fact]
    public async Task Upload_returns_stored_images_without_external_network()
    {
        var storage = new FakeStorage();
        var service = new ProductImageService(storage);

        var result = await service.UploadAsync([File("photo.png", "image/png")], 42);

        var image = Assert.Single(result);
        Assert.Equal("https://res.cloudinary.com/test/image/upload/angel-acessorios/products/42/photo.png", image.Url);
        Assert.Equal("angel-acessorios/products/42/photo", image.PublicId);
        Assert.Equal("angel-acessorios/products/42", Assert.Single(storage.Folders));
    }

    [Fact]
    public async Task Failed_upload_compensates_images_already_sent()
    {
        var storage = new FakeStorage { FailOnUploadNumber = 2 };
        var service = new ProductImageService(storage);

        await Assert.ThrowsAsync<ImageStorageException>(() => service.UploadAsync([
            File("one.png", "image/png"), File("two.png", "image/png")], 7));

        Assert.Single(storage.DeletedPublicIds);
        Assert.Equal("angel-acessorios/products/7/one", storage.DeletedPublicIds[0]);
    }

    [Fact]
    public async Task Invalid_or_oversized_images_are_rejected_before_upload()
    {
        var storage = new FakeStorage();
        var service = new ProductImageService(storage);

        await Assert.ThrowsAsync<ImageStorageException>(() => service.UploadAsync([File("script.svg", "image/svg+xml")], 1));
        Assert.Empty(storage.Folders);
    }

    [Fact]
    public async Task Edit_uploads_new_media_and_delete_targets_only_its_public_id()
    {
        var storage = new FakeStorage();
        var service = new ProductImageService(storage);

        await service.UploadAsync([File("new.webp", "image/webp")], 12);
        await service.DeleteAsync("angel-acessorios/products/12/new");

        Assert.Equal("angel-acessorios/products/12/new", Assert.Single(storage.DeletedPublicIds));
    }

    private static FormFile File(string name, string contentType)
    {
        var bytes = new byte[] { 1, 2, 3 };
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "imageFiles", name) { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    private sealed class FakeStorage : IImageStorage
    {
        public int FailOnUploadNumber { get; init; }
        public List<string> Folders { get; } = [];
        public List<string> DeletedPublicIds { get; } = [];
        private int _uploads;

        public Task<StoredImage> UploadAsync(IFormFile file, string folder, CancellationToken cancellationToken = default)
        {
            _uploads++;
            Folders.Add(folder);
            if (_uploads == FailOnUploadNumber) throw new ImageStorageException("simulated failure");
            var publicId = $"{folder}/{Path.GetFileNameWithoutExtension(file.FileName)}";
            return Task.FromResult(new StoredImage($"https://res.cloudinary.com/test/image/upload/{publicId}.png", publicId));
        }

        public Task DeleteAsync(string publicId, CancellationToken cancellationToken = default)
        {
            DeletedPublicIds.Add(publicId);
            return Task.CompletedTask;
        }
    }
}
