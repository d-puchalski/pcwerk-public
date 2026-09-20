using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text.Json;

namespace PcSell.Components.Pages;

public partial class Home
{
    private string _language = "DE";
    private string L(string key) => Translations.Translate(_language, key);

    private string OrganizationSchema => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "Organization",
        ["name"] = "PCWerk",
        ["url"] = "https://pcwerk.ch",
        ["logo"] = "https://pcwerk.ch/favicon.svg",
        ["description"] = L("Seo.OrganizationDescription"),
        ["areaServed"] = new Dictionary<string, string>
        {
            ["@type"] = "Country",
            ["name"] = L("Seo.Switzerland"),
        },
        ["inLanguage"] = _language.ToLowerInvariant(),
    });

    [SupplyParameterFromQuery(Name = "preset")]
    public long? CatalogPresetId { get; set; }

    [SupplyParameterFromQuery(Name = "configurator")]
    public bool? ConfiguratorRequested { get; set; }

    private bool _isConfiguratorOpen;

    [Inject]
    private IJSRuntime Js { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    protected override void OnParametersSet()
    {
        if (ConfiguratorRequested == true || CatalogPresetId is not null)
        {
            _isConfiguratorOpen = true;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        var storedLanguage = await Js.InvokeAsync<string?>("pcLanguage.get");
        var language = Translations.NormalizeLanguage(storedLanguage);
        var shouldOpenConfigurator = await Js.InvokeAsync<bool>("pcBuilder.shouldOpenModal");
        var languageChanged = language != _language;
        if (languageChanged)
        {
            _language = language;
        }

        if (shouldOpenConfigurator)
        {
            _isConfiguratorOpen = true;
        }

        if (languageChanged || shouldOpenConfigurator)
        {
            StateHasChanged();
        }
    }

    private async Task SetLanguage(string language)
    {
        _language = Translations.NormalizeLanguage(language);
        await Js.InvokeVoidAsync("pcLanguage.set", _language);
    }

    private void OpenConfigurator() => _isConfiguratorOpen = true;

    private void OpenConfiguratorWithPreset(Services.Models.CatalogPcPreset preset)
    {
        Navigation.NavigateTo($"/?preset={preset.PresetId}&configurator=true", forceLoad: true);
    }

    private void CloseConfigurator() => _isConfiguratorOpen = false;
}
