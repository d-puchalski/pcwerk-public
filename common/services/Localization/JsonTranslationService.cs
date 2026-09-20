using System.Text.Json;

namespace Services.Localization;

public sealed class JsonTranslationService
{
    private const string DefaultLanguage = "DE";
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _translations;

    public JsonTranslationService(string translationsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(translationsDirectory);

        _translations = Directory
            .EnumerateFiles(translationsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .ToDictionary(
                path => Path.GetFileNameWithoutExtension(path).ToUpperInvariant(),
                LoadTranslations,
                StringComparer.OrdinalIgnoreCase);

        if (!_translations.ContainsKey(DefaultLanguage))
        {
            throw new InvalidOperationException(
                $"The default translation file '{DefaultLanguage.ToLowerInvariant()}.json' is missing.");
        }
    }

    public string Translate(string? language, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var selectedLanguage = NormalizeLanguage(language);
        if (_translations[selectedLanguage].TryGetValue(key, out var translation))
        {
            return translation;
        }

        return _translations[DefaultLanguage].GetValueOrDefault(key, key);
    }

    public string NormalizeLanguage(string? language) =>
        !string.IsNullOrWhiteSpace(language) && _translations.ContainsKey(language)
            ? language.ToUpperInvariant()
            : DefaultLanguage;

    private static IReadOnlyDictionary<string, string> LoadTranslations(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"Translation file '{path}' is empty.");
    }
}
