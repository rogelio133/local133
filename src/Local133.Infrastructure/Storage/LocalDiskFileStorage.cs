using System.Text.RegularExpressions;
using Local133.Application.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace Local133.Infrastructure.Storage;

public sealed class FileStorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Carpeta raíz de los archivos (relativa a la raíz del contenido si no es absoluta).</summary>
    public string LocalDirectory { get; set; } = "App_Data/uploads";
}

/// <summary>
/// <see cref="IFileStorage"/> sobre el disco local (desarrollo y un solo servidor). En producción se cambia por blob.
/// Las llaves se validan contra un patrón estricto: nunca salen de la carpeta raíz.
/// </summary>
public sealed partial class LocalDiskFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalDiskFileStorage(IOptions<FileStorageOptions> options, IWebHostEnvironment environment)
        : this(Path.IsPathRooted(options.Value.LocalDirectory)
            ? options.Value.LocalDirectory
            : Path.Combine(environment.ContentRootPath, options.Value.LocalDirectory))
    {
    }

    public LocalDiskFileStorage(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _root = Path.GetFullPath(rootDirectory);
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Primero a un temporal en la misma carpeta y luego se renombra: nunca queda un archivo a medias.
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(file, cancellationToken);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        File.Delete(PathFor(key));
        return Task.CompletedTask;
    }

    /// <summary>Llave válida: segmentos de minúsculas, dígitos y guiones separados por <c>/</c>, con extensión.</summary>
    public static bool IsValidKey(string? key) => key is { Length: <= 200 } && KeyRegex().IsMatch(key);

    private string PathFor(string key)
    {
        if (!IsValidKey(key))
        {
            throw new ArgumentException($"Llave de archivo inválida: '{key}'.", nameof(key));
        }

        var path = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));
        // Defensa adicional: el patrón ya impide «..», rutas absolutas y separadores de Windows.
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"La llave '{key}' sale de la carpeta de archivos.", nameof(key));
        }

        return path;
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]*(/[a-z0-9][a-z0-9-]*)*\.[a-z0-9]{2,5}$")]
    private static partial Regex KeyRegex();
}
