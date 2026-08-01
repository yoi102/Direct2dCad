using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Handles;

namespace Direct2dCad.Tests;

public sealed class CadHandleSceneBuilderTests
{
    [Fact]
    public void BuildSelectionHandles_CreatesEntitySpecificGripSets()
    {
        var document = CadDocument.Create("Test");
        var line = document.AddLine(new CadPointD(0, 0), new CadPointD(10, 0));
        var circle = document.AddCircle(new CadPointD(20, 20), 5);
        var image = document.AddImage(
            CadRectD.FromLTRB(30, 40, 50, 50),
            pixelWidth: 1,
            pixelHeight: 1,
            stride: 4,
            pixels: new byte[4],
            rotationRadians: Math.PI / 2);
        var builder = new CadHandleSceneBuilder();

        var lineItems = builder.BuildSelectionHandles(document, [line.Id]);
        Assert.Single(lineItems.OfType<CadSelectionEntityReference>());
        Assert.Equal(2, lineItems.OfType<CadGripHandle>().Count(item => item.Type == CadHandleType.Vertex));
        Assert.Single(lineItems.OfType<CadGripHandle>(), item => item.Type == CadHandleType.Center);

        var circleItems = builder.BuildSelectionHandles(document, [circle.Id]);
        Assert.Single(circleItems.OfType<CadSelectionEntityReference>());
        Assert.Single(circleItems.OfType<CadGripHandle>(), item => item.Type == CadHandleType.Center);
        Assert.Equal(4, circleItems.OfType<CadGripHandle>().Count(item => item.Type == CadHandleType.Radius));

        var imageItems = builder.BuildSelectionHandles(document, [image.Id]);
        Assert.Single(imageItems.OfType<CadSelectionEntityReference>());
        Assert.Equal(4, imageItems.OfType<CadGripHandle>().Count(item => item.Type == CadHandleType.BoundsCorner));
        Assert.Equal(4, imageItems.OfType<CadGripHandle>().Count(item => item.Type == CadHandleType.BoundsSide));
        Assert.Single(imageItems.OfType<CadGripHandle>(), item => item.Type == CadHandleType.Center);
        Assert.Single(imageItems.OfType<CadGripHandle>(), item => item.Type == CadHandleType.Rotation);
        Assert.Single(imageItems.OfType<CadRotationHandleGuide>());
    }

    [Fact]
    public void BuildSelectionHandles_FiltersUnavailableEntitiesAndSuppressesLockedGrips()
    {
        var document = CadDocument.Create("Test");
        var lockedLayerId = document.CreateLayer("Locked", CadColor.Green, CadLineWeight.Default);
        var frozenLayerId = document.CreateLayer("Frozen", CadColor.Green, CadLineWeight.Default);
        var hiddenLayerId = document.CreateLayer("Hidden", CadColor.Green, CadLineWeight.Default);
        document.GetLayer(lockedLayerId).SetLocked(true);
        document.GetLayer(frozenLayerId).SetFrozen(true);
        document.GetLayer(hiddenLayerId).SetVisible(false);

        var locked = document.AddLine(CadPointD.Origin, new CadPointD(10, 0), lockedLayerId);
        var frozen = document.AddLine(CadPointD.Origin, new CadPointD(10, 0), frozenLayerId);
        var hiddenLayer = document.AddLine(CadPointD.Origin, new CadPointD(10, 0), hiddenLayerId);
        var hiddenEntity = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        hiddenEntity.SetVisible(false);
        var erased = document.AddLine(CadPointD.Origin, new CadPointD(10, 0));
        erased.Erase();

        var builder = new CadHandleSceneBuilder();
        var items = builder.BuildSelectionHandles(
            document,
            [locked.Id, frozen.Id, hiddenLayer.Id, hiddenEntity.Id, erased.Id]);

        var outline = Assert.Single(items);
        Assert.Equal(locked.Id, Assert.IsType<CadSelectionEntityReference>(outline).EntityId);

        var withLockedGrips = builder.BuildSelectionHandles(
            document,
            [locked.Id],
            new CadHandleSceneBuildOptions(IncludeLockedEntityGripHandles: true));
        Assert.Equal(3, withLockedGrips.OfType<CadGripHandle>().Count());
    }

    [Fact]
    public void BuildSelectionHandles_LargeSelectionUsesOneAggregateMoveGrip()
    {
        var document = CadDocument.Create("Test");
        var first = document.AddLine(new CadPointD(0, 0), new CadPointD(10, 0));
        var second = document.AddCircle(new CadPointD(30, 20), 5);
        var lockedLayerId = document.CreateLayer("Locked", CadColor.Green, CadLineWeight.Default);
        document.GetLayer(lockedLayerId).SetLocked(true);
        var locked = document.AddCircle(new CadPointD(1000, 1000), 100, lockedLayerId);
        var builder = new CadHandleSceneBuilder();
        var options = CadHandleSceneBuildOptions.Default with
        {
            MaximumIndividualGripEntityCount = 1
        };

        var items = builder.BuildSelectionHandles(document, [first.Id, second.Id, locked.Id], options);

        Assert.Equal(3, items.OfType<CadSelectionEntityReference>().Count());
        var grip = Assert.Single(items.OfType<CadGripHandle>());
        Assert.Equal(CadHandleType.Center, grip.Type);
        Assert.Equal(first.Bounds.Union(second.Bounds).Center, grip.Position);
    }

    [Fact]
    public void BuildSelectionHandles_ReusesUnchangedSelectionReference()
    {
        var document = CadDocument.Create("Test");
        var line = document.AddLine(new CadPointD(0, 0), new CadPointD(10, 0));
        var builder = new CadHandleSceneBuilder();
        var buffer = new CadHandleSceneBuildBuffer();
        var scene = new CadHandleScene();

        scene.Replace(builder.BuildSelectionHandles(document, [line.Id], buffer));
        var existing = Assert.Single(scene.SelectionReferences);

        var rebuilt = builder.BuildSelectionHandles(document, [line.Id], buffer, scene);

        Assert.Same(existing, Assert.Single(rebuilt.OfType<CadSelectionEntityReference>()));
    }

    [Fact]
    public void BuildSelectionHandles_OrdersSelectionReferencesLikeTheScene()
    {
        var document = CadDocument.Create("Test");
        var lowLayerId = document.CreateLayer("Low", CadColor.Green, CadLineWeight.Default);
        var highLayerId = document.CreateLayer("High", CadColor.Green, CadLineWeight.Default);
        document.DocumentSettings.LayerDrawingPriority.SetPriority(lowLayerId, 1);
        document.DocumentSettings.LayerDrawingPriority.SetPriority(highLayerId, 10);

        var low = document.AddLine(CadPointD.Origin, new CadPointD(10, 0), lowLayerId);
        var high = document.AddLine(CadPointD.Origin, new CadPointD(10, 0), highLayerId);
        var builder = new CadHandleSceneBuilder();

        var items = builder.BuildSelectionHandles(
            document,
            [high.Id, low.Id],
            CadHandleSceneBuildOptions.Default with
            {
                IncludeGripHandles = false
            });

        var references = items.OfType<CadSelectionEntityReference>().ToArray();
        Assert.Equal([low.Id, high.Id], references.Select(reference => reference.EntityId));
    }

    [Fact]
    public void HandleHitTester_SelectsClosestGripInsideHitRadius()
    {
        var scene = new CadHandleScene();
        var farther = new CadGripHandle(
            new EntityId(1),
            new CadPointD(10, 10),
            CadHandleType.Vertex,
            CadHandleStyle.Grip);
        var closer = new CadGripHandle(
            new EntityId(2),
            new CadPointD(12, 10),
            CadHandleType.Center,
            CadHandleStyle.Grip);
        scene.Replace([farther, closer]);
        var hitTester = new CadHandleHitTester();

        var hit = hitTester.TryHitGrip(
            scene,
            point => point,
            new CadPointD(12.5, 10),
            out var grip);

        Assert.True(hit);
        Assert.Same(closer, grip);
        Assert.False(hitTester.TryHitGrip(
            scene,
            point => point,
            new CadPointD(100, 100),
            out _));
    }
}
