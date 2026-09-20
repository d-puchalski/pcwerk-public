using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace ToppreiseCatalog.Import;

internal static partial class ToppreisePageParser
{
    private const string BaseUrl = "https://www.toppreise.ch";

    public static IReadOnlyList<RankedProduct> ParseRanking(string html, int limit)
    {
        var rows = ProductLinkRegex().Matches(html)
            .Select(match => new BrowserRankingRow(
                match.Groups["url"].Value,
                DecodeText(match.Groups["text"].Value)));
        return ParseRanking(rows, limit);
    }

    public static IReadOnlyList<RankedProduct> ParseRanking(
        IEnumerable<BrowserRankingRow> rows,
        int limit)
    {
        var products = new List<RankedProduct>();
        var seen = new HashSet<long>();
        foreach (var row in rows)
        {
            var rawUrl = WebUtility.HtmlDecode(row.Url ?? string.Empty);
            var urlMatch = ProductUrlRegex().Match(rawUrl);
            if (!urlMatch.Success
                || !long.TryParse(urlMatch.Groups["id"].Value, out var externalId)
                || !seen.Add(externalId))
            {
                continue;
            }

            var name = DecodeText(row.Name ?? string.Empty);
            if (name.Length == 0)
            {
                continue;
            }

            var url = rawUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? rawUrl
                : new Uri(new Uri(BaseUrl), rawUrl).AbsoluteUri;
            products.Add(new RankedProduct(externalId, products.Count + 1, name, CanonicalUrl(url)));
            if (products.Count == Math.Clamp(limit, 1, 100))
            {
                break;
            }
        }

        return products;
    }

    public static ScrapedProduct ParseProduct(
        string categoryCode,
        string? productSubtype,
        string fallbackName,
        string html,
        string bodyText,
        IReadOnlyList<ScrapedOffer> offers)
    {
        var name = DecodeText(MatchValue(ProductNameRegex(), html) ?? fallbackName);
        var manufacturer = ReadLabel(
            bodyText,
            "Manufacturer", "Brand", "Hersteller", "Marke", "Fabricant", "Marque") ?? FirstWord(name);
        var manufacturerPartNumber = ReadLabel(bodyText,
            "MPN", "Manufacturer article number", "Manufacturer part number", "Herstellerartikelnummer", "Numéro d'article du fabricant");
        var ean = NormalizeEan(ReadLabel(bodyText, "EAN", "GTIN"));
        var imageUrl = WebUtility.HtmlDecode(MatchValue(ProductImageRegex(), html) ?? string.Empty);
        if (imageUrl.Length == 0)
        {
            imageUrl = null;
        }

        return new ScrapedProduct(
            name,
            manufacturer,
            manufacturerPartNumber,
            ean,
            imageUrl,
            ParseSpecifications(categoryCode, productSubtype, name, bodyText),
            offers);
    }

    private static ParsedSpecifications ParseSpecifications(
        string categoryCode,
        string? productSubtype,
        string name,
        string text)
    {
        return categoryCode switch
        {
            "CPU" => ParseCpu(name, text),
            "MOTHERBOARD" => ParseMotherboard(name, text),
            "RAM" => ParseRam(name, text),
            "GPU" => ParseGpu(name, text),
            "STORAGE" => ParseStorage(name, text),
            "COOLING" => ParseCooler(productSubtype, name, text),
            "PSU" => ParsePsu(name, text),
            "CASE" => ParseCase(name, text),
            "MONITOR" => ParseMonitor(name, text),
            "MOUSE" => ParseMouse(text),
            "KEYBOARD" => ParseKeyboard(name, text),
            _ => new ParsedSpecifications()
        };
    }

    private static ParsedSpecifications ParseCpu(string name, string text)
    {
        var clocks = ClockRegex().Matches(name).Select(x => ParseDecimal(x.Groups[1].Value)).Where(x => x is not null).ToArray();
        return new ParsedSpecifications
        {
            Socket = NormalizeSocket(ReadLabel(text, "CPU socket", "Processor socket", "Socket", "Prozessorsockel", "Sockel") ?? ReadSocket(name)),
            CpuGeneration = ReadLabel(text, "Processor generation", "CPU generation", "Prozessorgeneration") ?? QuotedValue(name),
            Cores = ReadInteger(ReadLabel(text, "Processor cores", "CPU cores", "Prozessorkerne")) ?? ReadRegexInteger(name, @"\b(\d{1,2})x\b"),
            Threads = ReadInteger(ReadLabel(text, "Threads", "Number of threads", "Processor threads")),
            BaseClockGhz = clocks.FirstOrDefault() ?? ReadClockGhz(ReadLabel(text, "Clock rate", "Base clock")),
            BoostClockGhz = clocks.Skip(1).FirstOrDefault() ?? ReadClockGhz(ReadLabel(text, "Max. turbo frequency", "Boost clock")),
            TdpWatts = ReadWatts(ReadLabel(text, "TDP", "Max. TDP", "Thermal design power")),
            HasIntegratedGraphics = ReadPresence(ReadLabel(text, "Integrated graphics", "Integrated GPU", "Integrierte Grafik"))
        };
    }

    private static ParsedSpecifications ParseMotherboard(string name, string text)
    {
        var formFactors = ReadFormFactors(ReadLabel(text, "Form factor", "Format", "Formfaktor") ?? name);
        var videoOutputBlock = ReadLabelBlock(
            text,
            ["Outputs", "Video outputs", "Display outputs", "Grafikausgänge", "Videoausgänge"],
            ["Expansion slots", "Slots", "Erweiterungssteckplätze", "Steckplätze"]);
        return new ParsedSpecifications
        {
            Socket = NormalizeSocket(ReadLabel(text, "CPU socket", "Processor socket", "Socket", "Prozessorsockel", "Sockel") ?? ReadSocket(name)),
            Chipset = ReadLabel(text, "Chipset", "Chipsatz"),
            FormFactor = formFactors.FirstOrDefault(),
            MemoryType = ReadMemoryType(ReadLabel(text, "Memory type", "RAM type", "Type", "Speichertyp", "Typ") ?? name),
            MemorySlots = ReadInteger(ReadLabel(text, "Memory slots", "RAM slots", "Number of slots", "Speichersteckplätze", "Anzahl Steckplätze")),
            MaximumMemoryGb = ReadCapacityGb(ReadLabel(text, "Maximum memory", "Max. memory", "Maximaler Arbeitsspeicher")),
            M2SlotCount = ReadInteger(ReadLabel(text, "M.2 slots", "M.2 interfaces", "M.2")),
            SataPortCount = ReadInteger(ReadLabel(text, "SATA ports", "SATA interfaces", "SATA 6Gb/s")),
            SupportsEcc = ReadBoolean(ReadLabel(text, "ECC support", "ECC")),
            SupportedCpuGenerations = SplitList(ReadLabel(text, "Supported processor generations", "Supported CPUs")),
            VideoOutputs = ReadVideoOutputs(videoOutputBlock)
        };
    }

    private static ParsedSpecifications ParseRam(string name, string text)
    {
        var moduleMatch = ModuleCapacityRegex().Match(name);
        var capacity = ReadCapacityGb(ReadLabel(text, "Memory capacity", "Total size", "Capacity", "Speicherkapazität")) ?? ReadCapacityGb(name);
        var moduleCount = ReadInteger(ReadLabel(text, "Number of modules", "Module count"))
                          ?? (moduleMatch.Success ? ReadInteger(moduleMatch.Groups[1].Value) : null);
        return new ParsedSpecifications
        {
            MemoryType = ReadMemoryType(ReadLabel(text, "Memory type", "Type", "Speichertyp", "Typ") ?? name),
            ModuleFormFactor = ReadModuleFormFactor(ReadLabel(text, "Memory form factor", "Type", "Bauform", "Typ") ?? name),
            CapacityGb = capacity,
            ModuleCount = moduleCount ?? (name.Contains("Kit", StringComparison.OrdinalIgnoreCase) ? 2 : 1),
            SpeedMtPerSecond = ReadRegexInteger(name, @"DDR[345][\s-]+(\d{3,5})")
                               ?? ReadRegexInteger(ReadLabel(text, "Frequency", "Clock rate"), @"(\d{3,5})\s*MHz"),
            IsEcc = ReadBoolean(ReadLabel(text, "ECC", "Error correction")) ?? ContainsWord(name, "ECC"),
            IsRegistered = ReadBoolean(ReadLabel(text, "Registered", "Buffered")) ?? ContainsWord(name, "RDIMM"),
            HeightMm = ReadMillimetres(ReadLabel(text, "Module height", "Height", "Höhe"))
        };
    }

    private static ParsedSpecifications ParseGpu(string name, string text) => new()
    {
        GpuChipset = ReadLabel(text, "Chipset", "Graphics processor", "GPU", "Grafikprozessor") ?? ReadGpuChipset(name),
        GpuMemoryGb = ReadCapacityGb(ReadLabel(text, "Memory size", "Graphics memory", "Video memory", "Grafikspeicher")) ?? ReadRegexInteger(name, @"(\d{1,3})\s*GB\s+GDDR"),
        GpuMemoryType = ReadMemoryTypeName(ReadLabel(text, "Memory type") ?? name),
        TdpWatts = ReadWatts(ReadLabel(text, "Power consumption", "TDP", "Leistungsaufnahme")),
        RecommendedPsuWatts = ReadWatts(ReadLabel(text, "Recommended power supply", "Empfohlenes Netzteil")),
        LengthMm = ReadMillimetres(ReadLabel(text, "Card length", "Graphics card length", "Länge")),
        GpuHeightMm = ReadMillimetres(ReadLabel(text, "Card height", "Graphics card height", "Höhe")),
        SlotWidth = ReadDecimal(ReadLabel(text, "Slot width", "Required slots", "Slots")),
        PowerConnectors = ReadPowerConnectors(ReadLabel(text, "Power connection", "Power connectors", "Power supply", "Stromanschlüsse"))
    };

    private static ParsedSpecifications ParseStorage(string name, string text)
    {
        var protocolText = ReadLabel(text, "Protocol", "Protokoll");
        var interfaceText = string.Join(' ', new[]
        {
            ReadLabel(text, "Interface", "Schnittstelle"),
            ReadLabel(text, "Serial ATA"),
            ReadLabel(text, "SCSI"),
            ReadLabel(text, "IDE"),
            protocolText,
            name
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var formFactor = ReadLabel(text, "Form factor", "Size", "Formfaktor", "Grösse", "Größe")
                         ?? ReadRegexString(name, @"\b(M\.2\s*(?:\(?22\d{2}\)?)?|2\.5-inch)");
        return new ParsedSpecifications
        {
            StorageType = name.Contains("SSD", StringComparison.OrdinalIgnoreCase) ? "SSD" : "HDD",
            InterfaceType = ReadStorageInterface(interfaceText),
            Protocol = ReadStorageProtocol(protocolText, interfaceText, name),
            StorageFormFactor = formFactor,
            CapacityGb = ReadCapacityGb(ReadLabel(text, "Storage capacity", "Total capacity", "Capacity", "Speicherkapazität", "Gesamtkapazität")) ?? ReadCapacityGb(name),
            M2LengthMm = ReadRegexInteger(formFactor, @"M\.2\s*\(?22(\d{2})\)?")
        };
    }

    private static ParsedSpecifications ParseCooler(string? subtype, string name, string text)
    {
        var coolerType = string.Equals(subtype, "aio", StringComparison.OrdinalIgnoreCase) ? "aio" : "air";
        var socketText = ReadLabel(text, "Compatible sockets", "CPU sockets", "CPU socket", "Kompatible Sockel", "Sockel");
        return new ParsedSpecifications
        {
            CoolerType = coolerType,
            HeightMm = coolerType == "air" ? ReadMillimetres(ReadLabel(text, "Cooler height", "Height", "Höhe")) : null,
            RadiatorSizeMm = coolerType == "aio" ? ReadRadiatorSize(name, text) : null,
            TdpCapacityWatts = ReadWatts(ReadLabel(text, "Cooling capacity", "TDP", "Kühlleistung")),
            SupportedSockets = ReadSockets(socketText ?? text, socketText is not null)
        };
    }

    private static ParsedSpecifications ParsePsu(string name, string text)
    {
        var formFactor = ReadLabel(text, "Power supply form factor", "Form factor", "Netzteilformat") ?? name;
        var connectorText = string.Join(' ', new[]
        {
            ReadLabel(text, "Power connectors", "Connectors", "Anschlüsse"),
            ReadLabel(text, "PCI-Express"),
            ReadLabel(text, "Motherboard")
        }.Where(IsMeaningfulValue));
        return new ParsedSpecifications
        {
            Wattage = ReadWatts(ReadLabel(text, "Power", "Output power", "Leistung")) ?? ReadRegexInteger(name, @"\b(\d{3,4})\s*W\b"),
            FormFactor = ReadPsuFormFactor(formFactor),
            PsuLengthMm = ReadLengthMillimetres(ReadLabel(text, "Length", "Depth", "Länge", "Tiefe")),
            EfficiencyRating = ReadLabel(text, "Efficiency certification", "Efficiency", "80 PLUS", "Effizienz"),
            AtxStandard = ReadLabel(text, "ATX standard", "ATX version", "Specification", "Spezifikation"),
            PowerConnectors = ReadPowerConnectors(connectorText)
        };
    }

    private static ParsedSpecifications ParseCase(string name, string text)
    {
        var currentToppreiseFormFactors = ReadLabelBlock(
            text,
            ["Form factor", "Formfaktor"],
            ["Max. form factor", "Max. Formfaktor"]);
        var motherboardFactors = ReadFormFactors(
            currentToppreiseFormFactors
            ?? ReadLabel(text, "Supported motherboard form factors", "Motherboard form factor", "Mainboard-Formfaktoren")
            ?? name);
        var psuFactors = ReadPsuFormFactors(
            ReadLabel(text, "Supported power supply form factors", "Power supply form factor", "Netzteilformat"));
        return new ParsedSpecifications
        {
            MaximumGpuLengthMm = ReadCaseDimensionMillimetres(ReadLabel(text, "Maximum graphics card length", "Max. graphics card length", "Max. graphic card length", "Maximale Grafikkartenlänge")),
            MaximumGpuHeightMm = ReadCaseDimensionMillimetres(ReadLabel(text, "Maximum graphics card height", "Max. graphics card height")),
            MaximumGpuSlotWidth = ReadDecimal(ReadLabel(text, "Maximum graphics card slot width", "Expansion slots")),
            MaximumCpuCoolerHeightMm = ReadCaseDimensionMillimetres(ReadLabel(text, "Maximum CPU cooler height", "Max. CPU cooler height", "Maximale CPU-Kühlerhöhe")),
            MaximumPsuLengthMm = ReadCaseDimensionMillimetres(ReadLabel(text, "Maximum power supply length", "Max. PSU length", "PSU length support", "Maximale Netzteillänge")),
            SupportedMotherboardFormFactors = motherboardFactors,
            SupportedPsuFormFactors = psuFactors,
            SupportedRadiators = ReadRadiators(text)
        };
    }

    private static ParsedSpecifications ParseMonitor(string name, string text)
    {
        var resolution = ResolutionRegex().Match(ReadLabel(text, "Resolution", "Resolution (native)", "Auflösung") ?? name);
        return new ParsedSpecifications
        {
            ScreenSizeInches = ReadDecimal(ReadLabel(text, "Screen size", "Display diagonal", "Diagonal", "Bildschirmdiagonale") ?? ReadRegexString(name, "([0-9.,]+)\\s*(?:inch|\")")),
            ResolutionWidth = resolution.Success ? ReadInteger(resolution.Groups[1].Value) : null,
            ResolutionHeight = resolution.Success ? ReadInteger(resolution.Groups[2].Value) : null,
            RefreshRateHz = ReadInteger(ReadLabel(text, "Refresh rate", "Max. refresh rate", "Bildwiederholrate")) ?? ReadRegexInteger(name, @"(\d{2,3})\s*Hz"),
            PanelType = ReadLabel(text, "Panel type", "Panel-Technologie")
        };
    }

    private static ParsedSpecifications ParseMouse(string text)
    {
        return new ParsedSpecifications
        {
            MaximumDpi = ReadInteger(ReadLabel(text, "Maximum resolution", "Resolution", "DPI", "Maximale Auflösung")),
            WeightGrams = ReadWeightGrams(ReadLabel(text, "Weight", "Gewicht")),
            Connectivity = JoinValues(
                ReadLabel(text, "Connection", "Connectivity", "Verbindung"),
                ReadLabel(text, "Cable", "Kabel"),
                ReadLabel(text, "Connectors", "Anschlüsse"))
        };
    }

    private static ParsedSpecifications ParseKeyboard(string name, string text) => new()
    {
        KeyboardLayout = ReadLabel(text, "Keyboard layout", "Layout", "Tastaturlayout")
                         ?? ReadRegexString(name, @"\b((?:German|Swiss|US|UK|French|Italian)\s+Layout)\b"),
        KeyboardSize = ReadLabel(text, "Keyboard size", "Form factor", "Tastaturformat"),
        SwitchType = ReadLabel(text, "Switch type", "Key technology", "Schaltertyp"),
        Connectivity = JoinValues(
            ReadLabel(text, "Connection", "Connectivity", "Verbindung"),
            ReadLabel(text, "Cable", "Kabel"),
            ReadLabel(text, "Connectors", "Anschlüsse"))
    };

    public static IReadOnlyList<ScrapedOffer> ParseOffers(IEnumerable<BrowserOfferRow> rows)
    {
        var results = new List<ScrapedOffer>();
        foreach (var row in rows)
        {
            var total = ParsePrice(row.TotalPrice);
            if (total is null)
            {
                continue;
            }

            var shop = row.RetailerName?.Trim() ?? string.Empty;
            var externalKey = row.ExternalKey?.Trim();
            if (shop.Length == 0 || string.IsNullOrWhiteSpace(externalKey))
            {
                continue;
            }

            var productPrice = ParsePrice(row.ProductPrice) ?? total.Value;
            var availabilityText = row.AvailabilityText?.Trim() ?? string.Empty;
            var availability = ParseAvailability(availabilityText);
            var delivery = ParseDeliveryRange(availability, availabilityText);
            results.Add(new ScrapedOffer(
                externalKey,
                shop,
                externalKey,
                productPrice,
                Math.Max(0, total.Value - productPrice),
                total.Value,
                availability,
                delivery.Min,
                delivery.Max));
        }

        return results.DistinctBy(x => x.ExternalKey, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? ReadLabel(string text, params string[] labels)
    {
        var lines = text.Replace('\u00a0', ' ').Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            foreach (var label in labels)
            {
                if (line.TrimEnd(':').Equals(label, StringComparison.OrdinalIgnoreCase) && index + 1 < lines.Length)
                {
                    var valueIndex = index + 1;
                    while (valueIndex + 1 < lines.Length
                           && labels.Any(x => lines[valueIndex].TrimEnd(':').Equals(x, StringComparison.OrdinalIgnoreCase)))
                    {
                        valueIndex++;
                    }
                    return lines[valueIndex].Trim();
                }
                if (line.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase))
                {
                    return line[(label.Length + 1)..].Trim();
                }
            }
        }
        return null;
    }

    private static string? ReadLabelBlock(
        string text,
        IReadOnlyList<string> labels,
        IReadOnlyList<string> endLabels)
    {
        var lines = text.Replace('\u00a0', ' ').Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < lines.Length; index++)
        {
            if (!labels.Any(x => lines[index].TrimEnd(':').Equals(x, StringComparison.OrdinalIgnoreCase))) continue;

            var values = lines[(index + 1)..]
                .TakeWhile(line => !endLabels.Any(x => line.TrimEnd(':').Equals(x, StringComparison.OrdinalIgnoreCase)))
                .Where(IsMeaningfulValue)
                .ToArray();
            return values.Length == 0 ? null : string.Join(' ', values);
        }

        return null;
    }

    private static string? ReadSocket(string value) => ReadRegexString(
        value,
        @"(?:Socket|Sockel)\s*(AM[2-5]|FM[1-2]|SP[3-6]|sTRX4|sTR5|sWRX8|TR[45]|LGA\s*\d{3,4}(?:-\d)?|\d{3,4}(?:-\d)?|A5)");

    private static string? NormalizeSocket(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var intelSocket = Regex.Match(
            value,
            @"(?:Socket|Sockel|LGA)?\s*(\d{3,4}(?:-\d)?)",
            RegexOptions.IgnoreCase);
        if (intelSocket.Success)
        {
            return intelSocket.Groups[1].Value;
        }

        var match = SocketRegex().Match(value);
        if (!match.Success) return null;

        var socket = match.Groups[1].Value.Replace(" ", string.Empty).ToUpperInvariant();
        return socket == "A5" ? "AM5" : socket;
    }
    private static string? ReadMemoryType(string? value) => ReadRegexString(value, @"\b(DDR[345])\b")?.ToUpperInvariant();
    private static string? ReadMemoryTypeName(string? value) => ReadRegexString(value, @"\b(GDDR\dX?)\b")?.ToUpperInvariant();
    private static string? ReadModuleFormFactor(string value) => value.Contains("SO-DIMM", StringComparison.OrdinalIgnoreCase) ? "SO-DIMM" : value.Contains("DIMM", StringComparison.OrdinalIgnoreCase) ? "DIMM" : null;
    private static string? ReadStorageInterface(string value) =>
        value.Contains("SATA", StringComparison.OrdinalIgnoreCase) ? "SATA" :
        value.Contains("SAS", StringComparison.OrdinalIgnoreCase) ? "SAS" :
        value.Contains("PCI", StringComparison.OrdinalIgnoreCase) || value.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ? "PCIe" :
        ContainsWord(value, "IDE") ? "IDE" : null;
    private static string? ReadStorageProtocol(string? protocolText, string interfaceText, string name)
    {
        var value = string.Join(' ', protocolText, interfaceText, name);
        if (value.Contains("NVMe", StringComparison.OrdinalIgnoreCase)) return "NVMe";
        if (value.Contains("SAS", StringComparison.OrdinalIgnoreCase)) return "SAS";
        if (value.Contains("AHCI", StringComparison.OrdinalIgnoreCase)
            || value.Contains("SATA", StringComparison.OrdinalIgnoreCase)) return "AHCI";
        return null;
    }
    private static string? ReadPsuFormFactor(string value) => ReadPsuFormFactors(value).FirstOrDefault();
    private static IReadOnlyList<string> ReadPsuFormFactors(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var result = new List<string>();
        var remaining = value;
        if (remaining.Contains("SFX-L", StringComparison.OrdinalIgnoreCase))
        {
            result.Add("SFX-L");
            remaining = Regex.Replace(remaining, "SFX-L", " ", RegexOptions.IgnoreCase);
        }
        if (ContainsWord(remaining, "SFX")) result.Add("SFX");
        if (ContainsWord(remaining, "ATX")) result.Add("ATX");
        return result;
    }
    private static IReadOnlyList<string> ReadFormFactors(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var result = new List<string>();
        var remaining = value;
        foreach (var (source, normalized) in new[]
                 {
                     ("SSI EEB", "EEB"),
                     ("EEB", "EEB"),
                     ("SSI CEB", "CEB"),
                     ("CEB", "CEB"),
                     ("E-ATX", "E-ATX"),
                     ("Extended ATX", "E-ATX"),
                     ("eATX", "E-ATX"),
                     ("Micro-ATX", "Micro-ATX"),
                     ("Micro ATX", "Micro-ATX"),
                     ("µATX", "Micro-ATX"),
                     ("μATX", "Micro-ATX"),
                     ("uATX", "Micro-ATX"),
                     ("mATX", "Micro-ATX"),
                     ("Mini-ITX", "Mini-ITX"),
                     ("Mini ITX", "Mini-ITX")
                 })
        {
            if (!remaining.Contains(source, StringComparison.OrdinalIgnoreCase)) continue;
            if (!result.Contains(normalized, StringComparer.OrdinalIgnoreCase)) result.Add(normalized);
            remaining = Regex.Replace(remaining, Regex.Escape(source), " ", RegexOptions.IgnoreCase);
        }
        if (ContainsWord(remaining, "ITX") && !result.Contains("Mini-ITX", StringComparer.OrdinalIgnoreCase)) result.Add("Mini-ITX");
        if (ContainsWord(remaining, "ATX") && !result.Contains("ATX", StringComparer.OrdinalIgnoreCase)) result.Add("ATX");
        return result;
    }
    private static IReadOnlyList<string> ReadSockets(string value, bool includeStandaloneIntelSockets = false)
    {
        var result = SocketListRegex().Matches(value)
            .Select(x => NormalizeSocket(x.Value))
            .Where(x => x is not null)
            .Cast<string>()
            .ToList();
        if (includeStandaloneIntelSockets)
        {
            foreach (Match match in Regex.Matches(value, @"(?<![A-Z0-9])(115X|\d{3,4}(?:-\d)?)(?![A-Z0-9])", RegexOptions.IgnoreCase))
            {
                if (match.Value.Equals("115X", StringComparison.OrdinalIgnoreCase))
                {
                    result.AddRange(["1150", "1151", "1155", "1156"]);
                }
                else if (NormalizeSocket(match.Value) is { } socket)
                {
                    result.Add(socket);
                }
            }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static string? ReadGpuChipset(string name) => ReadRegexString(name, @"\b((?:GeForce\s+RTX|Radeon\s+RX|Intel\s+Arc)\s+[A-Z0-9 ]{3,15})\b")?.Trim();
    private static string? QuotedValue(string value) => ReadRegexString(value, "[\"“](.*?)[\"”]");
    private static string FirstWord(string value) => value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? value;
    private static string? NormalizeEan(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var compact = new string(value.Where(char.IsDigit).ToArray());
        if (compact.Length is >= 8 and <= 14) return compact;
        var candidate = Regex.Match(value, @"(?<!\d)(\d{8,14})(?!\d)");
        return candidate.Success ? candidate.Groups[1].Value : null;
    }
    private static bool ContainsWord(string value, string word) => Regex.IsMatch(value, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase);
    private static bool IsMeaningfulValue(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim() != "-";
    private static string? JoinValues(params string?[] values)
    {
        var result = values.Where(IsMeaningfulValue).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return result.Length == 0 ? null : string.Join("; ", result!);
    }
    private static IReadOnlyList<string> SplitList(string? value) => string.IsNullOrWhiteSpace(value) ? [] : value.Split([',', ';', '/', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static IReadOnlyDictionary<string, int> ReadPowerConnectors(string? value)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value)) return result;
        foreach (Match match in PowerConnectorRegex().Matches(value))
        {
            var count = ReadInteger(match.Groups[1].Value) ?? 1;
            var type = match.Groups[2].Value.ToUpperInvariant().Replace(" ", string.Empty);
            if (!type.Equals("12V-2X6", StringComparison.OrdinalIgnoreCase))
            {
                type = type.Replace("-", string.Empty);
            }
            result[type] = result.GetValueOrDefault(type) + count;
        }
        return result;
    }

    private static IReadOnlyList<ParsedVideoOutput> ReadVideoOutputs(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];

        var outputs = Regex.Matches(
                value,
                @"(?:(?<quantity>\d+)\s*x\s*)?(?<type>DisplayPort|HDMI|DVI(?:-[A-Z])?|VGA)(?:\s*(?<version>\d+(?:[.,]\d+)?))?",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(match => new ParsedVideoOutput(
                NormalizeVideoOutputType(match.Groups["type"].Value),
                match.Groups["version"].Value.Replace(',', '.'),
                int.TryParse(match.Groups["quantity"].Value, out var quantity) ? quantity : 1))
            .GroupBy(x => new { x.OutputType, x.Version })
            .Select(group => new ParsedVideoOutput(group.Key.OutputType, group.Key.Version, group.Sum(x => x.Quantity)))
            .ToArray();

        return outputs;
    }

    private static string NormalizeVideoOutputType(string value) => value.ToUpperInvariant() switch
    {
        "DISPLAYPORT" => "DisplayPort",
        "HDMI" => "HDMI",
        "VGA" => "VGA",
        var dvi when dvi.StartsWith("DVI", StringComparison.Ordinal) => dvi,
        _ => value.Trim()
    };

    private static IReadOnlyList<(string Location, int SizeMm)> ReadRadiators(string text)
    {
        var result = new List<(string Location, int SizeMm)>();
        foreach (Match match in RadiatorRegex().Matches(text))
        {
            var location = match.Groups[1].Value.ToLowerInvariant() switch { "vorne" => "front", "oben" => "top", "hinten" => "rear", var x => x };
            if (ReadInteger(match.Groups[2].Value) is { } size) result.Add((location, size));
        }
        return result.Distinct().ToArray();
    }

    private static (int? Min, int? Max) ParseDeliveryRange(string availability, string text) => availability switch
    {
        "in_stock" => (1, 3),
        "one_week" => (1, 5),
        "two_weeks" => (6, 10),
        "four_weeks" => (16, 20),
        _ when DeliveryDaysRegex().Match(text) is { Success: true } match => (ReadInteger(match.Groups[1].Value), ReadInteger(match.Groups[2].Value)),
        _ => (null, null)
    };

    private static string ParseAvailability(string text)
    {
        if (Regex.IsMatch(text, "nicht verfügbar|not available|ausverkauft|out of stock", RegexOptions.IgnoreCase)) return "unavailable";
        if (Regex.IsMatch(text, "eigenem Lager|in stock|ab Lager", RegexOptions.IgnoreCase)) return "in_stock";
        if (Regex.IsMatch(text, "einer Woche|one week", RegexOptions.IgnoreCase)) return "one_week";
        if (Regex.IsMatch(text, "zwei Wochen|two weeks", RegexOptions.IgnoreCase)) return "two_weeks";
        if (Regex.IsMatch(text, "vier Wochen|four weeks", RegexOptions.IgnoreCase)) return "four_weeks";
        return "unknown";
    }

    private static int? ReadWatts(string? value) => ReadRegexInteger(value, @"(\d{2,4})\s*W");
    private static decimal? ReadClockGhz(string? value) => ReadRegexDecimal(value, @"([0-9]+(?:[.,][0-9]+)?)\s*GHz");
    private static decimal? ReadMillimetres(string? value) => ReadRegexDecimal(value, @"([0-9]+(?:[.,][0-9]+)?)\s*mm");
    private static decimal? ReadLengthMillimetres(string? value)
    {
        if (ReadMillimetres(value) is { } millimetres) return millimetres;
        return ReadRegexDecimal(value, @"([0-9]+(?:[.,][0-9]+)?)\s*cm") is { } centimetres
            ? centimetres * 10
            : null;
    }
    private static decimal? ReadCaseDimensionMillimetres(string? value)
    {
        if (ReadLengthMillimetres(value) is { } measured) return measured;
        return ReadDecimal(value) is { } unitless ? unitless <= 100 ? unitless * 10 : unitless : null;
    }
    private static decimal? ReadWeightGrams(string? value)
    {
        if (ReadDecimal(value) is not { } weight) return null;
        return value!.Contains("kg", StringComparison.OrdinalIgnoreCase) ? weight * 1000 : weight;
    }
    private static int? ReadCapacityGb(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = CapacityRegex().Match(value);
        if (!match.Success || ParseDecimal(match.Groups[1].Value) is not { } amount) return null;
        return (int)Math.Round(match.Groups[2].Value.Equals("TB", StringComparison.OrdinalIgnoreCase) ? amount * 1000 : amount);
    }
    private static int? ReadRadiatorSize(string name, string text)
    {
        if (ReadRegexInteger(name, @"(?<!\d)(120|140|240|280|360|420)(?!\d)") is { } modelSize)
        {
            return modelSize;
        }

        var fanText = ReadLabel(text, "Recommended fan", "Empfohlener Lüfter");
        var fanMatch = Regex.Match(fanText ?? string.Empty, @"(\d+)\s*x\s*(12|14)\s*cm", RegexOptions.IgnoreCase);
        if (!fanMatch.Success
            || ReadInteger(fanMatch.Groups[1].Value) is not { } fanCount
            || ReadInteger(fanMatch.Groups[2].Value) is not { } fanSizeCm)
        {
            return null;
        }

        var radiatorSize = fanCount * fanSizeCm * 10;
        return radiatorSize is 120 or 140 or 240 or 280 or 360 or 420 ? radiatorSize : null;
    }
    private static int? ReadInteger(string? value) => ReadRegexInteger(value, @"(\d+)");
    private static decimal? ReadDecimal(string? value) => ReadRegexDecimal(value, @"([0-9]+(?:[.,][0-9]+)?)");
    private static int? ReadRegexInteger(string? value, string pattern) => int.TryParse(ReadRegexString(value, pattern), out var result) ? result : null;
    private static decimal? ReadRegexDecimal(string? value, string pattern) => ParseDecimal(ReadRegexString(value, pattern));
    private static string? ReadRegexString(string? value, string pattern)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = Regex.Match(value, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }
    private static bool? ReadBoolean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (Regex.IsMatch(value, @"\b(?:no|none|nein|not supported|nicht|ohne)\b", RegexOptions.IgnoreCase)) return false;
        if (Regex.IsMatch(value, @"\b(?:yes|ja|supported|vorhanden)\b", RegexOptions.IgnoreCase)) return true;
        return null;
    }
    private static bool? ReadPresence(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!IsMeaningfulValue(value)) return false;
        return ReadBoolean(value) ?? true;
    }
    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Replace("'", "").Replace("’", "").Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? result : null;
    }
    private static decimal? ParsePrice(string? value) => ParseDecimal(value);
    private static string CanonicalUrl(string url) => url.Split('?', '#')[0];
    private static string DecodeText(string html) => Regex.Replace(WebUtility.HtmlDecode(TagRegex().Replace(html, " ")), @"\s+", " ").Trim();
    private static string? MatchValue(Regex regex, string value) => regex.Match(value) is { Success: true } match ? match.Groups[1].Value.Trim() : null;

    [GeneratedRegex("<a\\b(?=[^>]*\\bclass=\"[^\"]*\\bPlugin_Product\\b[^\"]*\")[^>]*\\bhref=\"(?<url>(?:https://www\\.toppreise\\.ch)?/(?:price-comparison|preisvergleich|comparison-prix)/[^\"]*-p\\d+[^\"]*)\"[^>]*>(?<text>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ProductLinkRegex();
    [GeneratedRegex("^(?:https://www\\.toppreise\\.ch)?/(?:price-comparison|preisvergleich|comparison-prix)/[^?#]*-p(?<id>\\d+)(?:[?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ProductUrlRegex();
    [GeneratedRegex("<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ProductNameRegex();
    [GeneratedRegex("<meta[^>]+(?:property|name)=\"og:image\"[^>]+content=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex ProductImageRegex();
    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();
    [GeneratedRegex(@"(?:Socket|Sockel)?\s*(AM[2-5]|FM[1-2]|SP[3-6]|sTRX4|sTR5|sWRX8|TR[45]|LGA\s*\d{3,4}(?:-\d)?|\b\d{3,4}(?:-\d)?\b|A5)", RegexOptions.IgnoreCase)]
    private static partial Regex SocketRegex();
    [GeneratedRegex(@"\b(?:AM[2-5]|FM[1-2]|SP[3-6]|sTRX4|sTR5|sWRX8|TR[45]|LGA\s*\d{3,4}(?:-\d)?|Socket\s*\d{3,4}(?:-\d)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SocketListRegex();
    [GeneratedRegex(@"([0-9]+(?:[.,][0-9]+)?)\s*(GB|TB)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CapacityRegex();
    [GeneratedRegex(@"(\d+)\s*[x×]\s*\d+\s*GB", RegexOptions.IgnoreCase)]
    private static partial Regex ModuleCapacityRegex();
    [GeneratedRegex(@"([0-9]+(?:[.,][0-9]+)?)\s*GHz", RegexOptions.IgnoreCase)]
    private static partial Regex ClockRegex();
    [GeneratedRegex(@"(?:(\d+)\s*[x×]\s*)?(12V-2x6|12VHPWR|20\+4[ -]?pin|16[ -]?pin|8[ -]?pin|6\+2[ -]?pin|6[ -]?pin|4\+4[ -]?pin)", RegexOptions.IgnoreCase)]
    private static partial Regex PowerConnectorRegex();
    [GeneratedRegex(@"\b(front|top|rear|vorne|oben|hinten)\b[^\r\n]{0,100}?\b(120|140|240|280|360|420)\s*mm", RegexOptions.IgnoreCase)]
    private static partial Regex RadiatorRegex();
    [GeneratedRegex(@"(\d+)\s*[-–]\s*(\d+)\s*(?:business\s*)?(?:days|Werktage)", RegexOptions.IgnoreCase)]
    private static partial Regex DeliveryDaysRegex();
    [GeneratedRegex(@"(\d{3,5})\s*[x×]\s*(\d{3,5})", RegexOptions.IgnoreCase)]
    private static partial Regex ResolutionRegex();
}
