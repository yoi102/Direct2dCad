using System.Collections.ObjectModel;
using AvalonDock.Core;
using AvalonDock.Mvvm.CommunityToolkit;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.ViewServices;
using MessagePipe;

namespace Direct2dCad.ViewModels.Toolboxes;

public partial class FolderExplorerToolboxViewModel : ObservableToolboxBase
{
    private IDockLayoutService? _dockLayoutService;
    private readonly ISubscriber<EditorTabDocumentSummaryChangedMessage> _documentSummaryChangedSubscriber;
    private bool _isSynchronizingSelection;

    public FolderExplorerToolboxViewModel(
        IToolboxIconsService toolboxIconsService,
        ISubscriber<EditorTabDocumentSummaryChangedMessage> documentSummaryChangedSubscriber)
    {
        _documentSummaryChangedSubscriber = documentSummaryChangedSubscriber;
        ContentId = Id = Guid.NewGuid().ToString();
        Title = "FolderExplorer";
        Icon = toolboxIconsService.Explorer;
        Shortcut = "Ctrl+Shift+E";
        IsOpenByDefault = true;
    }
    [ObservableProperty]
    public partial string ContentId { get; private set; }
    public ObservableCollection<FolderExplorerDocumentItemViewModel> Documents { get; } = [];

    public bool HasDocuments => Documents.Count > 0;

    [ObservableProperty]
    public partial FolderExplorerDocumentItemViewModel? SelectedDocument { get; set; }

    public void Attach(IDockLayoutService dockLayoutService)
    {
        if (ReferenceEquals(_dockLayoutService, dockLayoutService))
            return;

        _dockLayoutService = dockLayoutService ?? throw new ArgumentNullException(nameof(dockLayoutService));
        RefreshDocuments();
    }

    public void RefreshDocuments()
    {
        var activeDocument = _dockLayoutService?.ActiveDockable as EditorTabViewModel;

        foreach (var document in Documents)
            document.Dispose();

        Documents.Clear();

        if (_dockLayoutService is not null)
        {
            foreach (var document in _dockLayoutService.Documents.OfType<EditorTabViewModel>())
                Documents.Add(new FolderExplorerDocumentItemViewModel(document, _documentSummaryChangedSubscriber));
        }

        OnPropertyChanged(nameof(HasDocuments));
        SetActiveDocument(activeDocument);
    }

    public void SetActiveDocument(EditorTabViewModel? document)
    {
        _isSynchronizingSelection = true;
        try
        {
            SelectedDocument = document is null
                ? null
                : Documents.FirstOrDefault(x => ReferenceEquals(x.Document, document));
        }
        finally
        {
            _isSynchronizingSelection = false;
        }
    }

    partial void OnSelectedDocumentChanged(FolderExplorerDocumentItemViewModel? value)
    {
        if (_isSynchronizingSelection || value is null || _dockLayoutService is null)
            return;

        _dockLayoutService.ActiveDockable = value.Document;
    }

    [RelayCommand]
    private void Refresh()
    {
        RefreshDocuments();
    }
}

public sealed partial class FolderExplorerDocumentItemViewModel : ObservableObject, IDisposable
{
    private bool _isRefreshing;
    private readonly IDisposable _documentSummaryChangedSubscription;

    public FolderExplorerDocumentItemViewModel(
        EditorTabViewModel document,
        ISubscriber<EditorTabDocumentSummaryChangedMessage> documentSummaryChangedSubscriber)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        _documentSummaryChangedSubscription = documentSummaryChangedSubscriber.Subscribe(OnDocumentSummaryChanged);
        RefreshFromDocument();
    }

    public EditorTabViewModel Document { get; }

    [ObservableProperty]
    public partial string DocumentName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FilePath { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsModified { get; private set; }

    public string DisplayPath => string.IsNullOrWhiteSpace(FilePath)
        ? "Unsaved document"
        : FilePath;

    public string ToolTipText => string.IsNullOrWhiteSpace(FilePath)
        ? "Not saved yet."
        : FilePath;

    public string IconKind => string.IsNullOrWhiteSpace(FilePath)
        ? "FilePlusOutline"
        : "FileDocumentOutline";

    public void RefreshFromDocument()
    {
        _isRefreshing = true;
        try
        {
            DocumentName = Document.DocumentName;
            FilePath = Document.CurrentFilePath;
            IsModified = Document.IsModified;
        }
        finally
        {
            _isRefreshing = false;
        }

        OnPropertyChanged(nameof(DisplayPath));
        OnPropertyChanged(nameof(ToolTipText));
        OnPropertyChanged(nameof(IconKind));
    }

    partial void OnDocumentNameChanged(string value)
    {
        if (_isRefreshing)
            return;

        Document.TryRenameDocument(value);
        RefreshFromDocument();
    }

    private void OnDocumentSummaryChanged(EditorTabDocumentSummaryChangedMessage message)
    {
        if (!ReferenceEquals(message.EditorTabViewModel, Document))
            return;

        RefreshFromDocument();
    }

    public void Dispose()
    {
        _documentSummaryChangedSubscription.Dispose();
    }
}
