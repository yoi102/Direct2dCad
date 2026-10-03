using Direct2dCad.Db.Geometry;
namespace Direct2dCad.ViewModels.Services.Platform.Printing;

public enum CadPaperScaling { ActualSize, Fit, Custom }
public sealed record CadPrintPlacement(CadRectD Output, CadRectD Printable, double Scale, bool IsClipped)
{
    public const double DipsPerMillimeter=96/25.4;
    public static CadPrintPlacement Calculate(CadRectD contentMillimeters,CadRectD printableDips,CadPaperScaling mode,double percent=100)
    {
        if(contentMillimeters.IsEmpty || contentMillimeters.Width<=0 || contentMillimeters.Height<=0 || printableDips.IsEmpty ||
            printableDips.Width<=0 || printableDips.Height<=0 || !Enum.IsDefined(mode) || !double.IsFinite(percent) || percent<=0 || percent>10000)
            throw new ArgumentException("Invalid print dimensions or scaling.");
        var scale=mode==CadPaperScaling.Fit ? Math.Min(printableDips.Width/contentMillimeters.Width,printableDips.Height/contentMillimeters.Height) : DipsPerMillimeter*(mode==CadPaperScaling.Custom?percent/100:1);
        var width=contentMillimeters.Width*scale;var height=contentMillimeters.Height*scale;
        var output=CadRectD.FromXYWH(printableDips.MinX+(printableDips.Width-width)/2,printableDips.MinY+(printableDips.Height-height)/2,width,height);
        return new(output,printableDips,scale,width>printableDips.Width+1e-6 || height>printableDips.Height+1e-6);
    }
}
