using EFDB;
using Microsoft.EntityFrameworkCore;

namespace Services.Ordering;

public sealed class EfOrderRepository(IDbContextFactory<AppDbContext> contextFactory) : IOrderRepository
{
    public async Task CreateAsync(PlacedOrder order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        var (firstName, lastName) = SplitName(order.Customer.FullName);
        var entity = new EFDB.Order
        {
            Id = Guid.NewGuid(),
            Number = order.Number,
            CreatedAt = order.PlacedAt,
            UpdatedAt = order.PlacedAt,
            Status = "new",
            EmailNotificationStatus = OrderEmailNotificationStatuses.Pending,
            CustomerFirstName = firstName,
            CustomerLastName = lastName,
            CustomerEmail = order.Customer.Email,
            CustomerPhone = order.Customer.Phone,
            Street = order.Customer.Street,
            PostalCode = order.Customer.PostalCode,
            City = order.Customer.City,
            Notes = order.Customer.Comments,
            TermsAccepted = order.Customer.AcceptTerms,
            Language = order.Language.ToLowerInvariant(),
            Subtotal = order.Total,
            Shipping = 0,
            Total = order.Total,
            Currency = "CHF",
            Items = order.Items.Select((item, position) => new OrderItem
            {
                Position = position,
                ItemType = ItemType(item.Type),
                Name = item.Name,
                ImagePath = item.ImagePath,
                UnitPrice = item.UnitPrice,
                Quantity = item.Quantity,
                ConfigurationOrderType = item.ConfigurationOrderType,
                ServicePackageCode = item.ServicePackageCode,
                PcPresetId = item.PcPresetId,
                Details = item.Details.Select((detail, detailPosition) => new OrderItemDetail
                {
                    Position = detailPosition,
                    Label = detail.Label,
                    Value = detail.Value
                }).ToList(),
                Components = (item.Components ?? []).Select((component, componentPosition) => new OrderItemComponent
                {
                    Position = componentPosition,
                    ProductId = component.ProductId,
                    CategoryCode = component.CategoryCode,
                    ProductName = component.ProductName,
                    Manufacturer = component.Manufacturer,
                    ManufacturerPartNumber = component.ManufacturerPartNumber,
                    RetailerName = component.RetailerName,
                    UnitPrice = component.UnitPrice,
                    Currency = component.Currency,
                    Quantity = component.Quantity
                }).ToList()
            }).ToList()
        };

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Orders.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetEmailNotificationResultAsync(
        string orderNumber,
        string status,
        string? error,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        if (status is not (OrderEmailNotificationStatuses.Sent or OrderEmailNotificationStatuses.Failed))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var order = await context.Orders.SingleAsync(entity => entity.Number == orderNumber, cancellationToken);
        order.EmailNotificationStatus = status;
        order.EmailNotificationError = string.IsNullOrWhiteSpace(error) ? null : error[..Math.Min(error.Length, 2000)];
        order.EmailSentAt = status == OrderEmailNotificationStatuses.Sent ? updatedAt : null;
        order.UpdatedAt = updatedAt;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static (string FirstName, string LastName) SplitName(string fullName)
    {
        var parts = fullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? ("-", "-")
            : parts.Length == 1 ? (parts[0], "-")
            : (parts[0], parts[1]);
    }

    private static string ItemType(CartItemType type) => type switch
    {
        CartItemType.PcPreset => "pc_preset",
        CartItemType.CustomConfiguration => "custom_configuration",
        CartItemType.Laptop => "laptop",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
