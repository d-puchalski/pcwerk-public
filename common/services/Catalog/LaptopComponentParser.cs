using System.Text;
using System.Text.RegularExpressions;
using Services.Models;

namespace Services.Catalog;

internal static partial class LaptopComponentParser
{
    public static IReadOnlyList<CatalogLaptopComponent> Parse(string productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            return [];
        }

        var segments = SplitTopLevelSegments(productName).Skip(1).ToArray();
        var components = new List<CatalogLaptopComponent>();

        AddFirstMatchingSegment(components, segments, "CPU", ProcessorRegex());
        AddFirstMatchingSegment(components, segments, "GPU", GraphicsRegex());
        AddFirstMatchingValue(components, segments, "DISPLAY", DisplayRegex(), skipStorageSegments: false);
        AddFirstMatchingValue(components, segments, "RAM", MemoryRegex(), skipStorageSegments: true);
        AddFirstMatchingValue(components, segments, "STORAGE", StorageRegex(), skipStorageSegments: false);
        AddFirstMatchingValue(components, segments, "POWER", PowerRegex(), skipStorageSegments: false);

        return components;
    }

    private static void AddFirstMatchingSegment(
        ICollection<CatalogLaptopComponent> components,
        IEnumerable<string> segments,
        string categoryCode,
        Regex pattern)
    {
        var segment = segments.FirstOrDefault(pattern.IsMatch);
        if (segment is not null)
        {
            components.Add(new CatalogLaptopComponent(categoryCode, segment));
        }
    }

    private static void AddFirstMatchingValue(
        ICollection<CatalogLaptopComponent> components,
        IEnumerable<string> segments,
        string categoryCode,
        Regex pattern,
        bool skipStorageSegments)
    {
        foreach (var segment in segments)
        {
            if (skipStorageSegments && StorageRegex().IsMatch(segment))
            {
                continue;
            }

            var match = pattern.Match(segment);
            if (match.Success)
            {
                components.Add(new CatalogLaptopComponent(categoryCode, match.Value.Trim()));
                return;
            }
        }
    }

    private static IReadOnlyList<string> SplitTopLevelSegments(string value)
    {
        var segments = new List<string>();
        var current = new StringBuilder();
        var parenthesisDepth = 0;

        foreach (var character in value)
        {
            if (character == '(')
            {
                parenthesisDepth++;
            }
            else if (character == ')' && parenthesisDepth > 0)
            {
                parenthesisDepth--;
            }

            if (character == ',' && parenthesisDepth == 0)
            {
                AddSegment(segments, current);
                continue;
            }

            current.Append(character);
        }

        AddSegment(segments, current);
        return segments;
    }

    private static void AddSegment(ICollection<string> segments, StringBuilder current)
    {
        var segment = current.ToString().Trim();
        if (segment.Length > 0)
        {
            segments.Add(segment);
        }

        current.Clear();
    }

    [GeneratedRegex(@"(?:\b(?:Intel\s+)?Core\b|\bAMD\s+Ryzen\b|\bApple\s+(?:M\d|A\d)|\bSnapdragon\b|\bCeleron\b|\bPentium\b|\bAthlon\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProcessorRegex();

    [GeneratedRegex(@"(?:\b(?:NVIDIA\s+)?(?:GeForce\s+)?(?:RTX|GTX)\b|\bAMD\s+Radeon\b|\bIntel\s+Arc\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GraphicsRegex();

    [GeneratedRegex(@"\b(?:Standard|Nano-texture)\s+Display\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DisplayRegex();

    [GeneratedRegex(@"\b\d+(?:[.,]\d+)?\s*GB(?:\s*RAM)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MemoryRegex();

    [GeneratedRegex(@"\b\d+(?:[.,]\d+)?\s*(?:GB|TB)\s*(?:SSD|SDD|HDD)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StorageRegex();

    [GeneratedRegex(@"\b\d{2,3}\s*W\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PowerRegex();
}
