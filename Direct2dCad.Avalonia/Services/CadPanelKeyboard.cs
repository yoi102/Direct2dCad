using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using Direct2dCad.Avalonia.Controls;
using Direct2dCad.Avalonia.Views;
using Direct2dCad.Avalonia.Views.Toolboxes;

namespace Direct2dCad.Avalonia.Services;

internal static class CadPanelKeyboard
{
    internal static bool IsPropertyEditor(Control? control) => control is TextBox { IsReadOnly: false, AcceptsReturn: false } &&
        control.GetVisualAncestors().Any(ancestor => ancestor is PropertyInspector or
            Views.Generated.CadAnnotationParametersView or Views.Generated.CadEditParametersView ||
            ancestor is UserControl { DataContext: { } model } && model.GetType().Namespace == "Direct2dCad.ViewModels.Toolboxes.EntityProperty");

    internal static bool TryCommitProperty(Control? control)
    {
        if (!IsPropertyEditor(control)) return true;
        return control is CadPropertyNumberBox number ? number.TryCommitEdit() : !DataValidationErrors.GetHasErrors(control!);
    }

    internal static bool CancelProperty(Control? control)
    {
        if (!IsPropertyEditor(control)) return false;
        if (control is CadPropertyNumberBox number) number.CancelEdit();
        return true;
    }

    // Only passive panel surfaces opt in. An unhandled key in a list, button or
    // text editor does not mean the user intended to finish a CAD command.
    internal static bool CanConfirm(Control? control)
    {
        if (control is not (Panel or Border or TextBlock or ContentPresenter or UserControl or ScrollViewer or Window)) return false;
        return !control.GetVisualAncestors().Prepend(control).Any(ancestor => ancestor is
            TextBox or Button or MenuItem or RangeBase or Thumb or CadCanvas or AiView or TerminalView or NumericUpDown ||
            ancestor is ItemsControl and not TabControl);
    }
}
