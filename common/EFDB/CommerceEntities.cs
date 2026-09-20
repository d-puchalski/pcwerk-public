namespace EFDB;

public sealed class Retailer
{
    public int Id { get; set; }
    public string ExternalKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; }
    public ICollection<Offer> Offers { get; set; } = [];
}

public sealed class Offer
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public int RetailerId { get; set; }
    public string ExternalOfferKey { get; set; } = string.Empty;
    public string? RetailerSku { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public decimal ProductPrice { get; set; }
    public decimal ShippingPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public string Currency { get; set; } = "CHF";
    public string Availability { get; set; } = "unknown";
    public int? DeliveryMinBusinessDays { get; set; }
    public int? DeliveryMaxBusinessDays { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public bool IsCurrent { get; set; } = true;

    public Product Product { get; set; } = null!;
    public Retailer Retailer { get; set; } = null!;
    public ICollection<OfferObservation> Observations { get; set; } = [];
}

public sealed class OfferObservation
{
    public long Id { get; set; }
    public long OfferId { get; set; }
    public long ImportRunId { get; set; }
    public decimal ProductPrice { get; set; }
    public decimal ShippingPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public string Currency { get; set; } = "CHF";
    public string Availability { get; set; } = "unknown";
    public int? DeliveryMinBusinessDays { get; set; }
    public int? DeliveryMaxBusinessDays { get; set; }
    public DateTimeOffset ObservedAt { get; set; }

    public Offer Offer { get; set; } = null!;
    public ImportRun ImportRun { get; set; } = null!;
}

public sealed class Order
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string Status { get; set; } = "new";
    public string EmailNotificationStatus { get; set; } = "pending";
    public string? EmailNotificationError { get; set; }
    public DateTimeOffset? EmailSentAt { get; set; }
    public string CustomerFirstName { get; set; } = string.Empty;
    public string CustomerLastName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string? Company { get; set; }
    public string? Street { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? Notes { get; set; }
    public bool TermsAccepted { get; set; }
    public string Language { get; set; } = "de";
    public decimal Subtotal { get; set; }
    public decimal Shipping { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "CHF";
    public ICollection<OrderItem> Items { get; set; } = [];
}

public sealed class OrderItem
{
    public long Id { get; set; }
    public Guid OrderId { get; set; }
    public int Position { get; set; }
    public string ItemType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ImagePath { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public string? ConfigurationOrderType { get; set; }
    public string? ServicePackageCode { get; set; }
    public long? PcPresetId { get; set; }
    public Order Order { get; set; } = null!;
    public PcPreset? PcPreset { get; set; }
    public ICollection<OrderItemDetail> Details { get; set; } = [];
    public ICollection<OrderItemComponent> Components { get; set; } = [];
}

public sealed class OrderItemDetail
{
    public long Id { get; set; }
    public long OrderItemId { get; set; }
    public int Position { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public OrderItem OrderItem { get; set; } = null!;
}

public sealed class OrderItemComponent
{
    public long Id { get; set; }
    public long OrderItemId { get; set; }
    public int Position { get; set; }
    public long? ProductId { get; set; }
    public string CategoryCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? ManufacturerPartNumber { get; set; }
    public string? RetailerName { get; set; }
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "CHF";
    public short Quantity { get; set; } = 1;
    public OrderItem OrderItem { get; set; } = null!;
    public Product? Product { get; set; }
}
