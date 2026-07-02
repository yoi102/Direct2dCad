using AvalonDock.Mvvm.CommunityToolkit;
using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;

namespace Direct2dCad.ViewModels.Toolboxes;

public partial class EntityPropertiesToolboxViewModel : ObservableToolboxBase
{
    [ObservableProperty]
    public partial EntityPropertyViewModel Entity { get; set; }
}
