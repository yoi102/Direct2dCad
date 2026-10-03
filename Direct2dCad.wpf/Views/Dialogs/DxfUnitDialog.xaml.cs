using System.Windows;
using Direct2dCad.Db.Cad.Settings;
namespace Direct2dCad.wpf.Views.Dialogs;
public partial class DxfUnitDialog
{
    public object[] Units {get;}=Enum.GetValues<CadUnit>().Where(u=>u!=CadUnit.Unitless).Select(u=>(object)new{Unit=u,Symbol=CadUnitConversion.GetSymbol(u)}).ToArray();
    public CadUnit? Unit=>UnitCombo.SelectedValue is CadUnit u?u:null;
    public DxfUnitDialog(){InitializeComponent();DataContext=this;}
    private void Confirm(object sender,RoutedEventArgs e){if(Unit is not null)DialogResult=true;}
}
