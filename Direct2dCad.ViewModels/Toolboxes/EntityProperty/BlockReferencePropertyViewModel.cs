using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Lang.Strings;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public partial class BlockReferencePropertyViewModel : EntityPropertyViewModel,
    IEntityHeaderPropertySectionViewModel,
    IEntitySettingsPropertySectionViewModel,
    IStrokeAppearancePropertySectionViewModel
{
    private const double Epsilon = 1e-9;
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;

    public BlockReferencePropertyViewModel(CadDocumentViewModel documentViewModel, EntityId entityId)
    {
        _documentViewModel = documentViewModel;
        EntityId = entityId;
        RefreshFromEntity();
    }

    public EntityId EntityId { get; }
    public string EntityIdText => EntityId.Value.ToString();
    public string Title => Localize("BlockReference", "Block reference");
    public string DefinitionLabel => Localize("BlockDefinition", "Block definition");
    public string ReferencesLabel => Localize("References", "References");
    public string EntitiesLabel => Localize("Entities", "Entities");
    public string ScaleXLabel => Localize("ScaleX", "Scale X");
    public string ScaleYLabel => Localize("ScaleY", "Scale Y");
    public IReadOnlyList<BlockDefinitionOption> DefinitionOptions { get; private set; } = [];

    [ObservableProperty]
    public partial BlockDefinitionOption? SelectedDefinition { get; set; }

    [ObservableProperty]
    public partial int DefinitionEntityCount { get; private set; }

    [ObservableProperty]
    public partial int ReferenceCount { get; private set; }

    [ObservableProperty]
    public partial double PositionX { get; set; }

    [ObservableProperty]
    public partial double PositionY { get; set; }

    [ObservableProperty]
    public partial double RotationDegrees { get; set; }

    [ObservableProperty]
    public partial double ScaleX { get; set; } = 1;

    [ObservableProperty]
    public partial double ScaleY { get; set; } = 1;

    [ObservableProperty]
    public partial int ZIndex { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial CadColor StrokeColor { get; set; } = CadColor.Green;

    [ObservableProperty]
    public partial bool UseByLayerColor { get; set; } = true;

    [ObservableProperty]
    public partial double LineWeight { get; set; } = CadLineWeight.Default.Value;

    [ObservableProperty]
    public partial bool UseByLayerLineWeight { get; set; } = true;

    public bool ColorControlsEnabled => IsExplicitColorSource;
    public bool LineWeightControlsEnabled => !UseByLayerLineWeight;

    public void RefreshFromEntity()
    {
        if (!TryGetReference(out var reference))
            return;

        var document = _documentViewModel.CadEditor.Document;
        _isRefreshing = true;
        try
        {
            RefreshLayerOptions(_documentViewModel, reference);
            DefinitionOptions = document.Blocks.Values
                .Where(block => !block.IsSystem)
                .OrderBy(block => block.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(block => new BlockDefinitionOption(block.Id, block.Name))
                .ToArray();
            OnPropertyChanged(nameof(DefinitionOptions));
            SelectedDefinition = DefinitionOptions.FirstOrDefault(option =>
                option.BlockId.Equals(reference.DefinitionBlockId));
            var definition = document.GetBlock(reference.DefinitionBlockId);
            DefinitionEntityCount = definition.EntityIds.Count;
            ReferenceCount = document.GetBlockReferenceCount(definition.Id);
            var displayPosition = ToDisplayPoint(reference.Position);
            PositionX = displayPosition.X;
            PositionY = displayPosition.Y;
            RotationDegrees = reference.RotationRadians * 180.0 / Math.PI;
            ScaleX = reference.ScaleX;
            ScaleY = reference.ScaleY;
            ZIndex = reference.ZIndex;
            IsVisible = reference.IsVisible;
            UseByLayerColor = reference.UseLayerColor;
            StrokeColor = ResolveStrokeColor(document, reference, reference.GraphicStyleId);
            UseByLayerLineWeight = reference.UseLayerLineWeight;
            LineWeight = ResolveEntityLineWeight(document, reference, reference.GraphicStyleId).Value;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    partial void OnSelectedDefinitionChanged(BlockDefinitionOption? value)
    {
        if (_isRefreshing || value is null || !TryGetReference(out var reference) ||
            reference.DefinitionBlockId.Equals(value.BlockId))
        {
            return;
        }
        try
        {
            _documentViewModel.CadEditor.SetBlockReferenceDefinition(EntityId, value.BlockId);
        }
        catch
        {
            RefreshFromEntity();
        }
    }

    partial void OnPositionXChanged(double value) => CommitTransform();
    partial void OnPositionYChanged(double value) => CommitTransform();
    partial void OnRotationDegreesChanged(double value) => CommitTransform();
    partial void OnScaleXChanged(double value) => CommitTransform();
    partial void OnScaleYChanged(double value) => CommitTransform();

    partial void OnZIndexChanged(int value)
    {
        if (!_isRefreshing && TryGetReference(out var reference) && reference.ZIndex != value)
            _documentViewModel.CadEditor.SetEntityZIndex(EntityId, value);
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (!_isRefreshing && TryGetReference(out var reference) && reference.IsVisible != value)
            _documentViewModel.CadEditor.SetEntityVisibility(EntityId, value);
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing ||
            !IsExplicitColorSource ||
            !TryGetReference(out var reference) ||
            ResolveStrokeColor(_documentViewModel.CadEditor.Document, reference, reference.GraphicStyleId) == value)
        {
            return;
        }

        _documentViewModel.CadEditor.SetEntityColor(EntityId, value);
    }

    partial void OnUseByLayerColorChanged(bool value)
    {
        if (!_isRefreshing &&
            TryGetReference(out var reference) &&
            reference.UseLayerColor != value)
        {
            _documentViewModel.CadEditor.SetEntityUseLayerColor(EntityId, value);
        }
    }

    partial void OnLineWeightChanged(double value)
    {
        if (_isRefreshing ||
            UseByLayerLineWeight ||
            value <= 0 ||
            !TryGetReference(out var reference) ||
            Math.Abs(ResolveEntityLineWeight(
                _documentViewModel.CadEditor.Document,
                reference,
                reference.GraphicStyleId).Value - value) <= Epsilon)
        {
            return;
        }

        _documentViewModel.CadEditor.SetEntityLineWeight(EntityId, new CadLineWeight(value));
    }

    partial void OnUseByLayerLineWeightChanged(bool value)
    {
        OnPropertyChanged(nameof(LineWeightControlsEnabled));
        if (_isRefreshing || !TryGetReference(out _))
            return;

        _documentViewModel.CadEditor.SetEntityLineWeight(
            EntityId,
            value
                ? CadLineWeight.ByLayer
                : new CadLineWeight(LineWeight > 0 ? LineWeight : CadLineWeight.Default.Value));
    }

    private void CommitTransform()
    {
        if (_isRefreshing || !TryGetReference(out var reference) ||
            !double.IsFinite(PositionX) || !double.IsFinite(PositionY) ||
            !double.IsFinite(RotationDegrees) ||
            !double.IsFinite(ScaleX) || !double.IsFinite(ScaleY) ||
            Math.Abs(ScaleX) <= Epsilon || Math.Abs(ScaleY) <= Epsilon)
        {
            return;
        }

        var position = ToModelPoint(new CadPointD(PositionX, PositionY));
        var rotation = RotationDegrees * Math.PI / 180.0;
        if (reference.Position.NearEquals(position, Epsilon) &&
            Math.Abs(reference.RotationRadians - rotation) <= Epsilon &&
            Math.Abs(reference.ScaleX - ScaleX) <= Epsilon &&
            Math.Abs(reference.ScaleY - ScaleY) <= Epsilon)
        {
            return;
        }

        _documentViewModel.CadEditor.SetBlockReferenceTransform(
            EntityId,
            position,
            rotation,
            ScaleX,
            ScaleY);
    }

    private bool TryGetReference(out CadBlockReference reference)
    {
        if (_documentViewModel.CadEditor.Document.TryGetEntity(EntityId, out var entity) &&
            entity is CadBlockReference current &&
            !current.IsErased)
        {
            reference = current;
            return true;
        }

        reference = null!;
        return false;
    }

    private static string Localize(string key, string fallback) =>
        Strings.ResourceManager.GetString(key) ?? fallback;
}

public sealed record BlockDefinitionOption(BlockId BlockId, string Name);
