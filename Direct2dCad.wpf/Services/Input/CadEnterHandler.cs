using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Direct2dCad.ViewModels;
using Direct2dCad.wpf.Controls;
using Direct2dCad.wpf.Views.Toolboxes;
using Direct2dCad.wpf.Views.Toolboxes.EntityProperty;

namespace Direct2dCad.wpf.Services.Input;

/// <summary>Contextual Enter after the focused control has had its own confirmation opportunity.</summary>
internal static class CadEnterHandler
{
    internal static bool Process(Window owner, CadDocumentViewModel? document, DependencyObject? focusedElement)
    {
        if (document is not { CanEditDocument: true }) return false;
        var propertyEditor = IsSingleLinePropertyEditor(focusedElement);
        if (!propertyEditor && !CanConfirmFrom(focusedElement)) return false;
        var (canvas, window) = CadEscapeHandler.FindCanvas(owner, document);
        if (propertyEditor)
        {
            // Do not let LostFocus commit invalid text or let this Enter finish a drawing as well.
            if (!MainWindow.TryCommitFocusedPropertyEdit(focusedElement)) return true;
        }
        else if (canvas is null || !canvas.ConfirmInteraction()) return false;

        if (canvas is not null)
        {
            window?.Activate();
            canvas.Focus();
        }
        return true;
    }

    internal static bool IsSingleLinePropertyEditor(DependencyObject? focusedElement)
    {
        if (focusedElement is not TextBox { IsReadOnly: false, AcceptsReturn: false } textBox) return false;
        var expression = textBox.GetBindingExpression(TextBox.TextProperty);
        if (expression is null || expression.ParentBinding.Mode is BindingMode.OneWay or BindingMode.OneTime) return false;
        foreach (var ancestor in Ancestors(Parent(textBox)))
        {
            // Editable combo boxes and numeric controls retain their own commit/validation rules.
            if (ancestor is ItemsControl and not TabControl || ancestor is ButtonBase or MahApps.Metro.Controls.NumericUpDown) return false;
            if (ancestor is UserControl && ancestor.GetType().Namespace == typeof(EntityHeaderPropertySection).Namespace)
                return true;
        }
        return false;
    }

    internal static bool CanConfirmFrom(DependencyObject? focusedElement)
    {
        // An unhandled key is not permission to submit: only passive panel surfaces opt in.
        if (focusedElement is not (Panel or Border or TextBlock or ContentPresenter or UserControl or ScrollViewer or Window))
            return false;
        foreach (var ancestor in Ancestors(focusedElement))
        {
            if (ancestor is TextBoxBase or PasswordBox or ButtonBase or MenuItem or
                RangeBase or Thumb or Hyperlink or CadCanvas or AiAssistantToolboxView or CommandLineToolboxView ||
                ancestor is ItemsControl and not TabControl || ancestor is MahApps.Metro.Controls.NumericUpDown)
                return false;
        }
        return true;
    }

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject? element)
    {
        for (var current = element; current is not null; current = Parent(current)) yield return current;
    }

    private static DependencyObject? Parent(DependencyObject element) =>
        (element is Visual ? VisualTreeHelper.GetParent(element) : null) ??
        (element is FrameworkContentElement content ? content.Parent : null) ?? LogicalTreeHelper.GetParent(element);
}
