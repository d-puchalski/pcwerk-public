using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.JSInterop;
using Services.Ordering;

namespace PcSell.Infrastructure;

public sealed class CartBrowserPersistence : IAsyncDisposable
{
    private const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly CartService _cart;
    private readonly CartRefreshService _cartRefresh;
    private readonly IJSRuntime _js;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CartBrowserPersistence> _logger;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private long _changeVersion;
    private long _lastSavedVersion;
    private bool _initialized;
    private bool _disposed;

    public CartBrowserPersistence(
        CartService cart,
        CartRefreshService cartRefresh,
        IJSRuntime js,
        IDataProtectionProvider dataProtectionProvider,
        TimeProvider timeProvider,
        ILogger<CartBrowserPersistence> logger)
    {
        _cart = cart;
        _cartRefresh = cartRefresh;
        _js = js;
        _protector = dataProtectionProvider.CreateProtector("PCWerk.CartBrowserPersistence.v1");
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized || _disposed)
        {
            return;
        }

        _initialized = true;
        var snapshot = await LoadAsync(cancellationToken);
        if (snapshot is null || snapshot.Items.Count == 0)
        {
            _cart.Changed += CartChanged;
            return;
        }

        _cart.ReplaceWith(snapshot.Items);
        _cart.Changed += CartChanged;

        // Browser storage is only a durable snapshot. Catalog data remains the
        // authority, so prices and availability are checked on every new visit.
        var versionBeforeRefresh = Volatile.Read(ref _changeVersion);
        try
        {
            var refresh = await _cartRefresh.RefreshAsync(snapshot.Items, cancellationToken);
            if (refresh.Failure != CartRefreshFailure.ProductUnavailable
                && versionBeforeRefresh == Volatile.Read(ref _changeVersion))
            {
                _cart.ReplaceWith(refresh.Items);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A temporary catalog failure must not make a saved cart disappear.
            // OrderService performs the same authoritative refresh at checkout.
            _logger.LogWarning(exception, "Could not refresh the restored cart");
        }
    }

    private async Task<CartSnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var protectedValue = await _js.InvokeAsync<string?>(
                "pcCart.get",
                cancellationToken);
            if (string.IsNullOrWhiteSpace(protectedValue))
            {
                return null;
            }

            var json = _protector.Unprotect(protectedValue);
            var snapshot = JsonSerializer.Deserialize<CartSnapshot>(json, JsonOptions);
            return IsValid(snapshot) ? snapshot : await RemoveInvalidSnapshotAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is CryptographicException
            or JsonException
            or NotSupportedException)
        {
            _logger.LogInformation(exception, "Ignoring an invalid saved cart");
            return await RemoveInvalidSnapshotAsync(cancellationToken);
        }
        catch (JSException exception)
        {
            _logger.LogWarning(exception, "Browser storage is unavailable; cart persistence is disabled for this visit");
            return null;
        }
    }

    private async Task<CartSnapshot?> RemoveInvalidSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _js.InvokeVoidAsync("pcCart.remove", cancellationToken);
        }
        catch (JSException)
        {
            // The invalid value can be ignored even if browser storage is blocked.
        }

        return null;
    }

    private static bool IsValid(CartSnapshot? snapshot) =>
        snapshot is
        {
            Version: CurrentVersion,
            Items.Count: > 0 and <= 50
        }
        && snapshot.Items.All(item =>
            item.Id != Guid.Empty
            && item.Quantity is > 0 and <= 10
            && item.UnitPrice >= 0
            && !string.IsNullOrWhiteSpace(item.Name)
            && item.Details.Count <= 100
            && (item.Components?.Count ?? 0) <= 100
            && (item.ServiceAddonCodes?.Count ?? 0) <= 50);

    private void CartChanged()
    {
        if (_disposed)
        {
            return;
        }

        var snapshot = new CartSnapshot(
            CurrentVersion,
            _timeProvider.GetUtcNow(),
            _cart.Items.ToArray());
        var version = Interlocked.Increment(ref _changeVersion);
        _ = SaveAsync(snapshot, version);
    }

    private async Task SaveAsync(CartSnapshot snapshot, long version)
    {
        await _saveLock.WaitAsync();
        try
        {
            if (_disposed || version <= _lastSavedVersion)
            {
                return;
            }

            if (snapshot.Items.Count == 0)
            {
                await _js.InvokeVoidAsync("pcCart.remove");
            }
            else
            {
                var json = JsonSerializer.Serialize(snapshot, JsonOptions);
                var protectedValue = _protector.Protect(json);
                await _js.InvokeVoidAsync("pcCart.set", protectedValue);
            }

            _lastSavedVersion = version;
        }
        catch (Exception exception) when (exception is JSException
            or ObjectDisposedException)
        {
            _logger.LogDebug(exception, "Could not save the cart in browser storage");
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _cart.Changed -= CartChanged;
        return ValueTask.CompletedTask;
    }

    private sealed record CartSnapshot(
        int Version,
        DateTimeOffset SavedAt,
        IReadOnlyList<CartItem> Items);
}
