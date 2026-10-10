using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Direct2dCad.Avalonia;

internal static partial class NativeSmoke
{
    private static async Task CheckExplicitColorSourceAsync(MainWindow window,Direct2dCad.ViewModels.CadDocumentViewModel vm,Direct2dCad.Db.EntityId entityId,NativeSmokeReport report,Action<bool,string> assert,string output)
    {
        vm.SelectEntities([entityId]);
        var model=(Direct2dCad.ViewModels.Toolboxes.EntityProperty.CirclePropertyViewModel)window.Model.EntityProperties.Entity!;
        window.UpdateLayout();
        var propertyView=window.GetVisualDescendants().OfType<Views.Generated.CirclePropertyView>().Single(control=>ReferenceEquals(control.DataContext,model));
        var appearance=propertyView.GetVisualDescendants().OfType<Expander>().Single(control=>Equals(control.Header,Direct2dCad.Lang.CadUiText.Get("Appearance")));
        var choice=appearance.GetVisualDescendants().OfType<ComboBox>().Single(control=>control.ItemsSource is IEnumerable<Direct2dCad.ViewModels.Toolboxes.EntityProperty.EntityColorSourceOption>);
        var color=appearance.GetVisualDescendants().OfType<ColorPicker>().Single();
        assert(model.SelectedColorSourceOption?.Value==Direct2dCad.Db.Cad.CadColorSource.ByLayer && !color.IsEffectivelyEnabled,"Color source regression must start with ByLayer and disabled explicit color editing.");
        var point=choice.TranslatePoint(new Point(choice.Bounds.Width-10,choice.Bounds.Height/2),window)!.Value;
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window,point,MouseButton.Left);
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window,point,MouseButton.Left);
        await Task.Delay(120);window.UpdateLayout();
        var row=window.GetVisualDescendants().OfType<ComboBoxItem>().Single(control=>control.IsEffectivelyVisible && control.Content is Direct2dCad.ViewModels.Toolboxes.EntityProperty.EntityColorSourceOption {Value:Direct2dCad.Db.Cad.CadColorSource.Explicit});
        point=row.TranslatePoint(new Point(row.Bounds.Width/2,row.Bounds.Height/2),window)!.Value;
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window,point,MouseButton.Left);
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window,point,MouseButton.Left);
        await Task.Delay(120);window.UpdateLayout();
        assert(vm.CadEditor.Document.GetEntity(entityId).ColorSource==Direct2dCad.Db.Cad.CadColorSource.Explicit && model.IsExplicitColorSource && color.IsEffectivelyEnabled && choice.SelectedItem is Direct2dCad.ViewModels.Toolboxes.EntityProperty.EntityColorSourceOption {Value:Direct2dCad.Db.Cad.CadColorSource.Explicit},$"Clicking Explicit did not retain the color source: entity={vm.CadEditor.Document.GetEntity(entityId).ColorSource}, model={model.SelectedColorSourceOption?.Value}, choice={choice.SelectedItem}, enabled={color.IsEffectivelyEnabled}.");
        var originalColor=model.StrokeColor;
        color.Color=Colors.Orange;
        assert(model.StrokeColor==new Direct2dCad.Db.Cad.CadColor(255,255,165,0),"Enabled explicit color editor did not write the selected color through the compiled binding.");
        vm.Undo();
        assert(model.StrokeColor==originalColor && model.IsExplicitColorSource,"Undoing explicit color did not restore its previous value/source.");
        using(var frame=global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window))frame?.Save(Path.Combine(Path.GetDirectoryName(output)!,"explicit-color-source.png"));
        vm.Undo();
        assert(vm.CadEditor.Document.GetEntity(entityId).ColorSource==Direct2dCad.Db.Cad.CadColorSource.ByLayer && !model.IsExplicitColorSource && !color.IsEffectivelyEnabled,"Undoing Explicit did not return the field and editor to ByLayer.");
        choice.Focus();
        void Key(Key key,RawInputModifiers modifiers=RawInputModifiers.None)
        {
            global::Avalonia.Headless.HeadlessWindowExtensions.KeyPress(window,key,modifiers,PhysicalKey.None,null);
            global::Avalonia.Headless.HeadlessWindowExtensions.KeyRelease(window,key,modifiers,PhysicalKey.None,null);
        }
        Key(global::Avalonia.Input.Key.Down,RawInputModifiers.Alt);await Task.Delay(80);
        Key(global::Avalonia.Input.Key.Down);Key(global::Avalonia.Input.Key.Enter);
        assert(vm.CadEditor.Document.GetEntity(entityId).ColorSource==Direct2dCad.Db.Cad.CadColorSource.Explicit && model.IsExplicitColorSource && color.IsEffectivelyEnabled,"Color source keyboard selection did not retain Explicit.");
        vm.Undo();
        assert(!model.IsExplicitColorSource && !color.IsEffectivelyEnabled,"Keyboard color source change did not undo to ByLayer.");
        report.Passed.Add("Actual docked Color source pointer/keyboard selection retains Explicit, enables compiled-bound color editing and undoes color/source in separate steps");
    }

    private static async Task CheckChoiceControlsAsync(MainWindow owner,NativeSmokeReport report,Action<bool,string> assert,string output)
    {
        var choice=new ComboBox {ItemsSource=new[]{"First","Second"},SelectedIndex=0,MinHeight=28};
        var color=new ColorPicker {Color=Colors.Green};
        var otherColor=new ColorPicker {Color=Colors.Black};
        var propertyScope=new StackPanel {Children={choice,color},Spacing=8};propertyScope.Classes.Add("cadProperties");
        var host=new Window {Width=420,Height=680,Content=new StackPanel {Margin=new Thickness(16),Spacing=12,Children={propertyScope,new TextBlock {Text="Other color controls"},otherColor}}};
        var previousTheme=global::Avalonia.Application.Current!.RequestedThemeVariant;
        try
        {
            host.Show(owner);
            foreach(var theme in new[]{ThemeVariant.Light,ThemeVariant.Dark})
            {
                global::Avalonia.Application.Current.RequestedThemeVariant=theme;
                await Task.Delay(220);host.UpdateLayout();
                foreach(var picker in new[]{color,otherColor})
                {
                    var label=picker.GetVisualDescendants().OfType<TextBlock>().Single(block=>block.Classes.Contains("cadColorLabel"));
                    var swatch=picker.GetVisualDescendants().OfType<Border>().Single(border=>border.Classes.Contains("cadColorSwatch"));
                    var button=picker.GetVisualDescendants().OfType<DropDownButton>().Single();
                    assert(label.Text==picker.Color.ToString() && swatch.Background is SolidColorBrush brush && brush.Color==picker.Color,$"Color preview lost its selected color: label={label.Text}, expected={picker.Color}.");
                    assert(picker.Bounds.Height<=30 && button.Bounds.Width>100 && button.Background is ISolidColorBrush background && background.Color.A==0,$"Color field retained the filled purple button or fixed tiny width: picker={picker.Bounds}, button={button.Bounds}, background={button.Background}.");
                }
                using(var frame=global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(host))frame?.Save(Path.Combine(Path.GetDirectoryName(output)!,theme==ThemeVariant.Light?"choices-light.png":"choices-dark.png"));
            }
            color.Color=Colors.Red;host.UpdateLayout();
            assert(color.GetVisualDescendants().OfType<TextBlock>().Single(block=>block.Classes.Contains("cadColorLabel")).Text==Colors.Red.ToString(),"Changing a color did not update its hexadecimal label.");
            var dropdown=color.GetVisualDescendants().OfType<DropDownButton>().Single();
            var point=dropdown.TranslatePoint(new Point(dropdown.Bounds.Width-12,dropdown.Bounds.Height/2),host)!.Value;
            global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(host,point,MouseButton.Left);
            global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(host,point,MouseButton.Left);
            await Task.Delay(200);host.UpdateLayout();
            assert(dropdown.Flyout?.IsOpen==true && host.GetVisualDescendants().OfType<Control>().Any(control=>control.Name=="ColorSpectrum" && control.IsEffectivelyVisible && control.Bounds.Width>100),"Styled color field did not open the library's visible color spectrum.");
            using(var frame=global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(host))frame?.Save(Path.Combine(Path.GetDirectoryName(output)!,"choices-color-popup.png"));
            void Key(Key key,RawInputModifiers modifiers=RawInputModifiers.None)
            {
                global::Avalonia.Headless.HeadlessWindowExtensions.KeyPress(host,key,modifiers,PhysicalKey.None,null);
                global::Avalonia.Headless.HeadlessWindowExtensions.KeyRelease(host,key,modifiers,PhysicalKey.None,null);
            }
            Key(global::Avalonia.Input.Key.Escape);await Task.Delay(80);
            assert(dropdown.Flyout?.IsOpen==false,"Color picker Escape did not dismiss the library popup.");
            choice.Focus();Key(global::Avalonia.Input.Key.Down,RawInputModifiers.Alt);await Task.Delay(120);host.UpdateLayout();
            var options=host.GetVisualDescendants().OfType<ComboBoxItem>().Where(item=>item.IsEffectivelyVisible && item.Bounds.Height>0).ToArray();
            assert(choice.IsDropDownOpen && options.Length>0 && options.All(item=>item.Bounds.Height<=33),$"Outlined dropdown opened without visible compact choice items: open={choice.IsDropDownOpen}, items={options.Length}.");
            using(var frame=global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(host))frame?.Save(Path.Combine(Path.GetDirectoryName(output)!,"choices-list-popup.png"));
            Key(global::Avalonia.Input.Key.Down);Key(global::Avalonia.Input.Key.Enter);
            assert(!choice.IsDropDownOpen && choice.SelectedIndex==1,"Outlined choice field lost Down/Enter selection behavior.");
            color.IsEnabled=false;
            global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(host,point,MouseButton.Left);
            global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(host,point,MouseButton.Left);
            assert(dropdown.Flyout?.IsOpen!=true,"A disabled color selector opened its popup.");
            report.Passed.Add("Global neutral outlined choice fields and color swatch/hex labels render in light and dark themes; library color/list popups, Escape, keyboard selection and disabled behavior remain functional");
        }
        finally {host.Close();global::Avalonia.Application.Current!.RequestedThemeVariant=previousTheme;}
    }
}
