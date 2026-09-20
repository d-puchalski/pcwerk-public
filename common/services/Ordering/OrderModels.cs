using System.ComponentModel.DataAnnotations;

namespace Services.Ordering;

public enum CartItemType
{
    PcPreset,
    CustomConfiguration,
    Laptop
}

public sealed record CartItemDetail(
    string Label,
    string Value,
    string? LabelTranslationKey = null,
    string? ValueTranslationKey = null);

public sealed record CartItemComponent(
    long ProductId,
    string CategoryCode,
    string ProductName,
    string? Manufacturer,
    string? ManufacturerPartNumber,
    string RetailerName,
    decimal UnitPrice,
    string Currency,
    short Quantity = 1,
    string? SourceUrl = null);

public sealed record CartItem(
    Guid Id,
    CartItemType Type,
    string Name,
    string? ImagePath,
    IReadOnlyList<CartItemDetail> Details,
    decimal UnitPrice,
    int Quantity,
    IReadOnlyList<CartItemComponent>? Components = null,
    string? ConfigurationOrderType = null,
    string? ServicePackageCode = null,
    long? PcPresetId = null,
    string? NameTranslationKey = null,
    IReadOnlyList<string>? ServiceAddonCodes = null)
{
    public decimal Total => UnitPrice * Quantity;
}

public sealed class CustomerDetails
{
    [Required(ErrorMessage = "Validation.Required"), StringLength(120, ErrorMessage = "Validation.TooLong")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.Required"), EmailAddress(ErrorMessage = "Validation.InvalidEmail"), StringLength(254, ErrorMessage = "Validation.TooLong")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.Required"), Phone(ErrorMessage = "Validation.InvalidPhone"), StringLength(40, ErrorMessage = "Validation.TooLong")]
    public string Phone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.Required"), StringLength(160, ErrorMessage = "Validation.TooLong")]
    public string Street { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.Required"), StringLength(20, ErrorMessage = "Validation.TooLong")]
    public string PostalCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.Required"), StringLength(100, ErrorMessage = "Validation.TooLong")]
    public string City { get; set; } = string.Empty;

    [StringLength(1500, ErrorMessage = "Validation.TooLong")]
    public string? Comments { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "Validation.AcceptTerms")]
    public bool AcceptTerms { get; set; }
}

public sealed record PlacedOrder(
    string Number,
    DateTimeOffset PlacedAt,
    string Language,
    CustomerDetails Customer,
    IReadOnlyList<CartItem> Items,
    decimal Total,
    string EmailNotificationStatus = OrderEmailNotificationStatuses.Pending);

public static class OrderEmailNotificationStatuses
{
    public const string Pending = "pending";
    public const string Sent = "sent";
    public const string Failed = "failed";
}

public sealed class OrderEmailOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "PCWerk Website";
    public string OrderRecipient { get; set; } = "info@pcwerk.ch";
}
