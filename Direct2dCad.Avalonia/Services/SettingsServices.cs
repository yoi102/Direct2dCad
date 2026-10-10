using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using global::Avalonia.Media;
using global::Avalonia.Styling;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.AI.Contracts;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Platform.Notifications;

namespace Direct2dCad.Avalonia.Services;

[JsonSerializable(typeof(CadUserSettings))]
[JsonSerializable(typeof(CadWorkspaceSettings))]
[JsonSerializable(typeof(CadToolboxLayoutSettings))]
[JsonSerializable(typeof(AiAssistantSettings))]
[JsonSerializable(typeof(DockUiState))]
[JsonSerializable(typeof(NativeDockState))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class SettingsJsonContext : JsonSerializerContext;
internal sealed class DockUiState
{
    public string[] AutoHidden { get; set; } = [];
    public double LeftWidth { get; set; } = 280;
    public double RightWidth { get; set; } = 300;
    public double BottomHeight { get; set; } = 220;
    public double LeftTopRatio { get; set; } = .5;
    public double RightTopRatio { get; set; } = .5;
    public double BottomLeftRatio { get; set; } = .5;
    public Dictionary<string, FloatingToolState> Floating { get; set; } = [];
}
internal sealed class FloatingToolState { public double Width { get; set; } = 380; public double Height { get; set; } = 550; }

internal static class SettingsPath
{
    public static string Get(string name) => Path.Combine(
        Environment.GetEnvironmentVariable("DIRECT2DCAD_SETTINGS_DIRECTORY") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Direct2dCad", "Avalonia"), name);
    public static T? Read<T>(string name, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        try { return File.Exists(Get(name)) ? JsonSerializer.Deserialize(File.ReadAllText(Get(name)), type) : default; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return default; }
    }
    public static void Write<T>(string name, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        var path = Get(name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(value, type)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
internal sealed class UserSettingsStore : IUserSettingsStore
{
    public CadUserSettings Load()
    {
        var settings = SettingsPath.Read("user-settings.json", SettingsJsonContext.Default.CadUserSettings);
        if (settings is null && Environment.GetEnvironmentVariable("DIRECT2DCAD_SETTINGS_DIRECTORY") is null && !File.Exists(SettingsPath.Get("user-settings.json")))
        {
            var wpf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Direct2dCad", "user-settings.json");
            try { if (File.Exists(wpf)) settings = JsonSerializer.Deserialize(File.ReadAllText(wpf), SettingsJsonContext.Default.CadUserSettings); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        settings ??= CadUserSettings.CreateDefault(); settings.Normalize(); return settings;
    }
    public void Save(CadUserSettings settings) { settings.Normalize(); SettingsPath.Write("user-settings.json", settings, SettingsJsonContext.Default.CadUserSettings); }
}
internal sealed class WorkspaceSettingsStore : IWorkspaceSettingsStore
{
    private readonly CadWorkspaceSettings _settings = SettingsPath.Read("workspace-settings.json", SettingsJsonContext.Default.CadWorkspaceSettings) ?? new();
    public CadDocumentWorkspaceSettings LoadDocument(string path) => _settings.Documents.TryGetValue(Path.GetFullPath(path), out var settings) ? settings.Clone() : new();
    public void SaveDocument(string path, CadDocumentWorkspaceSettings settings) { _settings.Documents[Path.GetFullPath(path)] = settings.Clone(); _settings.Normalize(); SettingsPath.Write("workspace-settings.json", _settings, SettingsJsonContext.Default.CadWorkspaceSettings); }
}
internal sealed class ToolboxSettingsStore : IToolboxLayoutSettingsStore
{
    private readonly CadToolboxLayoutSettings _settings = SettingsPath.Read("toolbox-settings.json", SettingsJsonContext.Default.CadToolboxLayoutSettings) ?? new();
    public CadToolboxState? Load(string id) => _settings.Toolboxes.TryGetValue(id, out var settings) ? settings.Clone() : null;
    public void Save(IEnumerable<KeyValuePair<string, CadToolboxState>> toolboxes) { _settings.Toolboxes = toolboxes.ToDictionary(p => p.Key, p => p.Value.Clone()); _settings.Normalize(); SettingsPath.Write("toolbox-settings.json", _settings, SettingsJsonContext.Default.CadToolboxLayoutSettings); }
}
internal sealed class AiSettingsStore : IAiAssistantSettingsStore
{
    public AiAssistantSettings Load() { var settings = SettingsPath.Read("ai-assistant-settings.json", SettingsJsonContext.Default.AiAssistantSettings) ?? new(); settings.Normalize(); return settings; }
    public void Save(AiAssistantSettings settings) { settings.Normalize(); SettingsPath.Write("ai-assistant-settings.json", settings, SettingsJsonContext.Default.AiAssistantSettings); }
}
internal sealed class ThemeService : IApplicationThemeService
{
    public bool IsDarkTheme { get; private set; }
    public Direct2dCad.Db.Cad.CadColor PrimaryColor { get; private set; }
    public Direct2dCad.Db.Cad.CadColor SecondaryColor { get; private set; }
    public void ToggleThemeLightDark() => ApplyThemeLightDark(!IsDarkTheme);
    public void ApplyThemeLightDark(bool dark) { IsDarkTheme = dark; global::Avalonia.Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light; }
    public void ApplyThemeColors(Direct2dCad.Db.Cad.CadColor primary, Direct2dCad.Db.Cad.CadColor secondary)
    {
        PrimaryColor = primary; SecondaryColor = secondary;
        if (global::Avalonia.Application.Current!.Styles.OfType<Material.Styles.Themes.CustomMaterialTheme>().FirstOrDefault() is { } material)
        {
            material.PrimaryColor = Color.FromArgb(primary.A, primary.R, primary.G, primary.B);
            material.SecondaryColor = Color.FromArgb(secondary.A, secondary.R, secondary.G, secondary.B);
        }
        if (global::Avalonia.Application.Current!.Styles.OfType<global::Avalonia.Themes.Fluent.FluentTheme>().FirstOrDefault() is { } theme)
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                // The default palettes are resources, not entries in Palettes.
                // Copy their colors before overriding Accent; a blank palette makes every surface black.
                if (!theme.Palettes.TryGetValue(variant, out var palette))
                {
                    Color C(string name) => theme.TryGetResource(name, variant, out var value) && value is Color color ? color : throw new InvalidOperationException("Missing Fluent color: " + name);
                    palette = new global::Avalonia.Themes.Fluent.ColorPaletteResources
                    {
                        AltHigh = C("SystemAltHighColor"), AltLow = C("SystemAltLowColor"), AltMedium = C("SystemAltMediumColor"), AltMediumHigh = C("SystemAltMediumHighColor"), AltMediumLow = C("SystemAltMediumLowColor"),
                        BaseHigh = C("SystemBaseHighColor"), BaseLow = C("SystemBaseLowColor"), BaseMedium = C("SystemBaseMediumColor"), BaseMediumHigh = C("SystemBaseMediumHighColor"), BaseMediumLow = C("SystemBaseMediumLowColor"),
                        ChromeAltLow = C("SystemChromeAltLowColor"), ChromeBlackHigh = C("SystemChromeBlackHighColor"), ChromeBlackLow = C("SystemChromeBlackLowColor"), ChromeBlackMedium = C("SystemChromeBlackMediumColor"), ChromeBlackMediumLow = C("SystemChromeBlackMediumLowColor"),
                        ChromeDisabledHigh = C("SystemChromeDisabledHighColor"), ChromeDisabledLow = C("SystemChromeDisabledLowColor"), ChromeGray = C("SystemChromeGrayColor"), ChromeHigh = C("SystemChromeHighColor"), ChromeLow = C("SystemChromeLowColor"), ChromeMedium = C("SystemChromeMediumColor"), ChromeMediumLow = C("SystemChromeMediumLowColor"), ChromeWhite = C("SystemChromeWhiteColor"),
                        ErrorText = C("SystemErrorTextColor"), ListLow = C("SystemListLowColor"), ListMedium = C("SystemListMediumColor"), RegionColor = C("SystemRegionColor")
                    };
                    theme.Palettes[variant] = palette;
                }
                palette.AltHigh = variant == ThemeVariant.Dark ? Color.Parse("#212121") : Color.Parse("#FAFAFA");
                palette.RegionColor = variant == ThemeVariant.Dark ? Color.Parse("#252525") : Colors.White;
                palette.Accent = Color.FromArgb(primary.A, primary.R, primary.G, primary.B);
                var themed = global::Avalonia.Application.Current.Resources.ThemeDictionaries;
                if (!themed.TryGetValue(variant, out var dictionary) || dictionary is not global::Avalonia.Controls.ResourceDictionary resources)
                    themed[variant] = resources = new global::Avalonia.Controls.ResourceDictionary();
                resources["CadRibbonAccentBrush"] = new SolidColorBrush(ReadableAccent(primary, variant == ThemeVariant.Dark));
                resources["CadTitleBarBrush"] = new SolidColorBrush(Color.FromRgb((byte)(primary.R * .55), (byte)(primary.G * .55), (byte)(primary.B * .55)));
                resources["CadSelectionBrush"] = new SolidColorBrush(Color.FromArgb(variant == ThemeVariant.Dark ? (byte)38 : (byte)24, primary.R, primary.G, primary.B));
                resources["CadDockHeaderBrush"] = new SolidColorBrush(variant == ThemeVariant.Dark ? Color.Parse("#303237") : Color.Parse("#E9E9ED"));
                resources["CadPanelBrush"] = new SolidColorBrush(variant == ThemeVariant.Dark ? Color.Parse("#1F2023") : Color.Parse("#FAFAFA"));
                // Match the WPF Material/MahApps surface rather than Material.Avalonia's black backdrop.
                resources["MaterialBackgroundBrush"] = new SolidColorBrush(variant == ThemeVariant.Dark ? Color.Parse("#212121") : Color.Parse("#FAFAFA"));
            }
        global::Avalonia.Application.Current.Resources["SecondaryAccentBrush"] = new SolidColorBrush(Color.FromArgb(secondary.A, secondary.R, secondary.G, secondary.B));
    }
    public void ApplyTheme(bool dark, Direct2dCad.Db.Cad.CadColor primary, Direct2dCad.Db.Cad.CadColor secondary) { ApplyThemeColors(primary, secondary); ApplyThemeLightDark(dark); }
    private static Color ReadableAccent(Direct2dCad.Db.Cad.CadColor primary, bool dark)
    {
        static double Channel(byte value) { var srgb = value / 255d; return srgb <= .04045 ? srgb / 12.92 : Math.Pow((srgb + .055) / 1.055, 2.4); }
        static double Luminance(Color color) => .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        var background = Luminance(dark ? Color.Parse("#212121") : Color.Parse("#FAFAFA"));
        var target = dark ? 255 : 0;
        for (var blend = dark ? .25 : 0; blend <= 1.001; blend += .05)
        {
            byte Mix(byte channel) => (byte)Math.Round(channel * (1 - blend) + target * blend);
            var color = Color.FromRgb(Mix(primary.R), Mix(primary.G), Mix(primary.B));
            var foreground = Luminance(color);
            if ((Math.Max(foreground, background) + .05) / (Math.Min(foreground, background) + .05) >= 4.5) return color;
        }
        return dark ? Colors.White : Colors.Black;
    }
}
internal sealed class CultureService : IApplicationCultureService
{
    public void ChangeCulture(int lcid) { var culture = CultureInfo.GetCultureInfo(lcid); CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture; CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture; Loc.Refresh(); }
    public void ChangeCulture(string language) => ChangeCulture(CultureInfo.GetCultureInfo(language).LCID);
    public int GetCurrentCultureLCID() => CultureInfo.CurrentUICulture.LCID;
}
internal sealed class FontCatalog : ISystemFontCatalog
{
    public IReadOnlyList<string> FontFamilies { get; } = FontManager.Current.SystemFonts.Select(f => f.Name).Order().ToArray();
}
internal sealed class IconProvider : IToolboxIconProvider
{
    public object Explorer => "▤"; public object Layers => "▱"; public object Blocks => "▦"; public object Terminal => ">_";
    public object Search => "⌕"; public object Filter => "▽"; public object Git => "◈"; public object Problems => "!";
    public object Assistant => "✦"; public object Messages => "≡";
}
internal sealed class SnackbarService(ICadMessageLog log) : ISnackbarService
{
    public event Action<string>? Message;
    public void Enqueue(object content, TimeSpan? durationOverride = null, bool promote = false, bool neverConsiderToBeDuplicate = false, CadMessageLevel level = CadMessageLevel.Information) { var text = content.ToString() ?? ""; log.Add(text, level, "Avalonia"); Message?.Invoke(text); }
    public void EnqueueInAll(object content, TimeSpan? durationOverride = null, bool promote = false, bool neverConsiderToBeDuplicate = false, CadMessageLevel level = CadMessageLevel.Information) => Enqueue(content, durationOverride, promote, neverConsiderToBeDuplicate, level);
    public void Enqueue(object identifier, object content, TimeSpan? durationOverride = null, bool promote = false, bool neverConsiderToBeDuplicate = false, CadMessageLevel level = CadMessageLevel.Information) => Enqueue(content, durationOverride, promote, neverConsiderToBeDuplicate, level);
}

