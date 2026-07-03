using System.Collections.ObjectModel;
using System.ComponentModel;
using AvalonDock.Core;
using AvalonDock.Mvvm.CommunityToolkit;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.ViewModels.Services;

namespace Direct2dCad.ViewModels.Toolboxes;

public partial class FolderExplorerToolboxViewModel : ObservableToolboxBase
{
    private IDockLayoutService? _dockLayoutService;
    private bool _isSynchronizingSelection;

    public FolderExplorerToolboxViewModel(IToolboxIconsService toolboxIconsService)
    {
        Id = Guid.NewGuid().ToString();
        Title = "FolderExplorer";
        Icon = toolboxIconsService.Explorer;
        Shortcut = "Ctrl+Shift+E";
        IsOpenByDefault = true;
    }

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
                Documents.Add(new FolderExplorerDocumentItemViewModel(document));
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

    public FolderExplorerDocumentItemViewModel(EditorTabViewModel document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Document.PropertyChanged += OnDocumentPropertyChanged;
        RefreshFromDocument();
    }

    public EditorTabViewModel Document { get; }

    [ObservableProperty]
    public partial string DocumentName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FilePath { get; private set; } = string.Empty;

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

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorTabViewModel.DocumentName) or
            nameof(EditorTabViewModel.CurrentFilePath) or
            nameof(EditorTabViewModel.Title))
        {
            RefreshFromDocument();
        }
    }

    public void Dispose()
    {
        Document.PropertyChanged -= OnDocumentPropertyChanged;
    }
}
