using EFDB;
using Microsoft.EntityFrameworkCore;
using Services.Models;

namespace Services.Catalog;

public sealed class LaptopCatalogService(IDbContextFactory<AppDbContext> contextFactory)
{
    public async Task<IReadOnlyList<CatalogLaptop>> GetAvailableLaptopsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var products = await AvailableProducts(db)
            .Where(x => x.CurrentRank > 0 && x.CurrentRank <= 100)
            .OrderBy(x => x.CurrentRank == 0 ? int.MaxValue : x.CurrentRank)
            .ThenBy(x => x.Name)
            .ToArrayAsync(cancellationToken);
        return products.Select(Map).ToArray();
    }

    public async Task<CatalogLaptop?> GetAvailableLaptopAsync(
        long productId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var product = await AvailableProducts(db)
            .SingleOrDefaultAsync(x => x.Id == productId, cancellationToken);
        return product is null ? null : Map(product);
    }

    private static IQueryable<Product> AvailableProducts(AppDbContext db) => db.Products
        .AsNoTracking()
        .Where(x => x.Category.Code == CatalogCodes.Laptop
            && x.Category.IsEnabled
            && x.IsVisible
            && x.LifecycleStatus == "active"
            && x.SpecificationStatus == "valid"
            && x.SellingPrice != null
            && x.SellingPrice > 0
            && x.Offers.Any(offer =>
                offer.IsCurrent
                && offer.Currency == "CHF"
                && offer.Availability != "unavailable"
                && offer.TotalPrice > 0
                && offer.Retailer.IsEnabled));

    private static CatalogLaptop Map(Product product) => new(
        product.Id,
        product.Name,
        product.Manufacturer,
        product.ManufacturerPartNumber,
        product.ImageUrl,
        product.SourceUrl,
        product.SellingPrice!.Value,
        "CHF",
        product.CurrentRank,
        LaptopComponentParser.Parse(product.Name));
}
