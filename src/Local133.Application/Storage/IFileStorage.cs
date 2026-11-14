namespace Local133.Application.Storage;

/// <summary>
/// Archivos del sitio (fotos de productos, logos…): disco local en desarrollo, blob en producción. Las llaves son
/// rutas relativas con <c>/</c> (p. ej. <c>products/7/3f2a….jpg</c>), en minúsculas.
/// </summary>
public interface IFileStorage
{
    /// <summary>Guarda (o reemplaza) el archivo completo; si falla no queda un archivo a medias.</summary>
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Abre el archivo para leerlo; <c>null</c> si no existe. Quien llama cierra el stream.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Borra el archivo (no falla si no existe).</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
