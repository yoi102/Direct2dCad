using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Services.Platform.Printing;
namespace Direct2dCad.ViewModels.Services.Tests;
public sealed class PrintScaleTests
{
    [Fact]public void ActualSizeDoesNotFitWhenPaperExceedsPrintableBounds()
    {
        var p=CadPrintPlacement.Calculate(CadRectD.FromXYWH(0,0,297,210),CadRectD.FromXYWH(10,10,1000,700),CadPaperScaling.ActualSize);
        Assert.Equal(96/25.4,p.Scale,9);Assert.True(p.IsClipped);
        Assert.Equal(100*96/25.4,100*p.Scale,8);
    }
    [Fact]public void FitAndCustomHaveIndependentExplicitScales()
    {
        var b=CadRectD.FromXYWH(0,0,420,297);var page=CadRectD.FromXYWH(0,0,700,1000);
        var fit=CadPrintPlacement.Calculate(b,page,CadPaperScaling.Fit);Assert.False(fit.IsClipped);Assert.Equal(700/420d,fit.Scale,9);
        var custom=CadPrintPlacement.Calculate(b,page,CadPaperScaling.Custom,50);Assert.Equal(96/25.4*.5,custom.Scale,9);
        Assert.Throws<ArgumentException>(()=>CadPrintPlacement.Calculate(b,page,CadPaperScaling.Custom,double.NaN));
    }
}
