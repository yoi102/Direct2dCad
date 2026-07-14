using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Lang.Strings;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public partial class BlockReferencePropertyViewModel : EntityPropertyViewModel,
    IEntityHeaderPropertySectionViewModel,
    IEntitySettingsPropertySectionViewModel
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
            ReferenceCount = document.GetBlockReferenceIds(definition.Id).Count;
            PositionX = reference.Position.X;
            PositionY = reference.Position.Y;
            RotationDegrees = reference.RotationRadians * 180.0 / Math.PI;
            ScaleX = reference.ScaleX;
            ScaleY = reference.ScaleY;
            ZIndex = reference.ZIndex;
            IsVisible = reference.IsVisible;
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

    private void CommitTransform()
    {
        if (_isRefreshing || !TryGetReference(out var reference) ||
            !double.IsFinite(PositionX) || !double.IsFinite(PositionY) ||
            !double.IsFinite(RotationDegrees) ||
            !double.IsFinite(ScaleX) || !double.IsFinite(ScaleY) ||
            ScaleX <= Epsilon || ScaleY <= Epsilon)
        {
            return;
        }

        var position = new CadPointD(PositionX, PositionY);
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
