using System.Windows.Media;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.wpf.Services.Application;

internal sealed class WpfSystemFontCatalog : ISystemFontCatalog
{
    public WpfSystemFontCatalog()
    {
        FontFamilies = Fonts.SystemFontFamilies
            .Select(fontFamily => fontFamily.Source)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> FontFamilies { get; }
}
