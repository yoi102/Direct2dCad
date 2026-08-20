using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Services.Rendering;

namespace Direct2dCad.Tests;

public sealed class CadOverlaySceneCoordinatorTests
{
    [Fact]
    public void MovingTransient_InvalidatesPreviousAndCurrentLocations()
    {
        var document = CadDocument.Create("Transient dirty regions");
        var editor = CreateEditor(document);
        var coordinator = new CadOverlaySceneCoordinator();
        var calculator = CreateCalculator(document, editor.Viewport);
        var style = new CadTransientStyle(
            CadColor.FromRgb(64, 255, 128),
            1.0);

        coordinator.UpdateOverlayScenesAndCreateInvalidation(
            calculator,
            editor,
            [new CadTransientLine(new CadPointD(20, 20), new CadPointD(60, 20), style)],
            includeGripHandles: true,
            updateHandleScene: false,
            activeHandleItems: null,
            CadHandleSceneBuildOptions.Default,
            interactionZoom: 1.0);

        var invalidation = coordinator.UpdateOverlayScenesAndCreateInvalidation(
            calculator,
            editor,
            [new CadTransientLine(new CadPointD(700, 700), new CadPointD(740, 700), style)],
            includeGripHandles: true,
            updateHandleScene: false,
            activeHandleItems: null,
            CadHandleSceneBuildOptions.Default,
            interactionZoom: 1.0);

        Assert.False(invalidation.IsFull);
        Assert.Equal(2, invalidation.DirtyScreenRects.Count);
    }

    [Fact]
    public void MovingInfiniteCross_DoesNotPromoteOverlayInvalidationToFullViewport()
    {
        var document = CadDocument.Create("Moving infinite cross dirty regions");
        var editor = CreateEditor(document);
        var coordinator = new CadOverlaySceneCoordinator();
        var calculator = CreateCalculator(document, editor.Viewport);
        var style = new CadTransientStyle(
            CadColor.FromRgb(255, 214, 92),
            1.25);

        coordinator.UpdateOverlayScenesAndCreateInvalidation(
            calculator,
            editor,
            [new CadTransientInfiniteCross(new CadPointD(250, 250), style)],
            includeGripHandles: true,
            updateHandleScene: false,
            activeHandleItems: null,
            CadHandleSceneBuildOptions.Default,
            interactionZoom: 1.0);

        var invalidation = coordinator.UpdateOverlayScenesAndCreateInvalidation(
            calculator,
            editor,
            [new CadTransientInfiniteCross(new CadPointD(750, 750), style)],
            includeGripHandles: true,
            updateHandleScene: false,
            activeHandleItems: null,
            CadHandleSceneBuildOptions.Default,
            interactionZoom: 1.0);

        Assert.False(invalidation.IsFull);
        Assert.Equal(new CadScreenRect(0, 0, 1000, 1000), invalidation.DirtyScreenRect);
        Assert.True(invalidation.DirtyScreenRects.Count > 1);
        Assert.True(invalidation.DirtyScreenRects.Sum(rect => rect.Area) < 1000L * 1000 / 5);
    }

    [Fact]
    public void MovingGripHandle_InvalidatesPreviousAndCurrentLocations()
    {
        var document = CadDocument.Create("Handle dirty regions");
        var editor = CreateEditor(document);
        var coordinator = new CadOverlaySceneCoordinator();
        var calculator = CreateCalculator(document, editor.Viewport);
        var entityId = document.AddLine(
            new CadPointD(20, 20),
            new CadPointD(60, 20)).Id;

        coordinator.UpdateOverlayScenesAndCreateInvalidation(
            calculator,
            editor,
            [],
            includeGripHandles: true,
            updateHandleScene: true,
            activeHandleItems:
            [
                new CadGripHandle(
                    entityId,
                    new CadPointD(40, 20),
                    CadHandleType.Center,
                    CadHandleStyle.Grip)
            ],
            CadHandleSceneBuildOptions.Default,
            interactionZoom: 1.0);

        var invalidation = coordinator.UpdateOverlayScenesAndCreateInvalidation(
            calculator,
            editor,
            [],
            includeGripHandles: true,
            updateHandleScene: true,
            activeHandleItems:
            [
                new CadGripHandle(
                    entityId,
                    new CadPointD(740, 700),
                    CadHandleType.Center,
                    CadHandleStyle.Grip)
            ],
            CadHandleSceneBuildOptions.Default,
            interactionZoom: 1.0);

        Assert.False(invalidation.IsFull);
        Assert.Equal(2, invalidation.DirtyScreenRects.Count);
    }

    [Fact]
    public void ClearingWideSelectionOutline_InvalidatesItsFullStrokeExtent()
    {
        var document = CadDocument.Create("Selection dirty regions");
        var line = document.AddLine(
            new CadPointD(100, 100),
            new CadPointD(200, 100));
        var editor = CreateEditor(document);
        var coordinator = new CadOverlaySceneCoordinator();
        var calculator = CreateCalculator(document, editor.Viewport);
        var selectionStyle = CadHandleStyle.SelectionOutline with
        {
            StrokeWidth = 80.0
        };

        coordinator.UpdateOverlayScenesAndCreateInvalidation(
            calculator,
            editor,
            [],
            includeGripHandles: true,
            updateHandleScene: true,
            activeHandleItems:
            [
                new CadSelectionEntityReference(
                    line.Id,
                    line.Bounds,
                    CadVectorD.Zero,
                    selectionStyle)
            ],
            CadHandleSceneBuildOptions.Default,
            interactionZoom: 1.0);

        var invalidation = coordinator.UpdateOverlayScenesAndCreateInvalidation(
            calculator,
            editor,
            [],
            includeGripHandles: true,
            updateHandleScene: true,
            activeHandleItems: [],
            CadHandleSceneBuildOptions.Default,
            interactionZoom: 1.0);

        Assert.False(invalidation.IsFull);
        Assert.True(invalidation.DirtyScreenRect.Height >= 400);
    }

    [Fact]
    public void LargeMixedSelection_UsesWidestOutlineForInvalidation()
    {
        var document = CadDocument.Create("Large selection dirty regions");
        var first = document.AddLine(
            new CadPointD(100, 100),
            new CadPointD(200, 100));
        var second = document.AddLine(
            new CadPointD(300, 100),
            new CadPointD(400, 100));
        var editor = CreateEditor(document);
        var calculator = CreateCalculator(document, editor.Viewport);
        var scene = new CadHandleScene();
        var items = new List<CadHandleItem>
        {
            new CadSelectionEntityReference(
                first.Id,
                first.Bounds,
                CadVectorD.Zero,
                CadHandleStyle.SelectionOutline),
            new CadSelectionEntityReference(
                second.Id,
                second.Bounds,
                CadVectorD.Zero,
                CadHandleStyle.SelectionOutline with { StrokeWidth = 80.0 })
        };
        for (var index = 0; index < 511; index++)
        {
            items.Add(new CadGripHandle(
                first.Id,
                new CadPointD(150, 100),
                CadHandleType.Center,
                CadHandleStyle.Grip));
        }
        scene.Replace(items);

        var invalidation = calculator.CreateHandleSceneInvalidation(
            scene,
            includeGripHandles: false);

        Assert.False(invalidation.IsFull);
        Assert.True(invalidation.DirtyScreenRect.Height >= 400);
    }

    [Fact]
    public void BlockMovePreview_WithLocalBounds_UsesDefinitionStrokeExtent()
    {
        var document = CadDocument.Create("Block transient dirty regions");
        var child = document.AddLine(
            new CadPointD(0, 0),
            new CadPointD(20, 0));
        var definitionId = document.CreateBlockDefinition(
            "Wide transient block",
            CadPointD.Origin);
        document.MoveEntityToBlock(child.Id, definitionId);
        var reference = document.AddBlockReference(
            definitionId,
            new CadPointD(400, 400),
            scaleX: 10,
            scaleY: 10);
        var editor = CreateEditor(document);
        var calculator = new CadRenderInvalidationCalculator(
            document,
            editor.Viewport,
            1000,
            1000,
            entity => new CadTransientStyle(
                CadColor.FromRgb(255, 255, 255),
                entity.Id.Equals(child.Id) ? 40.0 : 1.0));
        var style = new CadTransientStyle(
            CadColor.FromRgb(64, 255, 128),
            1.0);
        var preview = new CadTransientBlockReference(
            reference.DefinitionBlockId,
            reference.Position,
            reference.RotationRadians,
            reference.ScaleX,
            reference.ScaleY,
            reference.LayerId,
            reference.ColorSource,
            reference.GraphicStyleId,
            style);
        var scene = new CadTransientScene();
        scene.Replace(
        [
            new CadTransientGroup(
                [preview],
                CadMatrixD.CreateTranslation(new CadVectorD(100, 0)),
                style,
                reference.Bounds)
        ]);

        var invalidation = calculator.CreateTransientSceneInvalidation(scene);

        Assert.False(invalidation.IsFull);
        Assert.True(invalidation.DirtyScreenRect.Height >= 400);
    }

    private static CadEditor CreateEditor(CadDocument document)
    {
        var editor = new CadEditor(document);
        editor.Viewport.SetSize(1000, 1000);
        editor.Viewport.SetView(1.0, new CadPointD(0, 1000));
        return editor;
    }

    private static CadRenderInvalidationCalculator CreateCalculator(
        CadDocument document,
        CadViewport viewport)
    {
        return new CadRenderInvalidationCalculator(
            document,
            viewport,
            1000,
            1000,
            _ => new CadTransientStyle(
                CadColor.FromRgb(255, 255, 255),
                1.0));
    }
}
