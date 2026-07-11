namespace Direct2dCad.ViewModels.Services.Platform;

public interface ISystemFontCatalog
{
    IReadOnlyList<string> FontFamilies { get; }
}
