using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;

namespace Direct2dCad.ViewModels.Tests;

public sealed class EntityPropertyContractTests
{
    [Theory]
    [MemberData(nameof(CadEntityTestCases.All), MemberType = typeof(CadEntityTestCases))]
    public void NameLayerOrderAndVisibilityUseUndoableCommands(TestEntityKind kind)
    {
        using var context = new CadToolboxTestContext();
        var editor = context.Document.CadEditor;
        var entity = CadEntityTestCases.Add(editor.Document, kind);
        var layer = editor.Document.CreateLayer("Target", CadColor.Blue, CadLineWeight.Default);
        var panel = Select(context, entity);
        var originalName = entity.Name;
        Change(() => panel.EntityName = "Edited", () => Assert.Equal("Edited", entity.Name),
            () => Assert.Equal(originalName, entity.Name));
        Change(() => panel.SelectedLayerOption = panel.LayerOptions.Single(option => option.LayerId == layer),
            () => Assert.Equal(layer, entity.LayerId), () => Assert.Equal(LayerId.Default, entity.LayerId));
        var settings = Assert.IsAssignableFrom<IEntitySettingsPropertySectionViewModel>(panel);
        Change(() => settings.ZIndex = 42, () => Assert.Equal(42, entity.ZIndex), () => Assert.Equal(0, entity.ZIndex));
        Change(() => settings.IsVisible = false, () => Assert.False(entity.IsVisible), () => Assert.True(entity.IsVisible));

        void Change(Action edit, Action after, Action before)
        {
            edit();
            after();
            editor.Undo();
            context.Publish();
            before();
            editor.Redo();
            after();
            editor.Undo();
            context.Publish();
        }
    }

    [Theory]
    [MemberData(nameof(CadEntityTestCases.All), MemberType = typeof(CadEntityTestCases))]
    public void RemovedEntityCannotBeEditedThroughStalePanel(TestEntityKind kind)
    {
        using var context = new CadToolboxTestContext();
        var editor = context.Document.CadEditor;
        var entity = CadEntityTestCases.Add(editor.Document, kind);
        var panel = Select(context, entity);
        editor.DeleteEntities([entity.Id]);
        var version = editor.DocumentChangeVersion;
        panel.EntityName = "Stale edit";
        Assert.IsAssignableFrom<IEntitySettingsPropertySectionViewModel>(panel).ZIndex = 99;
        Assert.Equal(version, editor.DocumentChangeVersion);
        context.Publish();
        Assert.Null(context.Properties.Entity);
        editor.Undo();
        Assert.NotEqual("Stale edit", entity.Name);
        Assert.Equal(0, entity.ZIndex);
    }

    [Theory]
    [MemberData(nameof(CadEntityTestCases.Stroked), MemberType = typeof(CadEntityTestCases))]
    public void ExplicitAppearanceAndLayerInheritanceRemainIndependent(TestEntityKind kind)
    {
        using var context = new CadToolboxTestContext();
        var editor = context.Document.CadEditor;
        var entity = CadEntityTestCases.Add(editor.Document, kind);
        var panel = Assert.IsAssignableFrom<IStrokeAppearancePropertySectionViewModel>(Select(context, entity));
        panel.UseByLayerColor = false;
        panel.StrokeColor = CadColor.Red;
        panel.UseByLayerLineWeight = false;
        panel.LineWeight = 2.5;
        Assert.False(entity.UseLayerColor);
        Assert.False(entity.UseLayerLineWeight);
        Assert.True(panel.ColorControlsEnabled);
        Assert.True(panel.LineWeightControlsEnabled);
        Assert.Equal(2.5, entity.LineWeight!.Value.Value);
        panel.UseByLayerColor = true;
        panel.UseByLayerLineWeight = true;
        Assert.False(panel.ColorControlsEnabled);
        Assert.False(panel.LineWeightControlsEnabled);
        var history = editor.CreateDocumentHistorySnapshot();
        panel.StrokeColor = CadColor.Blue;
        panel.LineWeight = 99;
        Assert.True(editor.DocumentHistoryEquals(history));
        Assert.True(entity.UseLayerColor);
        Assert.True(entity.UseLayerLineWeight);
        editor.Undo();
        editor.Undo();
        context.Publish();
        Assert.False(entity.UseLayerColor);
        Assert.Equal(2.5, entity.LineWeight!.Value.Value);
        Assert.Equal(CadColor.Red, panel.StrokeColor);
    }

    [Theory]
    [MemberData(nameof(CadEntityTestCases.Stroked), MemberType = typeof(CadEntityTestCases))]
    public void InvalidLineWeightRestoresCurrentValueWithoutCommand(TestEntityKind kind)
    {
        using var context = new CadToolboxTestContext();
        var editor = context.Document.CadEditor;
        var entity = CadEntityTestCases.Add(editor.Document, kind);
        var panel = Assert.IsAssignableFrom<IStrokeAppearancePropertySectionViewModel>(Select(context, entity));
        panel.UseByLayerLineWeight = false;
        panel.LineWeight = 2.5;
        var history = editor.CreateDocumentHistorySnapshot();
        foreach (var invalid in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity })
        {
            panel.LineWeight = invalid;
            Assert.Equal(2.5, entity.LineWeight!.Value.Value);
            Assert.Equal(2.5, panel.LineWeight);
            Assert.True(editor.DocumentHistoryEquals(history));
        }
    }

    [Theory]
    [MemberData(nameof(CadEntityTestCases.Filled), MemberType = typeof(CadEntityTestCases))]
    public void EveryBuiltInFillIsUndoableAndOptionsDoNotGrow(TestEntityKind kind)
    {
        using var context = new CadToolboxTestContext();
        var editor = context.Document.CadEditor;
        var entity = CadEntityTestCases.Add(editor.Document, kind);
        if (entity is CadSpline spline)
            editor.SetSplineGeometry(entity.Id, spline.FitPoints, true);
        var panel = Assert.IsAssignableFrom<IFillPropertySectionViewModel>(Select(context, entity));
        Assert.True(panel.FillControlsEnabled);
        var options = panel.FillStyleOptions.ToArray();
        foreach (var option in options.Where(option => option.Kind != FillStyleOptionKind.None))
        {
            panel.SelectedFillStyleOption = option;
            panel.FillColor = CadColor.Blue;
            Assert.NotNull(FillId(entity));
            Assert.True(panel.FillColorControlsEnabled);
            context.Publish();
            Assert.Equal(options.Length, panel.FillStyleOptions.Count);
            Assert.Equal(option.Kind, panel.SelectedFillStyleOption!.Kind);
            Assert.Equal(CadColor.Blue, panel.FillColor);
            var filled = FillId(entity);
            panel.SelectedFillStyleOption = panel.FillStyleOptions.Single(item => item.Kind == FillStyleOptionKind.None);
            Assert.Null(FillId(entity));
            Assert.False(panel.FillColorControlsEnabled);
            editor.Undo();
            context.Publish();
            Assert.Equal(filled, FillId(entity));
            editor.Redo();
            context.Publish();
            Assert.Null(FillId(entity));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosingCurveEnablesFillAndRemovesEndpointCapabilities(bool spline)
    {
        using var context = new CadToolboxTestContext();
        var entity = CadEntityTestCases.Add(context.Document.CadEditor.Document,
            spline ? TestEntityKind.Spline : TestEntityKind.Polyline);
        var panel = Select(context, entity);
        Assert.True(panel.SupportsStartEndCaps);
        var fill = Assert.IsAssignableFrom<IFillPropertySectionViewModel>(panel);
        Assert.False(fill.FillControlsEnabled);
        if (panel is SplinePropertyViewModel splinePanel) splinePanel.IsClosed = true;
        else Assert.IsType<PolylinePropertyViewModel>(panel).IsClosed = true;
        context.Publish();
        Assert.True(fill.FillControlsEnabled);
        Assert.False(panel.SupportsStartEndCaps);
        context.Document.CadEditor.Undo();
        context.Publish();
        Assert.False(fill.FillControlsEnabled);
        Assert.True(panel.SupportsStartEndCaps);
    }

    internal static EntityPropertyViewModel Select(CadToolboxTestContext context, CadEntity entity)
    {
        context.Document.SelectEntities([entity.Id]);
        context.Properties.Attach(context.Document);
        return Assert.IsAssignableFrom<EntityPropertyViewModel>(context.Properties.Entity);
    }

    private static StyleId? FillId(CadEntity entity) => entity switch
    {
        CadCircle circle => circle.FillStyleId,
        CadEllipse ellipse => ellipse.FillStyleId,
        CadRectangle rectangle => rectangle.FillStyleId,
        CadPolyline polyline => polyline.FillStyleId,
        CadSpline spline => spline.FillStyleId,
        _ => throw new ArgumentException("Not fillable", nameof(entity))
    };
}
