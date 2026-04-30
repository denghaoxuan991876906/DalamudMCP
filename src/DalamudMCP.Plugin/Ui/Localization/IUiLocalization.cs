namespace DalamudMCP.Plugin.Ui.Localization;

public interface IUiLocalization
{
    public string this[string key] { get; }
    public string GetString(string key);
    public string CurrentLanguage { get; }
    public void SetLanguage(string language);
    public event Action? LanguageChanged;
}
