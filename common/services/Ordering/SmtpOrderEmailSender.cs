using System.Net;
using System.Net.Mail;
using System.Text;
using System.Globalization;

namespace Services.Ordering;

public sealed class SmtpOrderEmailSender(OrderEmailOptions options) : IOrderEmailSender
{
    public async Task SendAsync(PlacedOrder order, CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();
        cancellationToken.ThrowIfCancellationRequested();

        using var message = new MailMessage
        {
            From = new MailAddress(FromAddress(), options.FromName),
            Subject = BuildSubject(order),
            SubjectEncoding = Encoding.UTF8,
            Body = BuildBody(order),
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = true
        };
        message.To.Add(options.OrderRecipient);
        message.ReplyToList.Add(new MailAddress(order.Customer.Email, order.Customer.FullName));

        using var client = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = options.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false
        };
        if (!string.IsNullOrWhiteSpace(options.UserName))
        {
            client.Credentials = new NetworkCredential(options.UserName, options.Password);
        }
        await client.SendMailAsync(message, cancellationToken);
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(options.Host))
        {
            throw new InvalidOperationException("Email:Host is not configured.");
        }
        if (string.IsNullOrWhiteSpace(options.OrderRecipient))
        {
            throw new InvalidOperationException("Email:OrderRecipient is not configured.");
        }
        _ = new MailAddress(FromAddress());
        _ = new MailAddress(options.OrderRecipient);
    }

    private string FromAddress() => string.IsNullOrWhiteSpace(options.FromAddress)
        ? options.UserName
        : options.FromAddress;

    internal static string BuildSubject(PlacedOrder order)
    {
        var computerNames = order.Items
            .Where(item => item.Type == CartItemType.PcPreset)
            .Select(item => item.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return computerNames.Length == 1
            ? $"New PCWerk order {order.Number} — {computerNames[0]}"
            : $"New PCWerk order {order.Number}";
    }

    internal static string BuildBody(PlacedOrder order)
    {
        const string page = "max-width:780px;margin:0 auto;background:#f7f4f8;border:3px solid #332f36;";
        const string sectionTitle = "margin:0 0 12px;color:#332f36;font-size:18px;line-height:1.2;";
        const string label = "color:#625b66;font-size:11px;font-weight:bold;letter-spacing:.08em;text-transform:uppercase;";
        const string cell = "padding:11px 10px;border-bottom:1px solid #d8cedd;vertical-align:top;color:#332f36;font-size:13px;line-height:1.4;";
        const string headerCell = "padding:10px;background:#332f36;color:#fff;font-size:11px;letter-spacing:.05em;text-align:left;text-transform:uppercase;";

        var html = new StringBuilder("<!doctype html><html><body style=\"margin:0;padding:24px;background:#f1edf3;font-family:Arial,Helvetica,sans-serif;color:#332f36;\">");
        html.Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\"><tr><td>")
            .Append("<div style=\"").Append(page).Append("\">")
            .Append("<div style=\"padding:22px 28px;background:#332f36;color:#fff;\">")
            .Append("<div style=\"font-size:12px;font-weight:bold;letter-spacing:.18em;\">PCWERK</div>")
            .Append("<h1 style=\"margin:8px 0 0;font-size:27px;line-height:1.15;\">New order ").Append(E(order.Number)).Append("</h1>")
            .Append("</div>")
            .Append("<div style=\"padding:24px 28px;\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"margin-bottom:24px;background:#f1edf3;border-left:4px solid #81748b;\"><tr>")
            .Append("<td style=\"padding:12px 14px;\"><span style=\"").Append(label).Append("\">Placed</span><br><strong>")
            .Append(E(order.PlacedAt.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture))).Append("</strong></td>")
            .Append("<td style=\"padding:12px 14px;\"><span style=\"").Append(label).Append("\">Language</span><br><strong>")
            .Append(E(order.Language.ToUpperInvariant())).Append("</strong></td>")
            .Append("<td style=\"padding:12px 14px;text-align:right;\"><span style=\"").Append(label).Append("\">Order total</span><br><strong style=\"font-size:20px;\">")
            .Append(Money(order.Total)).Append("</strong></td></tr></table>")
            .Append("<h2 style=\"").Append(sectionTitle).Append("\">Customer</h2>")
            .Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"margin-bottom:28px;border:1px solid #ada4b2;\">")
            .Append(InfoRow("Name", order.Customer.FullName, cell))
            .Append(InfoRow("Email", order.Customer.Email, cell, $"mailto:{order.Customer.Email}"))
            .Append(InfoRow("Phone", order.Customer.Phone, cell))
            .Append(InfoRow("Address", $"{order.Customer.Street}, {order.Customer.PostalCode} {order.Customer.City}", cell))
            .Append("</table>")
            .Append("<h2 style=\"").Append(sectionTitle).Append("\">Order items</h2>");

        foreach (var item in order.Items)
        {
            html.Append("<div style=\"margin:0 0 24px;border:2px solid #81748b;\">")
                .Append("<div style=\"padding:14px 16px;background:#e7e1ea;\">");
            if (item.Type == CartItemType.PcPreset)
            {
                html.Append("<span style=\"").Append(label).Append("\">Computer model</span>")
                    .Append("<h3 style=\"margin:4px 0 0;font-size:19px;\">").Append(E(item.Name)).Append("</h3>");
            }
            else if (item.Type == CartItemType.Laptop)
            {
                html.Append("<span style=\"").Append(label).Append("\">Laptop</span>")
                    .Append("<h3 style=\"margin:4px 0 0;font-size:19px;\">").Append(E(item.Name)).Append("</h3>");
            }
            else
            {
                html.Append("<span style=\"").Append(label).Append("\">Custom configuration</span>")
                    .Append("<h3 style=\"margin:4px 0 0;font-size:19px;\">Individually configured PC</h3>");
            }
            html.Append("<div style=\"margin-top:5px;color:#625b66;font-size:13px;\">")
                .Append(item.Quantity).Append(" × ").Append(Money(item.UnitPrice)).Append(" = <strong>").Append(Money(item.Total)).Append("</strong></div></div>");

            if (item.Components is { Count: > 0 })
            {
                html.Append("<table width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse:collapse;\">")
                    .Append("<thead><tr>")
                    .Append("<th style=\"").Append(headerCell).Append("\">Part</th>")
                    .Append("<th style=\"").Append(headerCell).Append("\">Product</th>")
                    .Append("<th style=\"").Append(headerCell).Append("\">Provider</th>")
                    .Append("<th style=\"").Append(headerCell).Append("text-align:center;\">Qty</th>")
                    .Append("<th style=\"").Append(headerCell).Append("text-align:right;\">Price</th>")
                    .Append("<th style=\"").Append(headerCell).Append("text-align:center;\">Link</th>")
                    .Append("</tr></thead><tbody>");
                foreach (var component in item.Components)
                {
                    var url = SafeUrl(component.SourceUrl);
                    html.Append("<tr>")
                        .Append("<td style=\"").Append(cell).Append("font-weight:bold;\">").Append(E(CategoryLabel(component.CategoryCode))).Append("</td>")
                        .Append("<td style=\"").Append(cell).Append("\"><strong>").Append(E(component.ProductName)).Append("</strong>");
                    if (!string.IsNullOrWhiteSpace(component.ManufacturerPartNumber))
                    {
                        html.Append("<br><span style=\"color:#625b66;font-size:11px;\">MPN: ").Append(E(component.ManufacturerPartNumber)).Append("</span>");
                    }
                    html.Append("</td>")
                        .Append("<td style=\"").Append(cell).Append("\">").Append(E(component.RetailerName)).Append("</td>")
                        .Append("<td style=\"").Append(cell).Append("text-align:center;\">").Append(component.Quantity).Append("</td>")
                        .Append("<td style=\"").Append(cell).Append("text-align:right;white-space:nowrap;\">").Append(Money(component.UnitPrice * component.Quantity, component.Currency)).Append("</td>")
                        .Append("<td style=\"").Append(cell).Append("text-align:center;white-space:nowrap;\">");
                    if (url is not null)
                    {
                        html.Append("<a href=\"").Append(E(url)).Append("\" style=\"display:inline-block;padding:6px 9px;background:#5d5067;color:#fff;font-size:11px;font-weight:bold;text-decoration:none;\">OPEN</a>");
                    }
                    else
                    {
                        html.Append("—");
                    }
                    html.Append("</td></tr>");
                }
                html.Append("</tbody></table>");
            }

            if (item.Details.Count > 0)
            {
                html.Append("<div style=\"padding:12px 16px;background:#f7f4f8;\"><strong style=\"font-size:12px;\">Configuration and services</strong><ul style=\"margin:8px 0 0;padding-left:18px;color:#625b66;font-size:12px;line-height:1.55;\">");
                foreach (var detail in item.Details)
                {
                    html.Append("<li><strong>").Append(E(detail.Label)).Append(":</strong> ").Append(E(detail.Value)).Append("</li>");
                }
                html.Append("</ul></div>");
            }
            html.Append("</div>");
        }

        html.Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"margin-top:6px;background:#332f36;color:#fff;\"><tr>")
            .Append("<td style=\"padding:16px;font-size:14px;font-weight:bold;\">ORDER TOTAL</td>")
            .Append("<td style=\"padding:16px;text-align:right;font-size:24px;font-weight:bold;\">").Append(Money(order.Total)).Append("</td>")
            .Append("</tr></table>");
        if (!string.IsNullOrWhiteSpace(order.Customer.Comments))
        {
            html.Append("<h2 style=\"margin:26px 0 10px;font-size:18px;\">Customer comments</h2><p style=\"margin:0;padding:14px;background:#fff8e8;border-left:4px solid #b78336;font-size:13px;line-height:1.55;\">")
                .Append(E(order.Customer.Comments).Replace("\n", "<br>"))
                .Append("</p>");
        }
        html.Append("</div></div></td></tr></table></body></html>");
        return html.ToString();
    }

    private static string InfoRow(string labelText, string? value, string cellStyle, string? link = null)
    {
        var displayedValue = E(value);
        var safeLink = SafeUrl(link, allowMailTo: true);
        if (safeLink is not null)
        {
            displayedValue = $"<a href=\"{E(safeLink)}\" style=\"color:#5d5067;font-weight:bold;\">{displayedValue}</a>";
        }
        return $"<tr><td style=\"{cellStyle}width:120px;color:#625b66;font-weight:bold;\">{E(labelText)}</td><td style=\"{cellStyle}\">{displayedValue}</td></tr>";
    }

    private static string CategoryLabel(string categoryCode) => categoryCode.ToUpperInvariant() switch
    {
        "CPU" => "Processor",
        "GPU" => "Graphics card",
        "MOTHERBOARD" => "Motherboard",
        "RAM" => "Memory",
        "STORAGE" => "Storage",
        "COOLING" => "CPU cooling",
        "PSU" => "Power supply",
        "CASE" => "Case",
        "MONITOR" => "Monitor",
        "MOUSE" => "Mouse",
        "KEYBOARD" => "Keyboard",
        "LAPTOP" => "Laptop",
        _ => categoryCode
    };

    private static string? SafeUrl(string? value, bool allowMailTo = false)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return null;
        return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || (allowMailTo && uri.Scheme.Equals(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase))
                ? uri.AbsoluteUri
                : null;
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    private static string Money(decimal value, string currency = "CHF") => $"{E(currency)} {value.ToString("N2", CultureInfo.InvariantCulture)}";
}
