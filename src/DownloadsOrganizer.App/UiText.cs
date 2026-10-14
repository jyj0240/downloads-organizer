using System.Globalization;
using System.Resources;

namespace DownloadsOrganizer.App;

/// <summary>UI text from Strings.resx (English) and Strings.ko.resx (Korean).</summary>
static class UiText
{
    static readonly ResourceManager Strings = new("DownloadsOrganizer.App.Strings", typeof(UiText).Assembly);
    static readonly CultureInfo WindowsLanguage = CultureInfo.CurrentUICulture;

    /// <summary>A missing key shows as ‹Key› so it is easy to spot on screen and in the UI tests.</summary>
    public static string Get(string key) => Strings.GetString(key) ?? $"‹{key}›";

    public static string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>"auto" follows the Windows display language; "en" or "ko" picks one. Applies to windows opened afterwards.</summary>
    public static void Use(string language)
    {
        var culture = language switch
        {
            "ko" => CultureInfo.GetCultureInfo("ko-KR"),
            "en" => CultureInfo.GetCultureInfo("en-US"),
            _ => WindowsLanguage,
        };
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
