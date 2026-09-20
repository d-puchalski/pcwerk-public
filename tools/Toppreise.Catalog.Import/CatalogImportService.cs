using EFDB;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Services.Catalog;
using System.Text.RegularExpressions;

namespace ToppreiseCatalog.Import;

internal sealed class CatalogImportService
{
    private readonly AppDbContext _db;
    private readonly ToppreiseScraper _scraper;
    private readonly ImporterOptions _options;
    private readonly ILogger<CatalogImportService> _logger;
    private readonly PcPresetGenerator _presetGenerator;

    public CatalogImportService(
        AppDbContext db,
        ToppreiseScraper scraper,
        IOptions<ImporterOptions> options,
        PcPresetGenerator presetGenerator,
        ILogger<CatalogImportService> logger)
    {
        _db = db;
        _scraper = scraper;
        _options = options.Value;
        _presetGenerator = presetGenerator;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var run = new ImportRun
        {
            StartedAt = now,
            Status = "running",
            ParserVersion = _options.ParserVersion
        };
        _db.ImportRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken);
        var runId = run.Id;

        try
        {
            var activeSourceCategories = await _db.SourceCategories
                .AsNoTracking()
                .Include(x => x.Category)
                .Where(x => x.IsEnabled && x.Category.IsEnabled)
                .OrderBy(x => x.Category.DisplayOrder)
                .ThenBy(x => x.Id)
                .ToArrayAsync(cancellationToken);
            var sourceCategories = activeSourceCategories
                .Where(x => _options.Categories.IsEnabled(x.Category.Code))
                .ToArray();

            var skippedCategories = activeSourceCategories
                .Where(x => !_options.Categories.IsEnabled(x.Category.Code))
                .Select(x => x.Category.Code)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (skippedCategories.Length > 0)
            {
                _logger.LogInformation(
                    "Kategorie wyłączone w konfiguracji: {Categories}",
                    string.Join(", ", skippedCategories));
            }

            foreach (var sourceCategory in sourceCategories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.LogInformation(
                    "Import Top {Limit}: {Category} ({ExternalId})",
                    sourceCategory.TopLimit,
                    sourceCategory.Category.Code,
                    sourceCategory.ExternalCategoryId);
                try
                {
                    run = await ImportCategoryAsync(run, sourceCategory, cancellationToken);
                    run.CategoryCount++;
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (SourceBlockedException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    AddError(run, "category", sourceCategory.Url, exception.Message);
                    _logger.LogError(exception, "Nie udało się zaimportować kategorii {Category}", sourceCategory.Category.Code);
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }

            try
            {
                await RefreshPresetsAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                _db.ChangeTracker.Clear();
                run = await _db.ImportRuns.SingleAsync(x => x.Id == run.Id, cancellationToken);
                AddError(run, "presets", null, RootMessage(exception));
                _logger.LogError(exception, "Nie udało się przebudować presetów z aktualnego katalogu.");
                await _db.SaveChangesAsync(cancellationToken);
            }

            run.Status = run.ErrorCount == 0 ? "completed" : "partial";
            run.CompletedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Import zakończony: {Categories} kategorii, {Products} produktów, {Offers} ofert, {Errors} błędów.",
                run.CategoryCount,
                run.ProductCount,
                run.OfferCount,
                run.ErrorCount);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _db.ChangeTracker.Clear();
            var failedRun = await _db.ImportRuns.SingleAsync(x => x.Id == runId, CancellationToken.None);
            failedRun.Status = "failed";
            failedRun.CompletedAt = DateTimeOffset.UtcNow;
            failedRun.ErrorMessage = Limit(exception.Message, 4000);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<ImportRun> ImportCategoryAsync(
        ImportRun run,
        SourceCategory sourceCategory,
        CancellationToken cancellationToken)
    {
        var rankedProducts = await _scraper.ReadRankingAsync(
            sourceCategory.Url,
            sourceCategory.TopLimit,
            cancellationToken);
        var rankedProductIds = rankedProducts
            .Select(x => x.ToppreiseProductId)
            .ToArray();
        _logger.LogInformation(
            "POBRANO RANKING {Category}: {Count} produktów z {Url}",
            sourceCategory.Category.Code,
            rankedProducts.Count,
            sourceCategory.Url);
        var observedAt = DateTimeOffset.UtcNow;
        var snapshot = new RankingSnapshot
        {
            SourceCategoryId = sourceCategory.Id,
            ImportRunId = run.Id,
            ObservedAt = observedAt,
            ItemCount = 0
        };
        _db.RankingSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(cancellationToken);
        var visibleCount = 0;
        var hiddenCount = 0;
        var failedCount = 0;

        foreach (var rankedProduct in rankedProducts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var outcome = await ImportRankedProductAsync(
                    run,
                    snapshot,
                    sourceCategory,
                    rankedProduct,
                    observedAt,
                    cancellationToken);
                if (outcome.Product.IsVisible)
                {
                    visibleCount++;
                    _logger.LogInformation(
                        "{Action} [{Category} #{Rank}/{Total}] {Name} | MPN: {Mpn} | oferty: {Offers} | najlepsza cena: {BestPrice} CHF",
                        outcome.IsNew ? "DODANO" : outcome.Refreshed ? "ZAKTUALIZOWANO" : "BEZ ZMIAN",
                        sourceCategory.Category.Code,
                        rankedProduct.Rank,
                        rankedProducts.Count,
                        outcome.Product.Name,
                        outcome.Product.ManufacturerPartNumber ?? "brak",
                        outcome.CurrentOfferCount,
                        outcome.BestPrice);
                }
                else
                {
                    hiddenCount++;
                    _logger.LogWarning(
                        "UKRYTO [{Category} #{Rank}/{Total}] {Name} | status specyfikacji: {SpecificationStatus} | oferty: {Offers} | powód: {Reason}",
                        sourceCategory.Category.Code,
                        rankedProduct.Rank,
                        rankedProducts.Count,
                        outcome.Product.Name,
                        outcome.Product.SpecificationStatus,
                        outcome.CurrentOfferCount,
                        outcome.VisibilityReason ?? "produkt nie spełnia warunków publikacji");
                }
            }
            catch (SourceBlockedException)
            {
                _db.ChangeTracker.Clear();
                throw;
            }
            catch (Exception exception)
            {
                failedCount++;
                _db.ChangeTracker.Clear();
                run = await _db.ImportRuns.SingleAsync(x => x.Id == run.Id, cancellationToken);
                snapshot = await _db.RankingSnapshots
                    .Include(x => x.Items)
                    .SingleAsync(x => x.Id == snapshot.Id, cancellationToken);
                AddError(run, "product", rankedProduct.Url, exception.Message);
                _logger.LogWarning(
                    exception,
                    "POMINIĘTO [{Category} #{Rank}/{Total}] {Name} | powód: {Reason}",
                    sourceCategory.Category.Code,
                    rankedProduct.Rank,
                    rankedProducts.Count,
                    rankedProduct.Name,
                    RootMessage(exception));
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        var productsOutsideRanking = await _db.Products
            .AsNoTracking()
            .Where(x => x.SourceCategoryId == sourceCategory.Id
                        && !rankedProductIds.Contains(x.ToppreiseProductId))
            .OrderBy(x => x.Id)
            .Select(x => new RankedProduct(
                x.ToppreiseProductId,
                0,
                x.Name,
                x.SourceUrl))
            .ToArrayAsync(cancellationToken);

        foreach (var existingProduct in productsOutsideRanking)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var outcome = await RefreshExistingProductAsync(
                    run,
                    sourceCategory,
                    existingProduct,
                    observedAt,
                    cancellationToken);
                if (outcome.Product.IsVisible)
                {
                    visibleCount++;
                    _logger.LogInformation(
                        "ZWERYFIKOWANO POZA RANKINGIEM [{Category}] {Name} | oferty: {Offers} | najlepsza cena: {BestPrice} CHF",
                        sourceCategory.Category.Code,
                        outcome.Product.Name,
                        outcome.CurrentOfferCount,
                        outcome.BestPrice);
                }
                else
                {
                    hiddenCount++;
                    _logger.LogWarning(
                        "WYCOFANO POZA RANKINGIEM [{Category}] {Name} | oferty: {Offers} | powód: {Reason}",
                        sourceCategory.Category.Code,
                        outcome.Product.Name,
                        outcome.CurrentOfferCount,
                        outcome.VisibilityReason ?? "brak aktualnej oferty");
                }
            }
            catch (SourceBlockedException)
            {
                _db.ChangeTracker.Clear();
                throw;
            }
            catch (Exception exception)
            {
                failedCount++;
                _db.ChangeTracker.Clear();
                run = await _db.ImportRuns.SingleAsync(x => x.Id == run.Id, cancellationToken);
                AddError(run, "product-refresh", existingProduct.Url, exception.Message);
                _logger.LogWarning(
                    exception,
                    "NIE ZMIENIONO [{Category}] {Name}; nie udało się zweryfikować strony produktu | powód: {Reason}",
                    sourceCategory.Category.Code,
                    existingProduct.Name,
                    RootMessage(exception));
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        _logger.LogInformation(
            "KONIEC KATEGORII {Category}: ranking {RankingCount}, poza rankingiem {OutsideRankingCount}, widoczne {Visible}, ukryte {Hidden}, pominięte {Failed}.",
            sourceCategory.Category.Code,
            rankedProducts.Count,
            productsOutsideRanking.Length,
            visibleCount,
            hiddenCount,
            failedCount);
        return run;
    }

    private async Task<ProductImportOutcome> ImportRankedProductAsync(
        ImportRun run,
        RankingSnapshot snapshot,
        SourceCategory sourceCategory,
        RankedProduct rankedProduct,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var outcome = await UpsertProductAsync(
                run,
                sourceCategory,
                rankedProduct,
                observedAt,
                cancellationToken);
            snapshot.Items.Add(new RankingItem
            {
                Rank = rankedProduct.Rank,
                ProductId = outcome.Product.Id,
                Currency = "CHF"
            });
            run.ProductCount++;
            snapshot.ItemCount++;
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return outcome;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<ProductImportOutcome> RefreshExistingProductAsync(
        ImportRun run,
        SourceCategory sourceCategory,
        RankedProduct product,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var outcome = await UpsertProductAsync(
                run,
                sourceCategory,
                product,
                observedAt,
                cancellationToken);
            run.ProductCount++;
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return outcome;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<ProductImportOutcome> UpsertProductAsync(
        ImportRun run,
        SourceCategory sourceCategory,
        RankedProduct rankedProduct,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var product = await _db.Products
            .Include(x => x.Offers)
            .SingleOrDefaultAsync(x => x.ToppreiseProductId == rankedProduct.ToppreiseProductId, cancellationToken);
        var isNew = product is null;
        product ??= new Product
        {
            CategoryId = sourceCategory.CategoryId,
            SourceCategoryId = sourceCategory.Id,
            ToppreiseProductId = rankedProduct.ToppreiseProductId,
            Name = Required(rankedProduct.Name, 500),
            SourceUrl = Required(rankedProduct.Url, 1000),
            LifecycleStatus = "active",
            SpecificationStatus = "pending",
            FirstSeenAt = observedAt,
            LastSeenAt = observedAt,
            UpdatedAt = observedAt
        };
        if (isNew)
        {
            _db.Products.Add(product);
        }

        product.CategoryId = sourceCategory.CategoryId;
        product.SourceCategoryId = sourceCategory.Id;
        product.Name = Required(rankedProduct.Name, 500);
        product.SourceUrl = Required(rankedProduct.Url, 1000);
        product.CurrentRank = rankedProduct.Rank;
        if (rankedProduct.Rank > 0)
        {
            product.RankedAt = observedAt;
        }
        product.LastSeenAt = observedAt;
        product.LastSeenImportRunId = run.Id;
        product.UpdatedAt = observedAt;

        // Każdy przebieg ponownie otwiera stronę produktu. Pozycja poza Top 100
        // nie oznacza wycofania; rozstrzyga to dopiero aktualna lista ofert.
        string? visibilityReason = null;
        var scraped = await _scraper.ReadProductAsync(
            sourceCategory.Category.Code,
            sourceCategory.ProductSubtype,
            rankedProduct,
            cancellationToken);
        product.Name = Required(scraped.Name, 500);
        product.Manufacturer = Optional(scraped.Manufacturer, 120);
        product.ManufacturerPartNumber = Optional(scraped.ManufacturerPartNumber, 160);
        product.Ean = NormalizeEan(scraped.Ean);
        product.ImageUrl = Optional(scraped.ImageUrl, 1000);
        await _db.SaveChangesAsync(cancellationToken);

        var validationError = ValidateProductCategory(
                                  sourceCategory.Category.Code,
                                  scraped.Name)
                              ?? ValidateSpecifications(
                                  sourceCategory.Category.Code,
                                  scraped.Specifications);
        if (validationError is null)
        {
            await ReplaceSpecificationsAsync(
                product.Id,
                sourceCategory.Category.Code,
                scraped.Specifications,
                cancellationToken);
            product.SpecificationStatus = "valid";
            product.SpecificationsObservedAt = observedAt;
        }
        else
        {
            product.SpecificationStatus = "incomplete";
            visibilityReason = validationError;
            AddError(run, "specification", rankedProduct.Url, validationError);
        }

        await ReplaceOffersAsync(run, product, scraped.Offers, observedAt, cancellationToken);

        var hasCurrentOffer = product.Offers.Any(x =>
            x.IsCurrent && x.Availability != "unavailable" && x.TotalPrice > 0);
        product.SellingPrice = sourceCategory.Category.Code == CatalogCodes.Laptop && hasCurrentOffer
            ? LaptopPricing.CalculateSellingPrice(product.Offers
                .Where(x => x.IsCurrent && x.Availability != "unavailable" && x.TotalPrice > 0)
                .Min(x => x.TotalPrice))
            : null;
        product.LifecycleStatus = hasCurrentOffer ? "active" : "retired";
        product.IsVisible = product.SpecificationStatus == "valid" && hasCurrentOffer;
        if (!product.IsVisible && visibilityReason is null)
        {
            visibilityReason = product.SpecificationStatus != "valid"
                ? $"status specyfikacji: {product.SpecificationStatus}"
                : "brak aktualnej, dostępnej oferty z poprawną ceną";
        }
        await _db.SaveChangesAsync(cancellationToken);
        var currentOffers = product.Offers
            .Where(x => x.IsCurrent && x.Availability != "unavailable" && x.TotalPrice > 0)
            .ToArray();
        return new ProductImportOutcome(
            product,
            isNew,
            true,
            currentOffers.Length,
            currentOffers.Length == 0 ? null : currentOffers.Min(x => x.TotalPrice),
            visibilityReason);
    }

    private async Task RefreshPresetsAsync(CancellationToken cancellationToken)
        => await _presetGenerator.RefreshAsync(cancellationToken);

    private async Task ReplaceOffersAsync(
        ImportRun run,
        Product product,
        IReadOnlyList<ScrapedOffer> scrapedOffers,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        foreach (var existing in product.Offers)
        {
            existing.IsCurrent = false;
        }

        foreach (var scraped in scrapedOffers)
        {
            var retailerKey = NormalizeKey(scraped.RetailerName);
            var offerKey = Required(scraped.ExternalKey, 240);
            var retailer = await _db.Retailers.SingleOrDefaultAsync(x => x.ExternalKey == retailerKey, cancellationToken);
            if (retailer is null)
            {
                retailer = new Retailer
                {
                    ExternalKey = retailerKey,
                    Name = Required(scraped.RetailerName, 200)
                };
                _db.Retailers.Add(retailer);
                await _db.SaveChangesAsync(cancellationToken);
            }

            var offer = product.Offers.FirstOrDefault(x =>
                x.RetailerId == retailer.Id && x.ExternalOfferKey == offerKey);
            if (offer is null)
            {
                offer = new Offer
                {
                    ProductId = product.Id,
                    RetailerId = retailer.Id,
                    ExternalOfferKey = offerKey
                };
                product.Offers.Add(offer);
            }

            offer.RetailerSku = Optional(scraped.RetailerSku, 160);
            offer.SourceUrl = product.SourceUrl;
            offer.ProductPrice = scraped.ProductPrice;
            offer.ShippingPrice = scraped.ShippingPrice;
            offer.TotalPrice = scraped.TotalPrice;
            offer.Currency = "CHF";
            offer.Availability = scraped.Availability;
            offer.DeliveryMinBusinessDays = scraped.DeliveryMinBusinessDays;
            offer.DeliveryMaxBusinessDays = scraped.DeliveryMaxBusinessDays;
            offer.ObservedAt = observedAt;
            offer.IsCurrent = true;
            await _db.SaveChangesAsync(cancellationToken);

            _db.OfferObservations.Add(new OfferObservation
            {
                OfferId = offer.Id,
                ImportRunId = run.Id,
                ProductPrice = offer.ProductPrice,
                ShippingPrice = offer.ShippingPrice,
                TotalPrice = offer.TotalPrice,
                Currency = offer.Currency,
                Availability = offer.Availability,
                DeliveryMinBusinessDays = offer.DeliveryMinBusinessDays,
                DeliveryMaxBusinessDays = offer.DeliveryMaxBusinessDays,
                ObservedAt = observedAt
            });
            run.OfferCount++;
        }
    }

    private async Task ReplaceSpecificationsAsync(
        long productId,
        string categoryCode,
        ParsedSpecifications specs,
        CancellationToken cancellationToken)
    {
        switch (categoryCode)
        {
            case CatalogCodes.Cpu:
                await ReplaceAsync(new CpuSpecification
                {
                    ProductId = productId, Socket = Required(specs.Socket!, 40), Generation = Optional(specs.CpuGeneration, 80),
                    Cores = specs.Cores, Threads = specs.Threads, BaseClockGhz = specs.BaseClockGhz,
                    BoostClockGhz = specs.BoostClockGhz, TdpWatts = specs.TdpWatts,
                    HasIntegratedGraphics = specs.HasIntegratedGraphics
                }, productId, cancellationToken);
                break;
            case CatalogCodes.Motherboard:
                await ReplaceAsync(new MotherboardSpecification
                {
                    ProductId = productId, Socket = Required(specs.Socket!, 40), Chipset = Optional(specs.Chipset, 80),
                    FormFactor = Required(specs.FormFactor!, 40), MemoryType = Required(specs.MemoryType!, 20),
                    MemorySlots = specs.MemorySlots, MaximumMemoryGb = specs.MaximumMemoryGb,
                    M2SlotCount = specs.M2SlotCount, SataPortCount = specs.SataPortCount,
                    SupportsEcc = specs.SupportsEcc,
                    SupportedCpuGenerations = specs.SupportedCpuGenerations.Select(x =>
                        new MotherboardSupportedCpuGeneration { ProductId = productId, Generation = Required(x, 80) }).ToList(),
                    VideoOutputs = specs.VideoOutputs.Select(x => new MotherboardVideoOutput
                    {
                        ProductId = productId,
                        OutputType = Required(x.OutputType, 24),
                        Version = x.Version.Length == 0 ? string.Empty : Required(x.Version, 16),
                        Quantity = x.Quantity
                    }).ToList()
                }, productId, cancellationToken);
                break;
            case CatalogCodes.Ram:
                await ReplaceAsync(new RamSpecification
                {
                    ProductId = productId, MemoryType = Required(specs.MemoryType!, 20), ModuleFormFactor = Optional(specs.ModuleFormFactor, 20),
                    CapacityGb = specs.CapacityGb!.Value, ModuleCount = specs.ModuleCount!.Value,
                    SpeedMtPerSecond = specs.SpeedMtPerSecond, IsEcc = specs.IsEcc,
                    IsRegistered = specs.IsRegistered, HeightMm = specs.HeightMm
                }, productId, cancellationToken);
                break;
            case CatalogCodes.Gpu:
                await ReplaceAsync(new GpuSpecification
                {
                    ProductId = productId, Chipset = Optional(specs.GpuChipset, 120), MemoryGb = specs.GpuMemoryGb,
                    MemoryType = Optional(specs.GpuMemoryType, 20), TdpWatts = specs.TdpWatts,
                    RecommendedPsuWatts = specs.RecommendedPsuWatts, LengthMm = specs.LengthMm!.Value,
                    HeightMm = specs.GpuHeightMm, SlotWidth = specs.SlotWidth,
                    PowerConnectors = specs.PowerConnectors.Select(x => new GpuPowerConnectorRequirement
                    { ProductId = productId, ConnectorType = Required(x.Key, 32), Quantity = x.Value }).ToList()
                }, productId, cancellationToken);
                break;
            case CatalogCodes.Storage:
                await ReplaceAsync(new StorageSpecification
                {
                    ProductId = productId, StorageType = Required(specs.StorageType!, 24), InterfaceType = Required(specs.InterfaceType!, 40),
                    Protocol = Optional(specs.Protocol, 24), FormFactor = Optional(specs.StorageFormFactor, 24),
                    CapacityGb = specs.CapacityGb!.Value, M2LengthMm = specs.M2LengthMm
                }, productId, cancellationToken);
                break;
            case CatalogCodes.Cooling:
                await ReplaceAsync(new CoolerSpecification
                {
                    ProductId = productId, CoolerType = Required(specs.CoolerType!, 20), HeightMm = specs.HeightMm,
                    RadiatorSizeMm = specs.RadiatorSizeMm, TdpCapacityWatts = specs.TdpCapacityWatts,
                    SupportedSockets = specs.SupportedSockets.Select(x =>
                        new CoolerSupportedSocket { ProductId = productId, Socket = Required(x, 40) }).ToList()
                }, productId, cancellationToken);
                break;
            case CatalogCodes.Psu:
                await ReplaceAsync(new PsuSpecification
                {
                    ProductId = productId, Wattage = specs.Wattage!.Value, FormFactor = Required(specs.FormFactor!, 24),
                    LengthMm = specs.PsuLengthMm, EfficiencyRating = Optional(specs.EfficiencyRating, 40),
                    AtxStandard = Optional(specs.AtxStandard, 24),
                    PowerConnectors = specs.PowerConnectors.Select(x => new PsuPowerConnector
                    { ProductId = productId, ConnectorType = Required(x.Key, 32), Quantity = x.Value }).ToList()
                }, productId, cancellationToken);
                break;
            case CatalogCodes.Case:
                await ReplaceAsync(new CaseSpecification
                {
                    ProductId = productId, MaximumGpuLengthMm = specs.MaximumGpuLengthMm!.Value,
                    MaximumGpuHeightMm = specs.MaximumGpuHeightMm, MaximumGpuSlotWidth = specs.MaximumGpuSlotWidth,
                    MaximumCpuCoolerHeightMm = specs.MaximumCpuCoolerHeightMm,
                    MaximumPsuLengthMm = specs.MaximumPsuLengthMm,
                    SupportedMotherboardFormFactors = specs.SupportedMotherboardFormFactors.Select(x =>
                        new CaseSupportedMotherboardFormFactor { ProductId = productId, FormFactor = Required(x, 40) }).ToList(),
                    SupportedPsuFormFactors = specs.SupportedPsuFormFactors.Select(x =>
                        new CaseSupportedPsuFormFactor { ProductId = productId, FormFactor = Required(x, 40) }).ToList(),
                    SupportedRadiators = specs.SupportedRadiators.Select(x =>
                        new CaseSupportedRadiator { ProductId = productId, Location = Required(x.Location, 40), SizeMm = x.SizeMm }).ToList()
                }, productId, cancellationToken);
                break;
            case CatalogCodes.Monitor:
                await ReplaceAsync(new MonitorSpecification
                {
                    ProductId = productId, ScreenSizeInches = specs.ScreenSizeInches,
                    ResolutionWidth = specs.ResolutionWidth, ResolutionHeight = specs.ResolutionHeight,
                    RefreshRateHz = specs.RefreshRateHz, PanelType = Optional(specs.PanelType, 40)
                }, productId, cancellationToken);
                break;
            case CatalogCodes.Mouse:
                await ReplaceAsync(new MouseSpecification
                {
                    ProductId = productId, MaximumDpi = specs.MaximumDpi,
                    WeightGrams = specs.WeightGrams, Connectivity = Optional(specs.Connectivity, 80)
                }, productId, cancellationToken);
                break;
            case CatalogCodes.Keyboard:
                await ReplaceAsync(new KeyboardSpecification
                {
                    ProductId = productId, Layout = Optional(specs.KeyboardLayout, 40), Size = Optional(specs.KeyboardSize, 40),
                    SwitchType = Optional(specs.SwitchType, 120), Connectivity = Optional(specs.Connectivity, 80)
                }, productId, cancellationToken);
                break;
        }
    }

    private async Task ReplaceAsync<TEntity>(TEntity entity, long productId, CancellationToken cancellationToken)
        where TEntity : class
    {
        await _db.Set<TEntity>().Where(x => EF.Property<long>(x, "ProductId") == productId)
            .ExecuteDeleteAsync(cancellationToken);
        _db.Set<TEntity>().Add(entity);
    }

    internal static string? ValidateProductCategory(string categoryCode, string productName) => categoryCode switch
    {
        CatalogCodes.Mouse when Regex.IsMatch(
            productName,
            @"\b(?:mouse\s*pad|mousepad|mouse\s*mat)\b",
            RegexOptions.IgnoreCase) => "Produkt jest podkładką pod mysz, a nie myszą.",
        _ => null
    };

    internal static string? ValidateSpecifications(string categoryCode, ParsedSpecifications specs) => categoryCode switch
    {
        CatalogCodes.Cpu when string.IsNullOrWhiteSpace(specs.Socket) =>
            "Nie rozpoznano oznaczenia gniazda CPU w danych Toppreise.",
        CatalogCodes.Motherboard when string.IsNullOrWhiteSpace(specs.Socket) =>
            "Płyta nie ma oznaczenia gniazda CPU.",
        CatalogCodes.Motherboard when string.IsNullOrWhiteSpace(specs.FormFactor) =>
            "Toppreise nie podało formatu płyty (np. ATX/Micro-ATX/Mini-ITX).",
        CatalogCodes.Motherboard when string.IsNullOrWhiteSpace(specs.MemoryType) =>
            "Płyta nie ma typu pamięci.",
        CatalogCodes.Ram when string.IsNullOrWhiteSpace(specs.MemoryType)
            || specs.CapacityGb is null or <= 0 || specs.ModuleCount is null or <= 0 => "RAM nie ma typu, pojemności lub liczby modułów.",
        CatalogCodes.Gpu when specs.LengthMm is null or <= 0 =>
            "Toppreise nie podało długości karty graficznej.",
        CatalogCodes.Storage when specs.CapacityGb is null or <= 0
            || string.IsNullOrWhiteSpace(specs.InterfaceType) => "Dysk nie ma pojemności lub interfejsu.",
        CatalogCodes.Cooling when specs.SupportedSockets.Count == 0 =>
            "Chłodzenie nie ma listy obsługiwanych gniazd CPU.",
        CatalogCodes.Cooling when specs.CoolerType == "air" && specs.HeightMm is null or <= 0 =>
            "Toppreise nie podało całkowitej wysokości chłodzenia powietrznego.",
        CatalogCodes.Cooling when specs.CoolerType == "aio" && specs.RadiatorSizeMm is null or <= 0 =>
            "Chłodzenie AIO nie ma rozmiaru chłodnicy.",
        CatalogCodes.Psu when specs.Wattage is null or <= 0 || string.IsNullOrWhiteSpace(specs.FormFactor) => "Zasilacz nie ma mocy lub formatu.",
        CatalogCodes.Case when specs.MaximumGpuLengthMm is null or <= 0 =>
            "Obudowa nie ma maksymalnej długości karty graficznej.",
        CatalogCodes.Case when specs.SupportedMotherboardFormFactors.Count == 0 =>
            "Obudowa nie ma listy obsługiwanych formatów płyt.",
        CatalogCodes.Monitor when specs.ScreenSizeInches is null or <= 0
            || specs.ResolutionWidth is null or <= 0
            || specs.ResolutionHeight is null or <= 0 => "Monitor nie ma przekątnej lub rozdzielczości.",
        _ => null
    };

    internal static bool ShouldRefreshSpecifications(
        DateTimeOffset? specificationsObservedAt,
        string? previousParserVersion,
        string currentParserVersion,
        DateTimeOffset observedAt,
        int refreshDays) =>
        specificationsObservedAt is null
        || specificationsObservedAt < observedAt.AddDays(-Math.Max(1, refreshDays))
        || !string.Equals(previousParserVersion, currentParserVersion, StringComparison.OrdinalIgnoreCase);

    private void AddError(ImportRun run, string stage, string? url, string message)
    {
        run.ErrorCount++;
        _db.ImportErrors.Add(new ImportError
        {
            ImportRunId = run.Id,
            Stage = stage,
            SourceUrl = url,
            Message = Limit(message, 4000),
            OccurredAt = DateTimeOffset.UtcNow
        });
    }

    private static string NormalizeKey(string value)
    {
        var normalized = new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return normalized.Length == 0 ? "unknown" : Limit(normalized, 160);
    }

    private static string? NormalizeEan(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var compact = new string(value.Where(char.IsDigit).ToArray());
        if (compact.Length is >= 8 and <= 14)
        {
            return compact;
        }

        var candidate = System.Text.RegularExpressions.Regex.Match(value, @"(?<!\d)(\d{8,14})(?!\d)");
        return candidate.Success ? candidate.Groups[1].Value : null;
    }

    private static string Required(string value, int length)
    {
        var normalized = value.Trim();
        return Limit(normalized.Length == 0 ? "-" : normalized, length);
    }

    private static string? Optional(string? value, int length) =>
        string.IsNullOrWhiteSpace(value) ? null : Limit(value.Trim(), length);

    private static string RootMessage(Exception exception)
    {
        while (exception.InnerException is not null)
        {
            exception = exception.InnerException;
        }
        return Limit(exception.Message, 1000);
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];

    private sealed record ProductImportOutcome(
        Product Product,
        bool IsNew,
        bool Refreshed,
        int CurrentOfferCount,
        decimal? BestPrice,
        string? VisibilityReason);
}
