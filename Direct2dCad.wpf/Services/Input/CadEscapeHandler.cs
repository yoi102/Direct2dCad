using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;
using Direct2dCad.ViewModels;
using Direct2dCad.wpf.Controls;
using Direct2dCad.wpf.Views.Toolboxes.EntityProperty;

namespace Direct2dCad.wpf.Services.Input;

/// <summary>The last Escape layer, after the focused control has declined it.</summary>
internal static class CadEscapeHandler
{
    internal static bool Process(Window owner, CadDocumentViewModel? document, DependencyObject? focusedElement)
    {
        if (document is null) return false;
        var (canvas, canvasWindow) = FindCanvas(owner, document);

        // Restore the source value before moving focus: LostFocus must not submit
        // the discarded text. Already committed live property edits are not undone.
        var cancelledProperty = TryCancelPropertyEdit(focusedElement);
        if (!cancelledProperty)
        {
            if (canvas is not null) canvas.CancelInteraction();
            else document.Escape();
        }

        if (canvas is not null)
        {
            canvasWindow?.Activate();
            canvas.Focus();
        }
        return true;
    }

    internal static (CadCanvas? Canvas, Window? Window) FindCanvas(Window owner, CadDocumentViewModel document)
    {
        var windows = ApplicationWindows(owner).ToArray();
        foreach (var window in windows)
        {
            var canvas = Descendants(window).OfType<CadCanvas>().FirstOrDefault(IsActiveCanvas);
            if (canvas is not null) return (canvas, window);
        }

        // AvalonDock can host floating content in a child HWND with its own WPF
        // presentation source. That canvas is absent from Window's visual tree.
        // Verify the native root belongs to this router before traversing it.
        foreach (var source in PresentationSource.CurrentSources.OfType<HwndSource>())
        {
            if (source.IsDisposed || source.Dispatcher != owner.Dispatcher || source.RootVisual is not { } root) continue;
            var rootHandle = GetAncestor(source.Handle, 2 /* GA_ROOT */);
            var window = windows.FirstOrDefault(w => new WindowInteropHelper(w).Handle == rootHandle);
            if (window is null) continue;
            var canvas = Descendants(root).OfType<CadCanvas>().FirstOrDefault(IsActiveCanvas);
            if (canvas is not null) return (canvas, window);
        }
        return (null, null);

        bool IsActiveCanvas(CadCanvas canvas) => canvas.IsVisible && ReferenceEquals(canvas.DocumentViewModel, document);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    internal static bool TryCancelPropertyEdit(DependencyObject? focusedElement)
    {
        if (focusedElement is not TextBox { IsReadOnly: false } textBox) return false;
        for (DependencyObject? current = textBox; current is not null;
             current = LogicalTreeHelper.GetParent(current) ??
                 (current is Visual visual ? VisualTreeHelper.GetParent(visual) : null))
        {
            if (current is not UserControl || current.GetType().Namespace != typeof(EntityHeaderPropertySection).Namespace)
                continue;
            var expression = textBox.GetBindingExpression(TextBox.TextProperty);
            if (expression is null || expression.ParentBinding.Mode is BindingMode.OneWay or BindingMode.OneTime)
                return false;
            expression.UpdateTarget();
            Validation.ClearInvalid(expression);
            return true;
        }
        return false;
    }

    private static IEnumerable<Window> ApplicationWindows(Window owner)
    {
        yield return owner;
        if (System.Windows.Application.Current is not { } application || !application.CheckAccess()) yield break;
        var router = CadWindowShortcutRouter.Resolve(owner);
        foreach (Window window in application.Windows)
            if (window != owner && window.Dispatcher == owner.Dispatcher && router is not null &&
                ReferenceEquals(CadWindowShortcutRouter.Resolve(window), router))
                yield return window;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
