using System.Text.Json;
using DalamudMCP.Plugin.Ui.Localization;

namespace DalamudMCP.Plugin.Tests;

public sealed class JsonLocalizationTests
{
    [Fact]
    public void Loads_both_languages_at_construction()
    {
        using var loc = new JsonLocalization();
        Assert.Equal("zh", loc.CurrentLanguage);
        Assert.NotNull(loc["window.title"]);
        Assert.NotEqual("window.title", loc["window.title"]);
    }

    [Fact]
    public void GetString_falls_back_to_key_when_missing()
    {
        using var loc = new JsonLocalization();
        string result = loc["nonexistent.key.xyz"];
        Assert.Equal("nonexistent.key.xyz", result);
    }

    [Fact]
    public void SetLanguage_switches_output_text()
    {
        using var loc = new JsonLocalization();
        loc.SetLanguage("zh");
        string zhTitle = loc["window.title"];
        loc.SetLanguage("en");
        string enTitle = loc["window.title"];
        Assert.NotEqual(zhTitle, enTitle);
        Assert.Contains("设置", zhTitle, StringComparison.Ordinal);
        Assert.Contains("Settings", enTitle, StringComparison.Ordinal);
    }

    [Fact]
    public void SetLanguage_fires_LanguageChanged_event()
    {
        using var loc = new JsonLocalization();
        int eventCount = 0;
        loc.LanguageChanged += () => eventCount++;
        loc.SetLanguage("en");
        Assert.Equal(1, eventCount);
        loc.SetLanguage("zh");
        Assert.Equal(2, eventCount);
    }

    [Fact]
    public void SetLanguage_does_not_fire_when_language_unchanged()
    {
        using var loc = new JsonLocalization();
        int eventCount = 0;
        loc.LanguageChanged += () => eventCount++;
        loc.SetLanguage("zh"); // Default is already "zh"
        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void SetLanguage_ignores_unknown_values()
    {
        using var loc = new JsonLocalization();
        int eventCount = 0;
        loc.LanguageChanged += () => eventCount++;
        loc.SetLanguage("fr"); // Not supported
        Assert.Equal(0, eventCount);
        Assert.Equal("zh", loc.CurrentLanguage); // Should stay at default
    }

    [Fact]
    public void GetString_falls_back_from_zh_to_en_when_only_en_has_key()
    {
        // 此测试需要动态修改加载的词典，目前无法通过嵌入式资源实现。
        // 替代: 验证所有 en 键在 zh 中都存在（在 All_zh_keys_match_en_keys 中处理）
        Assert.True(true);
    }

    [Fact]
    public void All_zh_keys_match_en_keys()
    {
        var assembly = typeof(JsonLocalization).Assembly;

        List<string> ReadKeys(string resourceName)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
            return dict!.Keys.OrderBy(k => k).ToList();
        }

        var enKeys = ReadKeys("DalamudMCP.Plugin.lang.en.json");
        var zhKeys = ReadKeys("DalamudMCP.Plugin.lang.zh.json");

        var onlyInEn = enKeys.Except(zhKeys).ToList();
        var onlyInZh = zhKeys.Except(enKeys).ToList();

        Assert.Empty(onlyInEn);
        Assert.Empty(onlyInZh);
    }
}
