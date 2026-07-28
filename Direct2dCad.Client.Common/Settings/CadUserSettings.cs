using Direct2dCad.Db.Cad;

namespace Direct2dCad.Client.Common.Settings;

public sealed class CadUserSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public CadGeneralUserSettings General { get; set; } = new();
    public CadRenderingUserSettings Rendering { get; set; } = new();
    public CadInteractionUserSettings Interaction { get; set; } = new();

    public static CadUserSettings CreateDefault() => new();

    public void Normalize()
    {
        Version = CurrentVersion;
        General ??= new CadGeneralUserSettings();
        Rendering ??= new CadRenderingUserSettings();
        Interaction ??= new CadInteractionUserSettings();
        General.Normalize();
        Rendering.Normalize();
        Interaction.Normalize();
    }

    public CadUserSettings Clone()
    {
        var clone = new CadUserSettings();
        clone.CopyFrom(this);
        return clone;
    }

    public void CopyFrom(CadUserSettings source)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.Normalize();

        Version = CurrentVersion;
        General = new CadGeneralUserSettings
        {
            IsDarkTheme = source.General.IsDarkTheme,
            CultureLcid = source.General.CultureLcid,
            PrimaryColor = source.General.PrimaryColor,
            SecondaryColor = source.General.SecondaryColor
        };
        Rendering = new CadRenderingUserSettings
        {
            IsAntialiasingEnabled = source.Rendering.IsAntialiasingEnabled,
            IsTextAntialiasingEnabled = source.Rendering.IsTextAntialiasingEnabled,
            ShowFramesPerSecond = source.Rendering.ShowFramesPerSecond,
            IsZoomSnapshotPreviewEnabled =
                source.Rendering.IsZoomSnapshotPreviewEnabled,
            IsLevelOfDetailEnabled = source.Rendering.IsLevelOfDetailEnabled,
            AllowApproximateTileScaleFallback =
                source.Rendering.AllowApproximateTileScaleFallback,
            IsBackgroundChunkRecordingEnabled =
                source.Rendering.IsBackgroundChunkRecordingEnabled,
            IsMultiDeviceRenderingEnabled =
                source.Rendering.IsMultiDeviceRenderingEnabled,
            MultiDeviceRenderingDeviceCount =
                source.Rendering.MultiDeviceRenderingDeviceCount
        };
        Interaction = new CadInteractionUserSettings
        {
            SelectedEntityStrokeColor = source.Interaction.SelectedEntityStrokeColor,
            SelectedEntityStrokeWidth = source.Interaction.SelectedEntityStrokeWidth,
            GripStrokeColor = source.Interaction.GripStrokeColor,
            GripFillColor = source.Interaction.GripFillColor,
            GripSize = source.Interaction.GripSize,
            GripStrokeWidth = source.Interaction.GripStrokeWidth,
            GripPreviewStrokeColor = source.Interaction.GripPreviewStrokeColor,
            GripPreviewFillColor = source.Interaction.GripPreviewFillColor,
            GripPreviewStrokeWidth = source.Interaction.GripPreviewStrokeWidth,
            SelectionWindowStrokeColor = source.Interaction.SelectionWindowStrokeColor,
            SelectionWindowFillColor = source.Interaction.SelectionWindowFillColor,
            SelectionWindowStrokeWidth = source.Interaction.SelectionWindowStrokeWidth,
            SelectionCrossingStrokeColor = source.Interaction.SelectionCrossingStrokeColor,
            SelectionCrossingFillColor = source.Interaction.SelectionCrossingFillColor,
            SelectionCrossingStrokeWidth = source.Interaction.SelectionCrossingStrokeWidth
        };
        Normalize();
    }
}

public sealed class CadGeneralUserSettings
{
    public bool IsDarkTheme { get; set; } = true;
    public int CultureLcid { get; set; } = 1033;
    public CadColor PrimaryColor { get; set; } = CadColor.FromRgb(103, 58, 183);
    public CadColor SecondaryColor { get; set; } = CadColor.FromRgb(156, 39, 176);

    internal void Normalize()
    {
        if (CultureLcid is not (1033 or 1041 or 2052))
            CultureLcid = 1033;

        PrimaryColor = CadColor.FromRgb(PrimaryColor.R, PrimaryColor.G, PrimaryColor.B);
        SecondaryColor = CadColor.FromRgb(SecondaryColor.R, SecondaryColor.G, SecondaryColor.B);
    }
}

public sealed class CadRenderingUserSettings
{
    public bool IsAntialiasingEnabled { get; set; } = true;
    public bool IsTextAntialiasingEnabled { get; set; } = true;
    public bool ShowFramesPerSecond { get; set; } = true;
    public bool IsZoomSnapshotPreviewEnabled { get; set; } = false;
    public bool IsLevelOfDetailEnabled { get; set; } = true;
    public bool AllowApproximateTileScaleFallback { get; set; } = false;
    public bool IsBackgroundChunkRecordingEnabled { get; set; } = false;
    public bool IsMultiDeviceRenderingEnabled { get; set; } = false;
    public int MultiDeviceRenderingDeviceCount { get; set; } = 2;

    internal void Normalize()
    {
        MultiDeviceRenderingDeviceCount =
            Math.Clamp(MultiDeviceRenderingDeviceCount, 2, 4);
    }
}

public sealed class CadInteractionUserSettings
{
    public CadColor SelectedEntityStrokeColor { get; set; } = CadColor.FromArgb(240, 255, 214, 92);
    public double SelectedEntityStrokeWidth { get; set; } = 2.0;

    public CadColor GripStrokeColor { get; set; } = CadColor.White;
    public CadColor GripFillColor { get; set; } = CadColor.FromArgb(255, 42, 130, 255);
    public double GripSize { get; set; } = 7.0;
    public double GripStrokeWidth { get; set; } = 1.0;

    public CadColor GripPreviewStrokeColor { get; set; } = CadColor.FromArgb(245, 255, 214, 92);
    public CadColor GripPreviewFillColor { get; set; } = CadColor.FromArgb(22, 255, 214, 92);
    public double GripPreviewStrokeWidth { get; set; } = 1.4;

    public CadColor SelectionWindowStrokeColor { get; set; } = CadColor.FromArgb(230, 64, 196, 255);
    public CadColor SelectionWindowFillColor { get; set; } = CadColor.FromArgb(32, 64, 196, 255);
    public double SelectionWindowStrokeWidth { get; set; } = 1.0;

    public CadColor SelectionCrossingStrokeColor { get; set; } = CadColor.FromArgb(230, 92, 220, 128);
    public CadColor SelectionCrossingFillColor { get; set; } = CadColor.FromArgb(36, 92, 220, 128);
    public double SelectionCrossingStrokeWidth { get; set; } = 1.0;

    internal void Normalize()
    {
        SelectedEntityStrokeWidth = GuardPositive(SelectedEntityStrokeWidth, 2.0);
        GripSize = GuardPositive(GripSize, 7.0);
        GripStrokeWidth = GuardPositive(GripStrokeWidth, 1.0);
        GripPreviewStrokeWidth = GuardPositive(GripPreviewStrokeWidth, 1.4);
        SelectionWindowStrokeWidth = GuardPositive(SelectionWindowStrokeWidth, 1.0);
        SelectionCrossingStrokeWidth = GuardPositive(SelectionCrossingStrokeWidth, 1.0);
    }

    private static double GuardPositive(double value, double fallback)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value)
            ? value
            : fallback;
    }
}
