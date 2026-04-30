using System.Text.Json;

namespace DalamudMCP.Plugin.Ui.Localization;

internal sealed class JsonLocalization : IUiLocalization, IDisposable
{
    private Dictionary<string, string> en = new();
    private Dictionary<string, string> zh = new();
    private string currentLanguage = "zh";

    public string CurrentLanguage => currentLanguage;

    public event Action? LanguageChanged;

    public JsonLocalization()
    {
        en = LoadFromResource("DalamudMCP.Plugin.lang.en.json");
        zh = LoadFromResource("DalamudMCP.Plugin.lang.zh.json");
    }

    public string GetString(string key) =>
        currentLanguage switch
        {
            "zh" => zh.TryGetValue(key, out var v) ? v
                  : en.GetValueOrDefault(key, key),
            _ => en.GetValueOrDefault(key, key)
        };

    public string this[string key] => GetString(key);

    public void SetLanguage(string language)
    {
        if (currentLanguage == language) return;
        if (language != "zh" && language != "en") return;
        currentLanguage = language;
        LanguageChanged?.Invoke();
    }

    private static Dictionary<string, string> LoadFromResource(string resourceName)
    {
        var assembly = typeof(JsonLocalization).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource: {resourceName}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? new Dictionary<string, string>();
    }

    public void Dispose()
    {
    }
}

