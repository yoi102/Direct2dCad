using System.Collections.ObjectModel;
using AvalonDock.Core;
using AvalonDock.Mvvm.CommunityToolkit;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Platform.Notifications;

namespace Direct2dCad.ViewModels.Toolboxes;

public partial class MessageToolboxViewModel : CadToolboxViewModelBase, IDisposable
{
    private readonly ICadMessageLog _messageLog;

    public MessageToolboxViewModel(
        IToolboxLayoutSettingsStore toolboxLayoutSettingsStore,
        IToolboxIconProvider toolboxIconProvider,
        ICadMessageLog messageLog)
        : base(toolboxLayoutSettingsStore, "toolbox.messages", DockZone.BottomRight, isOpenByDefault: false)
    {
        _messageLog = messageLog ?? throw new ArgumentNullException(nameof(messageLog));

        Title = Strings.Messages;
        Icon = toolboxIconProvider.Messages;
        Shortcut = "Ctrl+Shift+M";
        CanClose = false;

        _messageLog.MessageAdded += OnMessageAdded;
        _messageLog.Cleared += OnMessagesCleared;
        RefreshEntries();
    }

    public ObservableCollection<CadMessageEntry> VisibleEntries { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial CadMessageLevel? SelectedLevel { get; set; }

    public int MessageCount => VisibleEntries.Count;

    [RelayCommand]
    private void ClearMessages()
    {
        _messageLog.Clear();
    }

    partial void OnSearchTextChanged(string value) => RefreshEntries();

    partial void OnSelectedLevelChanged(CadMessageLevel? value) => RefreshEntries();

    public void Dispose()
    {
        _messageLog.MessageAdded -= OnMessageAdded;
        _messageLog.Cleared -= OnMessagesCleared;
    }

    private void OnMessageAdded(object? sender, CadMessageEntry entry) => RefreshEntries();

    private void OnMessagesCleared(object? sender, EventArgs e) => RefreshEntries();

    private void RefreshEntries()
    {
        var search = SearchText.Trim();

        VisibleEntries.Clear();
        foreach (var entry in _messageLog.Entries.Reverse())
        {
            if (SelectedLevel is { } selectedLevel && entry.Level != selectedLevel)
                continue;

            if (search.Length > 0 &&
                !entry.Text.Contains(search, StringComparison.CurrentCultureIgnoreCase) &&
                !(entry.Source?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false))
            {
                continue;
            }

            VisibleEntries.Add(entry);
        }

        OnPropertyChanged(nameof(MessageCount));
    }
}
