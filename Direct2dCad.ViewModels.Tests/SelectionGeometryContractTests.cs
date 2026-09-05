using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;
using Direct2dCad.HitTesting;

namespace Direct2dCad.ViewModels.Tests;

public sealed class SelectionGeometryContractTests
{
    [Theory]
    [MemberData(nameof(CadEntityTestCases.All), MemberType = typeof(CadEntityTestCases))]
    public void WindowCrossingFilteringAndSelectionHistoryWorkForEveryEntity(TestEntityKind kind)
    {
        var document = CadDocument.Create("Selection");
        var entity = CadEntityTestCases.Add(document, kind);
        if (entity is CadText text)
            text.SetLocalBounds(CadRectD.FromXYWH(0, 0, 20, 10));
        var editor = new CadEditor(document);
        var bounds = new CadHitTestService(document).GetResolvedEntityBounds(entity);
        var point = kind switch
        {
            TestEntityKind.Line => new CadPointD(20, 15),
            TestEntityKind.Circle => new CadPointD(30, 15),
            TestEntityKind.Ellipse => new CadPointD(40, 15),
            TestEntityKind.Arc or TestEntityKind.EllipseArc => new CadPointD(20, 25),
            TestEntityKind.Rectangle or TestEntityKind.Polyline or TestEntityKind.CompositePath => new CadPointD(40, 15),
            TestEntityKind.Polygon => new CadPointD(0, 15),
            TestEntityKind.Spline => CadPointD.Origin,
            _ => bounds.Center
        };
        var touching = CadRectD.FromCenter(point, 2, 2);
        editor.Execute(new BoxSelectCommand(touching, requireContained: false));
        Assert.Equal([entity.Id], editor.Selection.EntityIds);
        editor.Execute(new BoxSelectCommand(touching, requireContained: true));
        Assert.Empty(editor.Selection.EntityIds);
        editor.UndoEditor();
        Assert.Equal([entity.Id], editor.Selection.EntityIds);
        editor.RedoEditor();
        Assert.Empty(editor.Selection.EntityIds);
        editor.Execute(new BoxSelectCommand(bounds.Inflate(10), requireContained: true));
        Assert.Equal([entity.Id], editor.Selection.EntityIds);
        editor.Execute(new BoxSelectCommand(bounds.Inflate(10), selectionFilter: _ => false));
        Assert.Empty(editor.Selection.EntityIds);
        editor.Execute(new BoxSelectCommand(CadRectD.FromXYWH(10000, 10000, 10, 10)));
        Assert.Empty(editor.Selection.EntityIds);
        Assert.False(editor.DocumentCommands.CanUndo);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(150)]
    public void RotatedImageCrossingUsesTheFrameNotItsAxisAlignedBounds(double angle)
    {
        var document = CadDocument.Create("Rotated image");
        var image = document.AddImage(CadRectD.FromXYWH(0, 0, 40, 20), 1, 1, 4, [0, 0, 255, 255]);
        image.SetRotation(angle * Math.PI / 180);
        var editor = new CadEditor(document);
        var corners = image.GetFrameCorners();
        var edge = new CadPointD((corners[0].X + corners[1].X) / 2, (corners[0].Y + corners[1].Y) / 2);
        editor.Execute(new BoxSelectCommand(CadRectD.FromCenter(edge, 1, 1)));
        Assert.Equal([image.Id], editor.Selection.EntityIds);
        editor.Execute(new BoxSelectCommand(CadRectD.FromCenter(image.Bounds.Center, 1, 1)));
        Assert.Equal([image.Id], editor.Selection.EntityIds);
    }
}
