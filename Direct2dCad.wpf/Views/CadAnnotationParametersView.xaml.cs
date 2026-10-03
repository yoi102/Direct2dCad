using System.Windows.Controls;
using System.Windows.Input;
using Direct2dCad.ViewModels;
namespace Direct2dCad.wpf.Views;
public partial class CadAnnotationParametersView : UserControl
{
    public CadAnnotationParametersView() => InitializeComponent();
    private void ParameterInput_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is CadDocumentViewModel document) document.BeginDimensionPropertyEdit();
    }
    private void ParameterInput_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is CadDocumentViewModel document) document.EndDimensionPropertyEdit();
    }
}
