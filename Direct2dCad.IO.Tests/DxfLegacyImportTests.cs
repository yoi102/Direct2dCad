using System.Text;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.IO.Dxf;

namespace Direct2dCad.IO.Tests;

/// <summary>Files written by older or localized AutoCAD versions, which the R2004 export never produces.</summary>
public sealed class DxfLegacyImportTests
{
    static DxfLegacyImportTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Theory]
    [InlineData("ANSI_936", 936, "轮廓", "中文 尺寸标注")]
    [InlineData("ANSI_932", 932, "外形", "日本語の寸法")]
    public async Task LegacyCodePageTextAndLayerNamesAreDecoded(string declared, int codePage, string layerName, string text)
    {
        var dxf = Dxf("AC1015", declared,
            Tables(Layer(layerName)),
            $"0\nTEXT\n8\n{layerName}\n10\n1\n20\n2\n30\n0\n40\n2.5\n1\n{text}\n");

        var result = await ImportAsync(Encoding.GetEncoding(codePage).GetBytes(dxf));

        Assert.Equal(text, Assert.Single(result.Document.Entities.Values.OfType<CadText>()).Text);
        Assert.Contains(result.Document.Layers.Values, layer => layer.Name == layerName);
    }

    [Fact]
    public async Task MultibyteEscapesAreDecodedFromTheirAutoCadTable()
    {
        // \M+5 is the GBK table: D6D0 is 中 and CEC4 is 文.
        var dxf = Dxf("AC1009", "ANSI_1252", "", "0\nTEXT\n8\n0\n10\n0\n20\n0\n30\n0\n40\n2.5\n1\n\\M+5D6D0\\M+5CEC4 A\n");

        var result = await ImportAsync(Encoding.ASCII.GetBytes(dxf));

        Assert.Equal("中文 A", Assert.Single(result.Document.Entities.Values.OfType<CadText>()).Text);
    }

    [Fact]
    public async Task R12PolylinesWithVerticesAreImported()
    {
        var entities =
            "0\nPOLYLINE\n8\n0\n66\n1\n10\n0\n20\n0\n30\n0\n70\n1\n" +
            "0\nVERTEX\n8\n0\n10\n0\n20\n0\n30\n0\n" +
            "0\nVERTEX\n8\n0\n10\n20\n20\n0\n30\n0\n" +
            "0\nVERTEX\n8\n0\n10\n20\n20\n10\n30\n0\n" +
            "0\nSEQEND\n8\n0\n" +
            "0\nPOLYLINE\n8\n0\n66\n1\n10\n0\n20\n0\n30\n0\n70\n0\n" +
            "0\nVERTEX\n8\n0\n10\n0\n20\n0\n30\n0\n42\n1\n" +
            "0\nVERTEX\n8\n0\n10\n10\n20\n0\n30\n0\n" +
            "0\nSEQEND\n8\n0\n" +
            "0\nPOLYLINE\n8\n0\n66\n1\n10\n0\n20\n0\n30\n0\n70\n8\n" +
            "0\nVERTEX\n8\n0\n10\n0\n20\n0\n30\n5\n70\n32\n" +
            "0\nVERTEX\n8\n0\n10\n1\n20\n1\n30\n6\n70\n32\n" +
            "0\nSEQEND\n8\n0\n";

        var result = await ImportAsync(Encoding.ASCII.GetBytes(Dxf("AC1009", "ANSI_1252", "", entities)));

        var polyline = Assert.Single(result.Document.Entities.Values.OfType<CadPolyline>());
        Assert.True(polyline.Closed);
        Assert.Equal([new CadPointD(0, 0), new CadPointD(20, 0), new CadPointD(20, 10)], polyline.Points);
        var arc = Assert.Single(result.Document.Entities.Values.OfType<CadCompositePath>());
        Assert.Equal(-5, arc.Bounds.MinY, 6);
        Assert.Equal(1, result.Unsupported["POLYLINE (3D/mesh)"]);
        Assert.DoesNotContain("SEQEND", result.Unsupported.Keys);
        Assert.DoesNotContain("VERTEX", result.Unsupported.Keys);
        Assert.Equal(2, result.Imported);
    }

    [Fact]
    public async Task MirroredArcsCirclesEllipsesAndPolylinesUseTheirObjectCoordinateSystem()
    {
        const string normal = "210\n0\n220\n0\n230\n-1\n";
        var entities =
            $"0\nARC\n8\n0\n10\n10\n20\n0\n30\n0\n40\n5\n50\n0\n51\n90\n{normal}" +
            $"0\nCIRCLE\n8\n0\n10\n30\n20\n4\n30\n0\n40\n2\n{normal}" +
            $"0\nELLIPSE\n8\n0\n10\n0\n20\n0\n30\n0\n11\n10\n21\n0\n31\n0\n40\n0.5\n41\n0\n42\n1.5707963267948966\n{normal}" +
            $"0\nLWPOLYLINE\n8\n0\n90\n2\n70\n0\n10\n0\n20\n0\n42\n1\n10\n10\n20\n0\n{normal}" +
            $"0\nLINE\n8\n0\n10\n1\n20\n1\n30\n0\n11\n2\n21\n2\n31\n0\n{normal}";

        var result = await ImportAsync(Encoding.ASCII.GetBytes(Dxf("AC1018", "ANSI_1252", "", entities)));

        Assert.Empty(result.Unsupported);
        var arc = Assert.Single(result.Document.Entities.Values.OfType<CadArc>());
        Assert.True(arc.Center.NearEquals(new(-10, 0)));
        AssertEndpoints(arc.StartPoint, arc.EndPoint, new(-10, 5), new(-15, 0));
        Assert.True(Assert.Single(result.Document.Entities.Values.OfType<CadCircle>()).Center.NearEquals(new(-30, 4)));
        var ellipse = Assert.Single(result.Document.Entities.Values.OfType<CadEllipseArc>());
        AssertEndpoints(ellipse.StartPoint, ellipse.EndPoint, new(10, 0), new(0, -5));
        var path = Assert.Single(result.Document.Entities.Values.OfType<CadCompositePath>());
        Assert.Equal(-10, path.Bounds.MinX, 6);
        Assert.Equal(0, path.Bounds.MaxX, 6);
        Assert.Equal(-5, path.Bounds.MinY, 6);
        var line = Assert.Single(result.Document.Entities.Values.OfType<CadLine>());
        Assert.True(line.Start.NearEquals(new(1, 1)));
    }

    [Fact]
    public async Task WidePolylinesKeepTheirCentreLineAndReportTheWidth()
    {
        var entities =
            "0\nLWPOLYLINE\n8\n0\n90\n3\n70\n1\n43\n0.5\n10\n0\n20\n0\n10\n20\n20\n0\n10\n20\n20\n10\n" +
            "0\nLWPOLYLINE\n8\n0\n90\n2\n70\n0\n10\n0\n20\n0\n40\n0.2\n41\n0.4\n42\n1\n10\n10\n20\n0\n" +
            "0\nPOLYLINE\n8\n0\n66\n1\n10\n0\n20\n0\n30\n0\n70\n0\n40\n1\n41\n1\n" +
            "0\nVERTEX\n8\n0\n10\n0\n20\n50\n30\n0\n" +
            "0\nVERTEX\n8\n0\n10\n30\n20\n50\n30\n0\n" +
            "0\nSEQEND\n8\n0\n";

        var result = await ImportAsync(Encoding.ASCII.GetBytes(Dxf("AC1015", "ANSI_1252", "", entities)));

        Assert.Equal(3, result.Imported);
        Assert.Equal(3, result.Unsupported["Polyline width omitted"]);
        Assert.Single(result.Unsupported);
        var polylines = result.Document.Entities.Values.OfType<CadPolyline>().OrderBy(p => p.Points.Count).ToArray();
        Assert.Equal([new CadPointD(0, 50), new CadPointD(30, 50)], polylines[0].Points);
        Assert.True(polylines[1].Closed);
        Assert.Equal(3, polylines[1].Points.Count);
        Assert.Equal(-5, Assert.Single(result.Document.Entities.Values.OfType<CadCompositePath>()).Bounds.MinY, 6);
    }

    [Fact]
    public async Task MirroredTextStaysReportedInsteadOfBeingPlacedWrongly()
    {
        var entities = "0\nTEXT\n8\n0\n10\n1\n20\n2\n30\n0\n40\n2.5\n1\nA\n210\n0\n220\n0\n230\n-1\n";

        var result = await ImportAsync(Encoding.ASCII.GetBytes(Dxf("AC1018", "ANSI_1252", "", entities)));

        Assert.Empty(result.Document.Entities);
        Assert.Equal(1, result.Unsupported["3D/extrusion"]);
    }

    [Fact]
    public async Task UndeclaredLegacyBytesFailWithADataErrorRatherThanACrash()
    {
        // Invalid in both UTF-8 and Shift-JIS: the import reports bad data instead of a decoder exception.
        var bytes = Encoding.ASCII.GetBytes(Dxf("AC1015", "ANSI_932", "", "0\nTEXT\n8\n0\n10\n0\n20\n0\n30\n0\n40\n2.5\n1\nXX\n"));
        var at = Array.LastIndexOf(bytes, (byte)'X');
        bytes[at - 1] = 0x81;
        bytes[at] = 0x7F;

        await Assert.ThrowsAsync<InvalidDataException>(() => ImportAsync(bytes));
    }

    private static void AssertEndpoints(CadPointD start, CadPointD end, CadPointD first, CadPointD second) =>
        Assert.True(start.NearEquals(first) && end.NearEquals(second) || start.NearEquals(second) && end.NearEquals(first),
            $"Endpoints {start} and {end} do not match {first} and {second}.");

    private static string Dxf(string version, string codePage, string tables, string entities) =>
        $"0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\n{version}\n9\n$DWGCODEPAGE\n3\n{codePage}\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n" +
        tables +
        $"0\nSECTION\n2\nENTITIES\n{entities}0\nENDSEC\n0\nEOF\n";

    private static string Tables(string records) =>
        $"0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n70\n1\n{records}0\nENDTAB\n0\nENDSEC\n";

    private static string Layer(string name) => $"0\nLAYER\n2\n{name}\n70\n0\n62\n1\n6\nCONTINUOUS\n";

    private static async Task<CadDxfResult> ImportAsync(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dxf");
        await File.WriteAllBytesAsync(path, bytes);
        try { return await new CadDxfStorage().ImportAsync(path); }
        finally { File.Delete(path); }
    }
}
