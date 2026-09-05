using Direct2dCad.CommandLine;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CommandLineContractTests
{
    [Theory]
    [InlineData("L", CadCanvasToolMode.Line)]
    [InlineData("._circle", CadCanvasToolMode.CircleCenterRadius)]
    [InlineData("C D", CadCanvasToolMode.CircleCenterDiameter)]
    [InlineData("C 2P", CadCanvasToolMode.CircleTwoPoint)]
    [InlineData("C 3P", CadCanvasToolMode.CircleThreePoint)]
    [InlineData("A", CadCanvasToolMode.ArcThreePoint)]
    [InlineData("A SCE", CadCanvasToolMode.ArcStartCenterEnd)]
    [InlineData("A SCA", CadCanvasToolMode.ArcStartCenterAngle)]
    [InlineData("A SCL", CadCanvasToolMode.ArcStartCenterLength)]
    [InlineData("A SEA", CadCanvasToolMode.ArcStartEndAngle)]
    [InlineData("A SED", CadCanvasToolMode.ArcStartEndDirection)]
    [InlineData("A SER", CadCanvasToolMode.ArcStartEndRadius)]
    [InlineData("A CSE", CadCanvasToolMode.ArcCenterStartEnd)]
    [InlineData("A CSA", CadCanvasToolMode.ArcCenterStartAngle)]
    [InlineData("A CSL", CadCanvasToolMode.ArcCenterStartLength)]
    [InlineData("A CON", CadCanvasToolMode.ArcContinue)]
    [InlineData("EL", CadCanvasToolMode.EllipseCenter)]
    [InlineData("EL END", CadCanvasToolMode.EllipseAxisEnd)]
    [InlineData("EL ARC", CadCanvasToolMode.EllipseArc)]
    [InlineData("REC", CadCanvasToolMode.Rectangle)]
    [InlineData("PL", CadCanvasToolMode.Polyline)]
    [InlineData("POL", CadCanvasToolMode.Polygon)]
    [InlineData("SPL", CadCanvasToolMode.Spline)]
    [InlineData("T", CadCanvasToolMode.Text)]
    [InlineData("OR", CadCanvasToolMode.SetOrigin)]
    [InlineData("S", CadCanvasToolMode.Select)]
    public void AliasesActivateTheDocumentModeAndEscapeClearsIt(string command, CadCanvasToolMode expected)
    {
        using var context = new CadToolboxTestContext();
        var service = new CadCommandLineService();
        Assert.True(service.Execute(command, context.Document).Success);
        Assert.Equal(expected, context.Document.CadCanvasToolMode);
        Assert.True(service.Execute("ESC", context.Document).Success);
        Assert.Equal(CadCanvasToolMode.Select, context.Document.CadCanvasToolMode);
    }

    [Fact]
    public void DrawingSelectionClipboardAndHistoryCommandsUseTheSameEditor()
    {
        using var context = new CadToolboxTestContext();
        var service = new CadCommandLineService();
        var document = context.Document;
        Assert.False(service.Execute("U", document).Success);
        Assert.False(service.Execute("REDO", document).Success);
        Assert.False(service.Execute("1,2", document).Success);
        Assert.False(service.Execute("COPY", document).Success);
        Assert.False(service.Execute("V", document).Success);
        Run("L");
        Run("0,0");
        Run("@40,30");
        var line = Assert.Single(document.CadEditor.Document.Entities.Values);
        Assert.Equal(40, line.Bounds.Width);
        Assert.Equal(30, line.Bounds.Height);
        Run("U");
        Assert.DoesNotContain(document.CadEditor.Document.Entities.Values, entity => !entity.IsErased);
        Run("REDO");
        Run("ALL");
        Assert.Single(document.CadEditor.Selection.EntityIds);
        Run("CO");
        Run("V");
        Assert.True(document.IsPastePreviewActive);
        Run("ESC");
        Assert.False(document.IsPastePreviewActive);
        Run("ALL");
        Run("E");
        Assert.DoesNotContain(document.CadEditor.Document.Entities.Values, entity => !entity.IsErased);
        Assert.False(service.Execute("E", document).Success);
        Run("U");
        Run("ZE");
        Run("Z EXTENTS");
        Assert.False(service.Execute("Z INVALID", document).Success);
        Assert.Contains("Entities: 1", Run("ST").Message);
        Assert.Contains("GPU cache:", Run("RS").Message);
        Assert.False(service.Execute("DONE", document).Success);
        Assert.True(service.Execute("CLS", document).ClearOutput);

        CadCommandLineResult Run(string command)
        {
            var result = service.Execute(command, document);
            Assert.True(result.Success, result.Message);
            return result;
        }
    }

    [Fact]
    public void HelpCompletionAndUnknownCommandsWorkWithoutDocument()
    {
        var service = new CadCommandLineService();
        Assert.True(service.Execute("?", null).Success);
        Assert.Contains("CIRCLE", service.Execute("HELP C", null).Message);
        Assert.False(service.Execute("HELP missing", null).Success);
        Assert.False(service.Execute(" ", null).Success);
        Assert.False(service.Execute("missing", null).Success);
        foreach (var descriptor in service.Commands.Where(item => item.Name is not ("HELP" or "CLEAR")))
            Assert.False(service.Execute(descriptor.Name, null).Success);
        Assert.Contains("POLYLINE", service.Complete("pl"));
        Assert.Single(service.Complete("", 1));
        Assert.Empty(service.Complete("unregistered"));
    }

    [Fact]
    public void DuplicateAliasRegistrationIsAtomicAndSameHandlerIsIdempotent()
    {
        var registry = new CadCommandLineRegistry();
        var first = new Handler(new("FIRST", "F", "FIRST", "First"));
        registry.Register(first);
        registry.Register(first);
        Assert.Single(registry.Handlers);
        Assert.True(registry.TryResolve("._f", out var resolved));
        Assert.Same(first, resolved);
        Assert.Throws<InvalidOperationException>(() => registry.Register(new Handler(new("SECOND", "F", "SECOND", "Second"))));
        Assert.False(registry.TryResolve("SECOND", out _));
        Assert.Single(registry.Handlers);
    }

    [Theory]
    [InlineData("@3,4", 13, 24)]
    [InlineData("@10<90", 10, 30)]
    [InlineData("3,4", 3, 4)]
    [InlineData("10<180", -10, 0)]
    public void CartesianAndPolarCoordinatesRespectAbsoluteAndRelativeOrigins(string text, double x, double y)
    {
        Assert.True(CadCommandLinePointParser.TryParse(text, new(10, 20), out var point, out var error));
        Assert.Null(error);
        Assert.Equal(x, point.X, 8);
        Assert.Equal(y, point.Y, 8);
    }

    [Theory]
    [InlineData("@1,2")]
    [InlineData("@1<90")]
    [InlineData("1,2,3")]
    [InlineData("nan,2")]
    [InlineData("1,Infinity")]
    [InlineData("@oops<90")]
    [InlineData("1<no")]
    [InlineData("1<2<3")]
    [InlineData("1e308,0")]
    [InlineData("1e308<90")]
    public void InvalidOrOverflowedPointCannotReachTheEditor(string input)
    {
        Assert.False(CadCommandLinePointParser.TryParse(input, null, CadUnit.Inch, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    private sealed record Handler(CadCommandLineDescriptor Descriptor) : ICadCommandLineHandler
    {
        public CadCommandLineResult Execute(CadCommandLineRequest request) => new(true, "Custom command");
    }
}
