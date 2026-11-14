using System.Text;
using Local133.Infrastructure.Storage;

namespace Local133.IntegrationTests.Products;

/// <summary>Archivos en disco local (sin SQL Server: corre en CI).</summary>
public sealed class LocalDiskFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"local133-storage-{Guid.NewGuid():N}");

    private LocalDiskFileStorage Storage => new(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Guarda_lee_reemplaza_y_borra()
    {
        const string key = "products/7/abc123.jpg";

        await Storage.SaveAsync(key, new MemoryStream("uno"u8.ToArray()), "image/jpeg");
        Assert.Equal("uno", await ReadAsync(key));
        Assert.True(File.Exists(Path.Combine(_root, "products", "7", "abc123.jpg")));

        await Storage.SaveAsync(key, new MemoryStream("dos"u8.ToArray()), "image/jpeg");
        Assert.Equal("dos", await ReadAsync(key));
        // Sin temporales sueltos.
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "products", "7")));

        await Storage.DeleteAsync(key);
        Assert.Null(await Storage.OpenReadAsync(key));
        await Storage.DeleteAsync(key); // borrar lo que no existe no falla
    }

    [Fact]
    public async Task Leer_lo_que_no_existe_devuelve_null() =>
        Assert.Null(await Storage.OpenReadAsync("products/1/nada.jpg"));

    [Theory]
    [InlineData("../fuera.jpg")]
    [InlineData("products/../../fuera.jpg")]
    [InlineData("/products/a.jpg")]
    [InlineData(@"C:\Windows\a.jpg")]
    [InlineData(@"products\a.jpg")]
    [InlineData("Products/A.jpg")]
    [InlineData("products//a.jpg")]
    [InlineData("products/.jpg")]
    [InlineData("products/sin-extension")]
    [InlineData("")]
    public async Task Rechaza_llaves_invalidas(string key)
    {
        Assert.False(LocalDiskFileStorage.IsValidKey(key));
        await Assert.ThrowsAsync<ArgumentException>(() => Storage.SaveAsync(key, new MemoryStream([1]), "image/jpeg"));
        await Assert.ThrowsAsync<ArgumentException>(() => Storage.OpenReadAsync(key));
        await Assert.ThrowsAsync<ArgumentException>(() => Storage.DeleteAsync(key));
    }

    private async Task<string> ReadAsync(string key)
    {
        await using var stream = await Storage.OpenReadAsync(key);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}
