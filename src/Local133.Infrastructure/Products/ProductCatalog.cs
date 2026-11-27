using FluentValidation;
using Local133.Application.Products;
using Local133.Application.Storage;
using Local133.Application.Subscriptions;
using Local133.Domain.Products;
using Local133.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Local133.Infrastructure.Products;

/// <summary>
/// Alta, edición y baja lógica de productos. La foto se procesa y se guarda en <see cref="IFileStorage"/> antes de
/// guardar el producto; si la base falla, los archivos nuevos se borran. Los de la foto anterior (reemplazada o
/// quitada) se borran después de guardar.
/// </summary>
internal sealed class ProductCatalog(
    AppDbContext db,
    IFileStorage storage,
    IProductPhotoProcessor photoProcessor,
    IValidator<SaveProductCommand> validator,
    IPlanAccess plans,
    TimeProvider clock,
    ILogger<ProductCatalog> logger) : IProductCatalog
{
    public async Task<SaveProductResult> SaveAsync(SaveProductCommand command, CancellationToken cancellationToken = default)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return new SaveProductResult.Invalid(validation.ToDictionary().AsReadOnly());
        }

        var merchant = await db.Merchants.AsNoTracking().SingleOrDefaultAsync(m => m.UserId == command.UserId, cancellationToken)
            ?? throw new InvalidOperationException($"El usuario {command.UserId} no tiene Merchant.");
        if (!merchant.CanManageProducts)
        {
            return new SaveProductResult.NotAllowed();
        }

        if (!await CanEditCatalogAsync(merchant.Id, cancellationToken))
        {
            return new SaveProductResult.PlanRequired();
        }

        Product? product = null;
        if (command.ProductId is { } productId)
        {
            product = await FindAsync(merchant.Id, productId, cancellationToken);
            if (product is null)
            {
                return new SaveProductResult.NotFound();
            }
        }

        // La foto se procesa primero: si no es una imagen válida no se cambia nada.
        ProcessedProductPhoto? photo = null;
        if (command.Photo is not null)
        {
            switch (await photoProcessor.ProcessAsync(command.Photo.Content, cancellationToken))
            {
                case ProductPhotoProcessingResult.Rejected rejected:
                    return new SaveProductResult.Invalid(new Dictionary<string, string[]>
                    {
                        [nameof(SaveProductCommand.Photo)] = [rejected.Message],
                    }.AsReadOnly());
                case ProductPhotoProcessingResult.Processed processed:
                    photo = processed.Photo;
                    break;
            }
        }

        var now = clock.GetUtcNow();
        if (product is null)
        {
            product = Product.Create(merchant.Id, command.Name!, command.Description, command.Price, now);
            db.Products.Add(product);
        }
        else
        {
            product.Update(command.Name!, command.Description, command.Price, now);
        }

        string? newPhotoKey = null;
        string? previousPhotoKey = null;
        if (photo is not null)
        {
            newPhotoKey = $"products/{merchant.Id}/{Guid.NewGuid():N}";
            await SavePhotoFilesAsync(newPhotoKey, photo, cancellationToken);
            previousPhotoKey = product.ReplacePhoto(newPhotoKey, now);
        }
        else if (command.RemovePhoto)
        {
            previousPhotoKey = product.RemovePhoto(now);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            if (newPhotoKey is not null)
            {
                await DeletePhotoFilesAsync(newPhotoKey);
            }

            throw;
        }

        if (previousPhotoKey is not null)
        {
            await DeletePhotoFilesAsync(previousPhotoKey);
        }

        logger.LogInformation("Producto {ProductId} del vendedor {MerchantId} guardado (foto: {PhotoChange})",
            product.Id, merchant.Id, newPhotoKey is not null ? "nueva" : previousPhotoKey is not null ? "quitada" : "sin cambio");
        return new SaveProductResult.Saved(product.Id);
    }

    public async Task<DeleteProductResult> DeleteAsync(DeleteProductCommand command, CancellationToken cancellationToken = default)
    {
        var merchant = await db.Merchants.AsNoTracking().SingleOrDefaultAsync(m => m.UserId == command.UserId, cancellationToken)
            ?? throw new InvalidOperationException($"El usuario {command.UserId} no tiene Merchant.");
        if (!merchant.CanManageProducts)
        {
            return new DeleteProductResult.NotAllowed();
        }

        if (!await CanEditCatalogAsync(merchant.Id, cancellationToken))
        {
            return new DeleteProductResult.PlanRequired();
        }

        var product = await FindAsync(merchant.Id, command.ProductId, cancellationToken);
        if (product is null)
        {
            return new DeleteProductResult.NotFound();
        }

        // Baja lógica: la fila y la foto se conservan.
        product.Delete(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Producto {ProductId} del vendedor {MerchantId} dado de baja", product.Id, merchant.Id);
        return new DeleteProductResult.Deleted();
    }

    /// <summary>Básico y vencido dejan el catálogo en solo lectura (dado de baja ya se revisó antes: el estatus manda).</summary>
    private async Task<bool> CanEditCatalogAsync(int merchantId, CancellationToken cancellationToken) =>
        (await plans.GetForMerchantAsync(merchantId, cancellationToken)).Entitlements.CanEditCatalog;

    /// <summary>Producto vigente del vendedor, con <c>MerchantId</c> explícito (no depende del claim de la sesión).</summary>
    private Task<Product?> FindAsync(int merchantId, int productId, CancellationToken cancellationToken) =>
        db.Products.IgnoreQueryFilters()
            .SingleOrDefaultAsync(p => p.Id == productId && p.MerchantId == merchantId && !p.IsDeleted, cancellationToken);

    private async Task SavePhotoFilesAsync(string photoKey, ProcessedProductPhoto photo, CancellationToken cancellationToken)
    {
        try
        {
            await SaveFileAsync(ProductPhotoRules.PhotoFileKey(photoKey), photo.Photo, cancellationToken);
            await SaveFileAsync(ProductPhotoRules.ThumbnailFileKey(photoKey), photo.Thumbnail, cancellationToken);
        }
        catch
        {
            await DeletePhotoFilesAsync(photoKey);
            throw;
        }
    }

    private async Task SaveFileAsync(string key, byte[] content, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content, writable: false);
        await storage.SaveAsync(key, stream, ProductPhotoRules.StoredContentType, cancellationToken);
    }

    /// <summary>Borra la foto y su miniatura; si falla solo se registra (el producto ya quedó bien).</summary>
    private async Task DeletePhotoFilesAsync(string photoKey)
    {
        foreach (var key in new[] { ProductPhotoRules.PhotoFileKey(photoKey), ProductPhotoRules.ThumbnailFileKey(photoKey) })
        {
            try
            {
                await storage.DeleteAsync(key, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo borrar el archivo {Key}", key);
            }
        }
    }
}
