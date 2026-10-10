using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using Direct2dCad.Lang;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.Avalonia;

internal static partial class NativeSmoke
{
    private static async Task CheckChineseUiAsync(MainWindow window,IDialogService dialogs,NativeSmokeReport report,Action<bool,string> assert,string output)
    {
        void CheckGlyphs(TextBlock text)
        {
            var runs=text.TextLayout.TextLines.SelectMany(line=>line.TextRuns).OfType<ShapedTextRun>().ToArray();
            assert(runs.Length>0 && runs.All(run=>run.GlyphRun.GlyphInfos.All(glyph=>glyph.GlyphIndex!=0)), $"Chinese UI has missing shaped glyphs: {text.Text}, font={text.FontFamily}.");
        }
        foreach(var key in new[]{"View","Language","Draw","Layers","DeleteSelection","CopySelection","CutSelection","SelectAll","ClearSelection","Fit","DontSave","GridSpacing","Layout","Loading","Overwrite","Topmost","Select","Model","Paper"})
        {
            var label=CadUiText.Get(key);
            assert(label!=key && label.Any(character=>character>='\u4e00' && character<='\u9fff'),$"Chinese resource is missing: {key}.");
            var text=new TextBlock {Text=label};text.Measure(new global::Avalonia.Size(600,60));
            CheckGlyphs(text);
        }
        var file=window.GetLogicalDescendants().OfType<Menu>().First().Items.OfType<MenuItem>().First();
        file.IsSubMenuOpen=true;
        await Task.Delay(120);window.UpdateLayout();
        assert(window.FindControl<TextBlock>("Prompt")!.Text==window.Model.CurrentEditorTabViewModel!.CadDocumentViewModel.CurrentToolNameDisplay,"Changing culture did not refresh the status tool caption.");
        assert(window.GetVisualDescendants().OfType<TextBlock>().Any(text=>text.IsEffectivelyVisible && Equals(text.Text,CadUiText.Get("Model"))),"Model-space tab is still an English literal after changing culture.");
        foreach(var text in window.GetVisualDescendants().OfType<TextBlock>().Where(text=>text.IsEffectivelyVisible && text.Text?.Any(character=>character>='\u4e00' && character<='\u9fff')==true))CheckGlyphs(text);
        using(var frame=global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window))frame?.Save(Path.Combine(Path.GetDirectoryName(output)!,"chinese-menu.png"));
        file.IsSubMenuOpen=false;
        var pending=dialogs.ShowUnsavedDocumentDialogAsync("中文测试图纸");
        await Task.Delay(120);window.UpdateLayout();
        var discard=window.GetVisualDescendants().OfType<Button>().Single(button=>button.IsEffectivelyVisible && Equals(button.Content,"不保存"));
        assert(!window.GetVisualDescendants().OfType<Button>().Any(button=>button.IsEffectivelyVisible && Equals(button.Content,"Discard")),"Chinese unsaved dialog still has an English Discard button.");
        using(var frame=global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window))frame?.Save(Path.Combine(Path.GetDirectoryName(output)!,"chinese-unsaved-dialog.png"));
        discard.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        assert(await pending==UnsavedDocumentDialogResult.Discard,"Localized Don't save changed the unsaved-document result.");
        report.Passed.Add("Chinese menu/button captions shape without missing glyphs; missing UI resources are localized and Don't save returns Discard");
    }
}
