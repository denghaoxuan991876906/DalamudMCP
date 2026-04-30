namespace DalamudMCP.Plugin.Ui.Localization;

/// <summary>
///     Service contract for UI string localization with runtime language switching.
/// </summary>
public interface IUiLocalization
{
    public string this[string key] { get; }
    public string GetString(string key);
    public string CurrentLanguage { get; }
    public void SetLanguage(string language);
    public event Action? LanguageChanged;
}

