using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace ToppreiseCatalog.Import;

internal sealed class ToppreiseScraper : IAsyncDisposable
{
    private const string ToppreiseHomeUrl = "https://www.toppreise.ch/";
    private readonly ILogger<ToppreiseScraper> _logger;
    private readonly ImporterOptions _options;
    private IPlaywright? _playwright;
    private IBrowserContext? _context;
    private IPage? _page;
    private DateTimeOffset _lastNavigationAt = DateTimeOffset.MinValue;
    private bool _sessionInitialized;

    public ToppreiseScraper(ILogger<ToppreiseScraper> logger, IOptions<ImporterOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<RankedProduct>> ReadRankingAsync(
        string categoryUrl,
        int limit,
        CancellationToken cancellationToken)
    {
        var page = await NavigateAsync(categoryUrl, cancellationToken);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
            await Task.Delay(250, cancellationToken);
        }

        // The page also contains product-history and recommendation links. Reading the
        // whole document used to classify those products as members of the ranking.
        var rankingLocator = page.Locator(
            ".Plugin_TopProductsListFull a.Plugin_Product[href]");
        var serializedRows = await rankingLocator.EvaluateAllAsync<string>(
            """
            links => JSON.stringify(links.map(link => ({
                url: link.href,
                name: link.querySelector('img[alt]')?.getAttribute('alt')
                    ?? link.querySelector('.Plugin_ProductName, .productName, .name')?.textContent?.trim()
                    ?? link.getAttribute('title')
                    ?? link.textContent?.trim()
            })))
            """);
        var rows = JsonSerializer.Deserialize<BrowserRankingRow[]>(serializedRows, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];
        var products = ToppreisePageParser.ParseRanking(rows, limit);
        if (products.Count == 0)
        {
            throw new InvalidOperationException($"Nie znaleziono produktów rankingu na {categoryUrl}.");
        }

        return products;
    }

    public async Task<ScrapedProduct> ReadProductAsync(
        string categoryCode,
        string? productSubtype,
        RankedProduct rankedProduct,
        CancellationToken cancellationToken)
    {
        var page = await NavigateAsync(
            rankedProduct.Url + "?selsort=pa&prcst=shipping",
            cancellationToken);
        await ExpandAllOffersAsync(page, cancellationToken);
        var offers = await ReadOffersAsync(page, rankedProduct, cancellationToken);
        return ToppreisePageParser.ParseProduct(
            categoryCode,
            productSubtype,
            rankedProduct.Name,
            await page.ContentAsync(),
            await page.Locator("body").InnerTextAsync(),
            offers);
    }

    private async Task<IReadOnlyList<ScrapedOffer>> ReadOffersAsync(
        IPage page,
        RankedProduct rankedProduct,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var offerLocator = page.Locator("div.Plugin_Offer[data-entity-id]");
        var rowCount = await offerLocator.CountAsync();
        if (rowCount == 0)
        {
            _logger.LogWarning(
                "Toppreise nie zwróciło wierszy ofert dla [{Rank}] {Name} ({Url}).",
                rankedProduct.Rank,
                rankedProduct.Name,
                rankedProduct.Url);
            return [];
        }

        var serializedRows = await offerLocator.EvaluateAllAsync<string>(
            """
            offers => JSON.stringify(offers.map(offer => ({
                externalKey: offer.getAttribute('data-entity-id'),
                retailerName: offer.querySelector('.Plugin_ShopLogo img[alt]')?.getAttribute('alt'),
                productPrice: offer.querySelector('.priceContainer.productPrice .Plugin_Price')?.textContent?.trim(),
                totalPrice: offer.querySelector('.priceContainer.shippingPrice .Plugin_Price')?.textContent?.trim(),
                availabilityText: offer.querySelector('.AbstractTooltip_AvailabilityInformationTooltip')?.getAttribute('title')
            })))
            """);
        var rows = JsonSerializer.Deserialize<BrowserOfferRow[]>(serializedRows, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];
        var offers = ToppreisePageParser.ParseOffers(rows);
        var skipped = rowCount - offers.Count;
        if (skipped > 0)
        {
            _logger.LogWarning(
                "Toppreise [{Rank}] {Name}: wiersze ofert: {Rows}, poprawnie odczytane: {Offers}, pominięte: {Skipped}.",
                rankedProduct.Rank,
                rankedProduct.Name,
                rowCount,
                offers.Count,
                skipped);
        }
        else
        {
            _logger.LogInformation(
                "OFERTY OK [{Rank}] {Name} | odczytano: {Offers}.",
                rankedProduct.Rank,
                rankedProduct.Name,
                offers.Count);
        }

        return offers;
    }

    private async Task<IPage> NavigateAsync(string url, CancellationToken cancellationToken)
    {
        var page = await GetPageAsync();
        await EnsureSessionAsync(page, cancellationToken);
        return await NavigateWithRecoveryAsync(page, url, cancellationToken);
    }

    private async Task EnsureSessionAsync(IPage page, CancellationToken cancellationToken)
    {
        if (_sessionInitialized)
        {
            return;
        }

        _logger.LogInformation("Otwieram Toppreise w trwałej sesji Playwright/Chrome.");
        await NavigateWithRecoveryAsync(page, ToppreiseHomeUrl, cancellationToken);
        _sessionInitialized = true;
    }

    private async Task<IPage> NavigateWithRecoveryAsync(
        IPage page,
        string url,
        CancellationToken cancellationToken)
    {
        var response = await NavigateCoreAsync(page, url, cancellationToken);
        if (await IsBlockedAsync(page, response))
        {
            await WaitForManualVerificationAsync(page, url, response?.Status, cancellationToken);
            response = await NavigateCoreAsync(page, url, cancellationToken);
        }

        if (await IsBlockedAsync(page, response))
        {
            throw new SourceBlockedException(
                $"Toppreise nadal blokuje sesję Playwright (HTTP {response?.Status}). " +
                "Otwórz Toppreise ręcznie w uruchomionym oknie Chrome i ponów import.");
        }
        if (response?.Status == 404)
        {
            throw new InvalidOperationException($"Toppreise zwróciło HTTP 404 dla {url}.");
        }

        return page;
    }

    private async Task<IResponse?> NavigateCoreAsync(
        IPage page,
        string url,
        CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromMilliseconds(Math.Max(250, _options.RequestDelayMilliseconds))
                    - (DateTimeOffset.UtcNow - _lastNavigationAt);
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        _lastNavigationAt = DateTimeOffset.UtcNow;
        _logger.LogDebug("Toppreise: {Url}", url);
        const int maximumAttempts = 3;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            try
            {
                return await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = Math.Max(10000, _options.NavigationTimeoutMilliseconds)
                });
            }
            catch (PlaywrightException exception)
                when (IsNavigationInterrupted(exception) && attempt < maximumAttempts)
            {
                _logger.LogWarning(
                    exception,
                    "Nawigacja Toppreise została przerwana przez inne przekierowanie; ponawiam ({Attempt}/{MaximumAttempts}) dla {Url}.",
                    attempt,
                    maximumAttempts,
                    url);
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
            }
            catch (PlaywrightException exception)
            {
                throw new InvalidOperationException($"Nie można otworzyć strony Toppreise: {exception.Message}", exception);
            }
        }

        throw new InvalidOperationException($"Nie można otworzyć strony Toppreise po {maximumAttempts} próbach: {url}");
    }

    internal static bool IsNavigationInterrupted(PlaywrightException exception) =>
        exception.Message.Contains("is interrupted by another navigation", StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> IsBlockedAsync(IPage page, IResponse? response)
    {
        if (response?.Status is 403 or 429)
        {
            return true;
        }

        var html = await page.ContentAsync();
        return Regex.IsMatch(
            html,
            "massive, ongoing attacks|massiver, anhaltender Angriffe|Error 403",
            RegexOptions.IgnoreCase);
    }

    private async Task WaitForManualVerificationAsync(
        IPage page,
        string requestedUrl,
        int? status,
        CancellationToken cancellationToken)
    {
        if (_options.Headless || Console.IsInputRedirected)
        {
            throw new SourceBlockedException(
                $"Toppreise zwróciło HTTP {status}. Uruchom importer z ToppreiseImport:Headless=false.");
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(30, _options.ManualVerificationTimeoutSeconds));
        _logger.LogWarning(
            "Toppreise zwróciło HTTP {Status}. W otwartym Chrome przejdź ręcznie do {HomeUrl}, " +
            "ukończ ewentualną weryfikację, a następnie wróć do konsoli i naciśnij ENTER (limit {Seconds} s).",
            status,
            ToppreiseHomeUrl,
            (int)timeout.TotalSeconds);
        Console.WriteLine($"Po uzyskaniu dostępu do Toppreise naciśnij ENTER. Docelowy adres: {requestedUrl}");
        try
        {
            await Task.Run(Console.ReadLine, CancellationToken.None).WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new SourceBlockedException(
                $"Nie potwierdzono dostępu do Toppreise w ciągu {(int)timeout.TotalSeconds} sekund.");
        }

        await page.BringToFrontAsync();
    }

    private static async Task ExpandAllOffersAsync(IPage page, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var button = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions
            {
                NameRegex = new Regex("weitere Angebote anzeigen|show more offers|afficher plus d'offres", RegexOptions.IgnoreCase)
            });
            if (await button.CountAsync() == 0 || !await button.First.IsVisibleAsync())
            {
                return;
            }

            var before = await page.Locator("div.Plugin_Offer").CountAsync();
            await button.First.ClickAsync();
            try
            {
                await page.WaitForFunctionAsync(
                    "before => document.querySelectorAll('div.Plugin_Offer').length > before",
                    before,
                    new PageWaitForFunctionOptions { Timeout = 5000 });
            }
            catch (TimeoutException)
            {
                return;
            }
        }
    }

    private async Task<IPage> GetPageAsync()
    {
        if (_page is not null)
        {
            return _page;
        }

        _playwright = await Playwright.CreateAsync();
        var profilePath = ResolveProfilePath();
        Directory.CreateDirectory(profilePath);
        _context = await _playwright.Chromium.LaunchPersistentContextAsync(
            profilePath,
            new BrowserTypeLaunchPersistentContextOptions
            {
                Channel = "chrome",
                Headless = _options.Headless,
                Locale = "de-CH",
                TimezoneId = "Europe/Zurich",
                ViewportSize = _options.Headless ? new ViewportSize { Width = 1440, Height = 1200 } : ViewportSize.NoViewport,
                Args = _options.Headless ? [] : ["--start-maximized"]
            });
        _page = _context.Pages.FirstOrDefault() ?? await _context.NewPageAsync();
        _page.SetDefaultTimeout(Math.Max(10000, _options.NavigationTimeoutMilliseconds));
        return _page;
    }

    private static string ResolveProfilePath()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var legacyProfile = Path.Combine(localApplicationData, "PcSell", "ToppreiseImport", "ChromeProfile");
        return Directory.Exists(legacyProfile)
            ? legacyProfile
            : Path.Combine(localApplicationData, "PcWerk", "ToppreiseCatalogImport", "ChromeProfile");
    }

    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
        _playwright?.Dispose();
    }
}
