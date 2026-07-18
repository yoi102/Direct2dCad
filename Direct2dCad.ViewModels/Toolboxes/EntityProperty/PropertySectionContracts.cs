using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public interface IDrawingLayerPropertySectionViewModel
{
    string EntityName { get; set; }
    IReadOnlyList<EntityLayerOption> LayerOptions { get; }
    EntityLayerOption? SelectedLayerOption { get; set; }
}

public interface IEntityHeaderPropertySectionViewModel : IDrawingLayerPropertySectionViewModel
{
    string EntityIdText { get; }
}

public interface IEntitySettingsPropertySectionViewModel
{
    int ZIndex { get; set; }
    bool IsVisible { get; set; }
}

public interface IFillPropertySectionViewModel
{
    IReadOnlyList<FillStyleOption> FillStyleOptions { get; }
    FillStyleOption? SelectedFillStyleOption { get; set; }
    bool FillControlsEnabled { get; }
    CadColor FillColor { get; set; }
    bool FillColorControlsEnabled { get; }
}

public interface IStrokeAppearancePropertySectionViewModel
{
    CadColor StrokeColor { get; set; }
    bool UseByLayerColor { get; set; }
    bool ColorControlsEnabled { get; }
    bool SupportsColorSourceSelection { get; }
    bool IsExplicitColorSource { get; }
    IReadOnlyList<EntityColorSourceOption> ColorSourceOptions { get; }
    EntityColorSourceOption? SelectedColorSourceOption { get; set; }
    double LineWeight { get; set; }
    bool UseByLayerLineWeight { get; set; }
    bool LineWeightControlsEnabled { get; }
}

public sealed record EntityColorSourceOption(CadColorSource Value, string Name);

public interface IStrokeStylePropertySectionViewModel
{
    bool SupportsStartEndCaps { get; }
    bool SupportsLineJoin { get; }
    IReadOnlyList<StrokeCapOption> StrokeCapOptions { get; }
    IReadOnlyList<StrokeDashStyleOption> StrokeDashStyleOptions { get; }
    IReadOnlyList<StrokeLineJoinOption> StrokeLineJoinOptions { get; }
    StrokeCapOption? SelectedStartCapOption { get; set; }
    StrokeCapOption? SelectedEndCapOption { get; set; }
    StrokeCapOption? SelectedDashCapOption { get; set; }
    StrokeDashStyleOption? SelectedDashStyleOption { get; set; }
    StrokeLineJoinOption? SelectedLineJoinOption { get; set; }
}
