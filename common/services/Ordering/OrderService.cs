using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;

namespace Services.Ordering;

public interface IOrderEmailSender
{
    Task SendAsync(PlacedOrder order, CancellationToken cancellationToken = default);
}

public interface IOrderRepository
{
    Task CreateAsync(PlacedOrder order, CancellationToken cancellationToken = default);

    Task SetEmailNotificationResultAsync(
        string orderNumber,
        string status,
        string? error,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);
}

public sealed class OrderService(
    CartService cart,
    CartRefreshService cartRefresh,
    IOrderEmailSender emailSender,
    IOrderRepository repository,
    TimeProvider timeProvider,
    ILogger<OrderService> logger)
{
    public async Task<PlacedOrder> PlaceAsync(
        CustomerDetails customer,
        string language,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);
        Validator.ValidateObject(customer, new ValidationContext(customer), validateAllProperties: true);
        if (cart.Items.Count == 0)
        {
            throw new InvalidOperationException("The cart is empty.");
        }

        var refresh = await cartRefresh.RefreshAsync(cart.Items, cancellationToken);
        if (refresh.Failure == CartRefreshFailure.ProductUnavailable)
        {
            throw new CartChangedException(
                CartRefreshFailure.ProductUnavailable,
                refresh.ItemName);
        }

        cart.ReplaceWith(refresh.Items);
        if (refresh.Failure == CartRefreshFailure.PriceChanged)
        {
            throw new CartChangedException(CartRefreshFailure.PriceChanged);
        }

        var placedAt = timeProvider.GetUtcNow();
        var order = new PlacedOrder(
            $"PW-{placedAt:yyyyMMdd}-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            placedAt,
            NormalizeLanguage(language),
            CopyCustomer(customer),
            cart.Items.ToArray(),
            cart.Total);

        // Database persistence defines successful placement. Email delivery is
        // tracked separately so an SMTP outage cannot lose or duplicate an order.
        await repository.CreateAsync(order, cancellationToken);

        var emailStatus = OrderEmailNotificationStatuses.Sent;
        string? emailError = null;
        try
        {
            await emailSender.SendAsync(order, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            emailStatus = OrderEmailNotificationStatuses.Failed;
            emailError = exception.Message;
            logger.LogError(exception, "Email notification failed for order {OrderNumber}", order.Number);
        }

        try
        {
            await repository.SetEmailNotificationResultAsync(
                order.Number,
                emailStatus,
                emailError,
                timeProvider.GetUtcNow(),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The order itself is already durable. Do not invite the customer to
            // resubmit it just because updating the notification flag failed.
            logger.LogError(exception, "Could not update email status for order {OrderNumber}", order.Number);
        }

        cart.Clear();
        return order with { EmailNotificationStatus = emailStatus };
    }

    private static CustomerDetails CopyCustomer(CustomerDetails customer) => new()
    {
        FullName = customer.FullName.Trim(),
        Email = customer.Email.Trim(),
        Phone = customer.Phone.Trim(),
        Street = customer.Street.Trim(),
        PostalCode = customer.PostalCode.Trim(),
        City = customer.City.Trim(),
        Comments = string.IsNullOrWhiteSpace(customer.Comments) ? null : customer.Comments.Trim(),
        AcceptTerms = customer.AcceptTerms
    };

    private static string NormalizeLanguage(string? language) => language?.ToUpperInvariant() switch
    {
        "EN" => "EN",
        "IT" => "IT",
        _ => "DE",
    };
}

public sealed class CartChangedException(
    CartRefreshFailure failure,
    string? itemName = null) : InvalidOperationException("The cart changed during checkout.")
{
    public CartRefreshFailure Failure { get; } = failure;
    public string? ItemName { get; } = itemName;
}
