using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Threading;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.ViewModels.Blocks;
using Direct2dCad.ViewModels.Settings;
using Direct2dCad.ViewModels.Settings.UserSettings;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.Lang;
using DialogHostAvalonia;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.VisualTree;

namespace Direct2dCad.Avalonia.Services;

internal sealed class DialogService : IDialogService
{
    private readonly Dictionary<string, object> _progress = [];
    internal static DialogHost Host(string identifier) => identifier == ViewServiceIdentifiers.DocumentSettingsDialogHost && SettingsHost is { } host && TopLevel.GetTopLevel(host) is not null ? host : App.Window!.FindControl<DialogHost>("RootDialog")!;
    private static DialogHost? SettingsHost;
    private static string T(string key) => CadUiText.Get(key);
    internal static Task<T> Show<T>(string title, Control content, params (string Label, T Value, Func<bool>? Validate)[] buttons) => ShowAt(ViewServiceIdentifiers.RootDialogHost, title, content, buttons);
    private static async Task<T> ShowAt<T>(string identifier, string title, Control content, params (string Label, T Value, Func<bool>? Validate)[] buttons)
    {
        var host = Host(identifier);
        host.CurrentSession?.Close();
        var focus = TopLevel.GetTopLevel(host)?.FocusManager?.GetFocusedElement();
        var panel = new Grid { Width = 480, MaxWidth = Math.Max(240, host.Bounds.Width - 48), MaxHeight = Math.Max(160, host.Bounds.Height - 48), Margin = new global::Avalonia.Thickness(20), RowDefinitions = RowDefinitions.Parse("Auto,*,Auto") };
        KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Cycle);
        panel.Children.Add(new TextBlock { Text = title, FontSize = 18, Margin = new global::Avalonia.Thickness(4,0,4,16) });
        var scroller = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }; Grid.SetRow(scroller, 1); panel.Children.Add(scroller);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new global::Avalonia.Thickness(0,16,0,0) }; Grid.SetRow(actions, 2); panel.Children.Add(actions);
        foreach (var choice in buttons) { var button = new Button { Content = choice.Label, MinWidth = 80 }; button.Classes.Add("flat"); button.Click += (_, _) => { if (choice.Validate?.Invoke() != false) host.CurrentSession?.Close(choice.Value); }; actions.Children.Add(button); }
        panel.AddHandler(Control.KeyDownEvent, (_, e) =>
        {
            if (e.Handled || e.KeyModifiers != KeyModifiers.None) return;
            if (e.Key == Key.Escape) { host.CurrentSession?.Close(buttons[^1].Value); e.Handled = true; }
            else if (e.Key == Key.Enter && e.Source is not TextBox { AcceptsReturn: true } && e.Source is not Button)
            { if (buttons[0].Validate?.Invoke() != false) host.CurrentSession?.Close(buttons[0].Value); e.Handled = true; }
        }, RoutingStrategies.Bubble);
        try
        {
            var result = await DialogHost.Show(panel, host, openedEventHandler: (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (host.IsOpen) (content.GetVisualDescendants().Prepend(content).OfType<Control>().FirstOrDefault(c => c.Focusable && c.IsEffectivelyEnabled && c.IsEffectivelyVisible) ?? actions.Children[0]).Focus();
            }));
            // Replacement/progress closure must cancel, never default an enum to Save/Overwrite.
            return result is T value ? value : buttons[^1].Value;
        }
        finally { if (!host.IsOpen) focus?.Focus(); }
    }
    private static Control Text(string text) => new TextBlock { Text = text, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, Margin = new global::Avalonia.Thickness(8) };
    public void Close(string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => Dispatcher.UIThread.Post(() => { _progress.Remove(dialogIdentifier); Host(dialogIdentifier).CurrentSession?.Close(); });
    public IDisposable ShowProgressBarDialog(string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost)
    {
        var token = new object();
        DialogHost? progressHost = null; DialogSession? progressSession = null;
        Dispatcher.UIThread.Post(() => { var host = Host(dialogIdentifier); host.CurrentSession?.Close(); var content = new StackPanel { Width = 320, Margin = new global::Avalonia.Thickness(24), Spacing = 16 }; content.Children.Add(new TextBlock { Text = T("Loading") }); content.Children.Add(new ProgressBar { IsIndeterminate = true }); _progress[dialogIdentifier] = token; _ = ShowProgress(host, content); progressHost = host; progressSession = host.CurrentSession; });
        return new Scope(() => Dispatcher.UIThread.Post(() => { if (_progress.TryGetValue(dialogIdentifier, out var current) && ReferenceEquals(current, token)) { _progress.Remove(dialogIdentifier); if (ReferenceEquals(progressHost?.CurrentSession, progressSession)) progressSession?.Close(); } }));
    }
    private static async Task ShowProgress(DialogHost host, Control content)
    {
        var focus = TopLevel.GetTopLevel(host)?.FocusManager?.GetFocusedElement();
        try { await DialogHost.Show(content, host); }
        finally { if (!host.IsOpen) focus?.Focus(); }
    }
    private sealed class Scope(Action close) : IDisposable { public void Dispose() => close(); }
    public async Task ShowOrReplaceMessageDialogAsync(string message, string header = "", string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => await ShowAt(dialogIdentifier, header, Text(message), (T("Confirm"), true, null));
    public async Task<bool> ShowOrReplaceMessageDialogWithCancelAsync(string message, string header = "", string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => await ShowAt(dialogIdentifier, header, Text(message), (T("Confirm"), true, null), (T("Cancel"), false, null));
    public Task<bool> ShowExitConfirmation(string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => ShowOrReplaceMessageDialogWithCancelAsync(T("ConfirmExitMessage"), T("ConfirmExitTitle"), dialogIdentifier);
    public Task<UnsavedDocumentDialogResult> ShowUnsavedDocumentDialogAsync(string name, string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => Unsaved([name], dialogIdentifier);
    public Task<UnsavedDocumentDialogResult> ShowUnsavedDocumentsDialogAsync(IReadOnlyList<UnsavedDocumentInfo> documents, string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => Unsaved(documents.Select(d => d.Name), dialogIdentifier);
    private static async Task<UnsavedDocumentDialogResult> Unsaved(IEnumerable<string> names, string identifier) => await ShowAt(identifier, T("Save"), Text(string.Join(Environment.NewLine, names)), (T("Save"), UnsavedDocumentDialogResult.Save, null), (T("DontSave"), UnsavedDocumentDialogResult.Discard, null), (T("Cancel"), UnsavedDocumentDialogResult.Cancel, null));
    public async Task<CadFileConflictChoice> ShowFileConflictDialogAsync(string path, string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) => await ShowAt(dialogIdentifier, T("SaveConflict"), Text(T("FileConflictDescription") + Environment.NewLine + path), (T("Overwrite"), CadFileConflictChoice.Overwrite, null), (T("SaveAs"), CadFileConflictChoice.SaveAs, null), (T("Cancel"), CadFileConflictChoice.Cancel, null));
    public async Task<CadUnit?> ChooseDxfSourceUnitAsync()
    {
        var units = new ComboBox { ItemsSource = Enum.GetValues<CadUnit>(), SelectedItem = CadUnit.Millimeter };
        var result = await Show("DXF · " + T("Unit"), units, (T("Confirm"), true, null), (T("Cancel"), false, null)); return result ? (CadUnit?)units.SelectedItem : null;
    }
    public async Task<GridSpacingPresetDialogResult?> ShowGridSpacingPresetDialogAsync(GridSpacingPresetDialogRequest request, string dialogIdentifier = ViewServiceIdentifiers.DocumentSettingsDialogHost) { var vm = new GridSpacingPresetEditorViewModel(request); var accepted = await ShowAt(dialogIdentifier, T("GridSpacing"), KnownViews.Create(vm)!, (T("Confirm"), true, () => vm.IsValid), (T("Cancel"), false, null)); return accepted ? vm.CreateResult() : null; }
    public async Task<CreateBlockDialogResult?> ShowCreateBlockDialogAsync(CreateBlockDialogRequest request, string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) { var vm = new CreateBlockDialogViewModel(request); var accepted = await ShowAt(dialogIdentifier, T("CreateBlock"), KnownViews.Create(vm)!, (T("Confirm"), true, () => vm.IsValid), (T("Cancel"), false, null)); return accepted ? vm.CreateResult() : null; }
    public async Task<AiAssistantSettingsDialogResult?> ShowAiAssistantSettingsDialogAsync(AiAssistantSettingsDialogRequest request, string dialogIdentifier = ViewServiceIdentifiers.RootDialogHost) { var vm = new AiAssistantSettingsDialogViewModel(request); var accepted = await ShowAt(dialogIdentifier, T("AiAssistant"), KnownViews.Create(vm)!, (T("Confirm"), true, () => vm.IsValid), (T("Cancel"), false, null)); return accepted ? vm.CreateResult() : null; }
    public async void ShowDocumentSettingsDialog(IDocumentSettingsDialogViewModel model)
    {
        if (model is not DocumentSettingsViewModel vm) throw new ArgumentException("Unknown document settings model.");
        await ShowSettings(T("DocumentSettings"), vm, KnownViews.Create(vm)!, vm.TryApply, vm.ResetToDefaults, () => vm.ValidationError, 940, 620);
    }
    public async void ShowUserSettingsDialog(IUserSettingsDialogViewModel model)
    {
        if (model is not UserSettingsViewModel vm) throw new ArgumentException("Unknown user settings model.");
        await ShowSettings(T("ApplicationSettings"), vm, KnownViews.Create(vm)!, vm.TryApply, vm.ResetToDefaults, () => vm.ValidationError, 760, 650);
    }
    private static async Task ShowSettings(string title, System.ComponentModel.INotifyPropertyChanged model, Control view, Func<bool> apply, Action reset, Func<string?> validation, double width, double height)
    {
        var window = new Window { Title = title, Width = width, Height = height, MinWidth = width - 110, MinHeight = height - 120, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new Grid { Margin = new global::Avalonia.Thickness(16), RowDefinitions = RowDefinitions.Parse("*,Auto,Auto") }; root.Children.Add(view);
        var error = new TextBlock { Text = validation(), TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, Foreground = global::Avalonia.Media.Brushes.IndianRed, Margin = new global::Avalonia.Thickness(4,8,4,0) }; Grid.SetRow(error, 1); root.Children.Add(error);
        var actions = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), Margin = new global::Avalonia.Thickness(0,14,0,0) }; Grid.SetRow(actions, 2); root.Children.Add(actions);
        var resetButton = new Button { Content = T("Reset"), MinWidth = 88, HorizontalAlignment = HorizontalAlignment.Left }; resetButton.Click += (_, _) => reset(); actions.Children.Add(resetButton);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; Grid.SetColumn(right, 1); actions.Children.Add(right);
        var confirm = new Button { Content = T("Ok"), MinWidth = 88, IsDefault = true }; confirm.Click += (_, _) => { if (apply()) window.Close(); };
        var applyButton = new Button { Content = T("Apply"), MinWidth = 88 }; applyButton.Click += (_, _) => apply();
        var cancel = new Button { Content = T("Cancel"), MinWidth = 88, IsCancel = true }; cancel.Click += (_, _) => window.Close();
        right.Children.Add(confirm); right.Children.Add(applyButton); right.Children.Add(cancel);
        System.ComponentModel.PropertyChangedEventHandler changed = (_, e) => { if (e.PropertyName == "ValidationError") error.Text = validation(); }; model.PropertyChanged += changed;
        var host = new DialogHost { Identifier = ViewServiceIdentifiers.DocumentSettingsDialogHost, Content = root, CloseOnClickAway = false };
        var previousHost = SettingsHost; SettingsHost = host;
        window.Closed += (_, _) => host.CurrentSession?.Close();
        window.AddHandler(Control.KeyDownEvent, (_, e) => { if (host.IsOpen && e.Key == Key.Escape) { host.CurrentSession?.Close(); e.Handled = true; } }, RoutingStrategies.Tunnel);
        window.Content = Controls.CadWindowChrome.Wrap(window,host); try { await window.ShowDialog(App.Window!); } finally { SettingsHost = previousHost; model.PropertyChanged -= changed; }
    }
}


