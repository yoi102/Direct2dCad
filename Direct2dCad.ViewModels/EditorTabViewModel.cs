using System.Collections.ObjectModel;
using System.ComponentModel;
using AvalonDock.Core;
using AvalonDock.Mvvm.CommunityToolkit;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.IO;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Interactions;
using Direct2dCad.ViewModels.Layouts;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Documents;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Platform.Notifications;
using Direct2dCad.ViewModels.Services.Platform.Printing;
using Direct2dCad.ViewModels.Settings;
using MessagePipe;

namespace Direct2dCad.ViewModels;


public abstract class CadObservableDocument : ObservableDocument
{

}


public partial class EditorTabViewModel : CadObservableDocument, IEditorTabDocumentSummaryMessageSource, IDisposable
{
    private readonly IUserSettingsStore _userSettingsStore;
    private readonly IWorkspaceSettingsStore _workspaceSettingsStore;

    private readonly CadUserSettings _userSettings;
    private readonly CadDocumentSaveSession _saveSession;
    private readonly CadSelectionAvailabilityCache _selectionAvailability = new();
    private long _notifiedSelectionAvailabilityVersion = -1;
    private readonly IFileDialogService _fileDialogService;
    private readonly IDialogService _dialogService;
    private readonly ISnackbarService _snackbarService;
    private readonly ICadPrintService _printService;
    private readonly IDockLayoutService _dockLayoutService;
    private bool _isSyncingViewSettings;
    private bool _isSyncingUserSettings;
    private bool _isRestoringWorkspaceSettings;
    private bool _disposed;
    private IDisposable? _documentScope;
    private CadEditor? _trackedEditor;
    private readonly IDisposable _viewSettingsChangedSubscription;
    private readonly IDisposable _selectionFilterChangedSubscription;
    private readonly IDisposable _interactionStateChangedSubscription;
    private readonly IPublisher<EditorTabDocumentSummaryChangedMessage> _documentSummaryChangedPublisher;

    public EditorTabViewModel(CadDocumentViewModel cadDocumentViewModel,
        IUserSettingsStore userSettingsStore,
        IWorkspaceSettingsStore workspaceSettingsStore,
        IDockLayoutService dockLayoutService,
        IFileDialogService fileDialogService,
        IDialogService dialogService,
        ISnackbarService snackbarService,
        ICadPrintService printService,
        ISubscriber<CadDocumentViewSettingsChangedMessage> viewSettingsChangedSubscriber,
        ISubscriber<CadSelectionFilterChangedMessage> selectionFilterChangedSubscriber,
        ISubscriber<CadDocumentInteractionStateChangedMessage> interactionStateChangedSubscriber,
        IPublisher<EditorTabDocumentSummaryChangedMessage> documentSummaryChangedPublisher,
        ICadDocumentWriter documentWriter
        )
    {
        _userSettingsStore = userSettingsStore;
        _workspaceSettingsStore = workspaceSettingsStore;
        _dockLayoutService = dockLayoutService;
        _userSettings = _userSettingsStore.Load();
        _fileDialogService = fileDialogService;
        _dialogService = dialogService;
        _snackbarService = snackbarService;
        _printService = printService;
        _documentSummaryChangedPublisher = documentSummaryChangedPublisher;
        CadDocumentViewModel = cadDocumentViewModel;
        _saveSession = new(CadDocumentViewModel.CadEditor, documentWriter);
        LayoutWorkspace = new LayoutWorkspaceViewModel(CadDocumentViewModel);
        CadDocumentViewModel.ApplyUserSettings(_userSettings);
        try
        {
            CadDocumentViewModel.PropertyChanged += OnCadDocumentViewModelPropertyChanged;
            _viewSettingsChangedSubscription = viewSettingsChangedSubscriber.Subscribe(OnCadDocumentViewSettingsChanged);
            _selectionFilterChangedSubscription = selectionFilterChangedSubscriber.Subscribe(OnSelectionFilterChanged);
            _interactionStateChangedSubscription = interactionStateChangedSubscriber.Subscribe(OnInteractionStateChanged);
            AttachDocumentChangeTracking(CadDocumentViewModel.CadEditor);
            CadDocumentViewModel.DrawingDefaults.Text = TextInput;
            ApplyDocumentViewSettingsToToolbar();
            ApplyUserSettingsToToolbar();
            CadCanvasToolMode = CadDocumentViewModel.CadCanvasToolMode;
            ContentId = Id = cadDocumentViewModel.CadEditor.Document.Id.ToString();
            Title = cadDocumentViewModel.CadEditor.Document.Name;
            ToolTip = $"id: {cadDocumentViewModel.CadEditor.Document.Id}";
            RefreshModifiedState();
        }
        catch
        {
            // DI cannot dispose an object whose constructor never returned.
            DetachDocumentChangeTracking();
            CadDocumentViewModel.PropertyChanged -= OnCadDocumentViewModelPropertyChanged;
            _viewSettingsChangedSubscription?.Dispose();
            _selectionFilterChangedSubscription?.Dispose();
            _interactionStateChangedSubscription?.Dispose();
            _saveSession.Dispose();
            _recoveryLifetime.Dispose();
            Operation.Dispose();
            throw;
        }

    }

    public async Task<bool> ConfirmCloseAsync()
    {
        await _saveSession.WaitForIdleAsync();
        if (IsModified && !await ConfirmCloseCoreAsync())
            return false;

        await CompleteRecoveryCloseAsync();
        return true;
    }

    internal Task WaitForSaveIdleAsync() => _saveSession.WaitForIdleAsync();

    internal async Task<bool> SaveForCloseAsync()
    {
        return await TrySaveFileAsync() && !IsModified;
    }

    internal async Task<bool> SaveForWorkspaceToolAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(CurrentFilePath))
            return await SaveToAsync(cancellationToken: cancellationToken, interactiveConflict: false);

        var fileName = string.IsNullOrWhiteSpace(CadDocumentViewModel.CadEditor.Document.Name)
            ? "Untitled.d2cad"
            : $"{CadDocumentViewModel.CadEditor.Document.Name}.d2cad";
        var selectedFileName = _fileDialogService.SaveAsD2cad(fileName);
        if (selectedFileName is null)
            return false;

        var authorized = await Task.Run(() => CadFileRevision.Capture(selectedFileName), cancellationToken);
        return await SaveToAsync(selectedFileName, cancellationToken, interactiveConflict: false, overwriteAuthorization: authorized);
    }

    internal async Task<bool> SaveToFileForWorkspaceToolAsync(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await SaveToAsync(filePath, cancellationToken, interactiveConflict: false);
    }

    private async Task<bool> ConfirmCloseCoreAsync()
    {
        var result = await _dialogService.ShowUnsavedDocumentDialogAsync(DocumentName);
        return result switch
        {
            UnsavedDocumentDialogResult.Save => await SaveForCloseAsync(),
            UnsavedDocumentDialogResult.Discard => true,
            _ => false
        };
    }

    public CadDocumentViewModel CadDocumentViewModel { get; }
    public LayoutWorkspaceViewModel LayoutWorkspace { get; }
    public string LayoutSpaceGroupName { get; } = $"LayoutSpaceMode_{Guid.NewGuid():N}";

    public string DocumentName => CadDocumentViewModel.CadEditor.Document.Name;
    public bool IsCompatibilityReadOnly => CadDocumentViewModel.CadEditor.Document.IsReadOnly;
    public CadLongOperation Operation { get; } = new();

    [ObservableProperty]
    public partial string CurrentFilePath { get; private set; } = string.Empty;

    partial void OnCurrentFilePathChanged(string value)
    {
        PublishDocumentSummaryChanged();
    }

    [ObservableProperty]
    public partial string ToolTip { get; private set; }
    [ObservableProperty]
    public partial string ContentId { get; private set; }
    [ObservableProperty]
    public partial string TextInput { get; set; } = "Text";

    [ObservableProperty]
    public partial ViewModelCadGridType ViewModelCadGridType { get; set; } = ViewModelCadGridType.Lines;

    public ObservableCollection<GridSpacingPresetItemViewModel> GridSpacingPresets { get; } = [];

    [ObservableProperty]
    public partial GridSpacingPresetItemViewModel? SelectedMajorGridSpacingPreset { get; set; }

    [ObservableProperty]
    public partial GridSpacingPresetItemViewModel? SelectedMinorGridSpacingPreset { get; set; }

    [ObservableProperty]
    public partial ViewModelCadSnapMarkerType ViewModelCadSnapMarkerType { get; set; } = ViewModelCadSnapMarkerType.Cross;

    [ObservableProperty]
    public partial ViewModelCadOriginDisplayType ViewModelCadOriginDisplayType { get; set; } = ViewModelCadOriginDisplayType.AxesAndMarker;

    [ObservableProperty]
    public partial ViewModelCadOriginMarkerType ViewModelCadOriginMarkerType { get; set; } = ViewModelCadOriginMarkerType.Circle;

    [ObservableProperty]
    public partial ViewModelCadOriginLinePattern ViewModelCadOriginLinePattern { get; set; } = ViewModelCadOriginLinePattern.Solid;

    [ObservableProperty]
    public partial double ViewModelCadOriginSize { get; set; } = 18.0;

    [ObservableProperty]
    public partial double ViewModelCadOriginStrokeWidth { get; set; } = 0.62;

    [ObservableProperty]
    public partial string ViewModelCadOriginColorText { get; set; } = "#FF50BEFF";

    [ObservableProperty]
    public partial CadColor ViewModelCadBackgroundColor { get; set; }

    [ObservableProperty]
    public partial double ViewModelCadOriginX { get; set; }

    [ObservableProperty]
    public partial double ViewModelCadOriginY { get; set; }

    [ObservableProperty]
    public partial bool IsAntialiasingEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsTextAntialiasingEnabled { get; set; } = true;

    [ObservableProperty]
    public partial string SelectedEntityColorText { get; set; } = "#F0FFD65C";

    [ObservableProperty]
    public partial string SelectionWindowStrokeColorText { get; set; } = "#E640C4FF";

    [ObservableProperty]
    public partial string SelectionWindowFillColorText { get; set; } = "#2040C4FF";

    [ObservableProperty]
    public partial string SelectionCrossingStrokeColorText { get; set; } = "#E65CDC80";

    [ObservableProperty]
    public partial string SelectionCrossingFillColorText { get; set; } = "#245CDC80";
    [ObservableProperty]
    public partial CadCanvasToolMode CadCanvasToolMode { get; set; } = CadCanvasToolMode.Select;

    [ObservableProperty]
    public partial ViewModelCadUnit ViewModelCadUnit { get; set; } = ViewModelCadUnit.Millimeter;

    public string DocumentUnitSymbol => CadUnitConversion.GetSymbol((CadUnit)ViewModelCadUnit);

    partial void OnTextInputChanged(string value)
    {
        CadDocumentViewModel.DrawingDefaults.Text = value;
        CadDocumentViewModel.RequestRender();
    }

    partial void OnViewModelCadUnitChanged(ViewModelCadUnit value)
    {
        OnPropertyChanged(nameof(DocumentUnitSymbol));
        if (_isSyncingViewSettings)
            return;

        var document = CadDocumentViewModel.CadEditor.Document;
        var unit = (CadUnit)value;
        if (document.DocumentSettings.Unit == unit)
            return;

        CadDocumentViewModel.SetDocumentUnit(unit);
    }

    partial void OnViewModelCadGridTypeChanged(ViewModelCadGridType value)
    {
        if (_isSyncingViewSettings)
            return;

        var gridType = (CadGridType)value;
        if (CadDocumentViewModel.CadEditor.Document.ViewSettings.Grid.Type == gridType)
            return;

        CadDocumentViewModel.CadEditor.SetGridType(gridType);
    }

    partial void OnSelectedMajorGridSpacingPresetChanged(GridSpacingPresetItemViewModel? value)
    {
        ApplyGridSpacingPresetsFromToolbar();
    }

    partial void OnSelectedMinorGridSpacingPresetChanged(GridSpacingPresetItemViewModel? value)
    {
        ApplyGridSpacingPresetsFromToolbar();
    }

    partial void OnViewModelCadSnapMarkerTypeChanged(ViewModelCadSnapMarkerType value)
    {
        if (_isSyncingViewSettings)
            return;

        var markerType = (CadSnapMarkerType)value;
        if (CadDocumentViewModel.CadEditor.Document.ViewSettings.Grid.SnapMarkerType == markerType)
            return;

        CadDocumentViewModel.CadEditor.SetSnapMarkerType(markerType);
    }

    partial void OnViewModelCadOriginDisplayTypeChanged(ViewModelCadOriginDisplayType value)
    {
        if (_isSyncingViewSettings)
            return;

        ApplyOriginSettingsFromToolbar();
    }

    partial void OnViewModelCadOriginMarkerTypeChanged(ViewModelCadOriginMarkerType value)
    {
        if (_isSyncingViewSettings)
            return;

        ApplyOriginSettingsFromToolbar();
    }

    partial void OnViewModelCadOriginLinePatternChanged(ViewModelCadOriginLinePattern value)
    {
        if (_isSyncingViewSettings)
            return;

        ApplyOriginSettingsFromToolbar();
    }

    partial void OnViewModelCadOriginSizeChanged(double value)
    {
        if (_isSyncingViewSettings)
            return;

        ApplyOriginSettingsFromToolbar();
    }

    partial void OnViewModelCadOriginStrokeWidthChanged(double value)
    {
        if (_isSyncingViewSettings)
            return;

        ApplyOriginSettingsFromToolbar();
    }

    partial void OnViewModelCadOriginColorTextChanged(string value)
    {
        if (_isSyncingViewSettings)
            return;

        ApplyOriginSettingsFromToolbar();
    }

    partial void OnViewModelCadBackgroundColorChanged(CadColor value)
    {
        if (_isSyncingViewSettings)
            return;

        ApplyBackgroundColorFromToolbar();
    }

    partial void OnViewModelCadOriginXChanged(double value)
    {
        if (_isSyncingViewSettings)
            return;

        ApplyOriginPositionFromToolbar();
    }

    partial void OnViewModelCadOriginYChanged(double value)
    {
        if (_isSyncingViewSettings)
            return;

        ApplyOriginPositionFromToolbar();
    }

    partial void OnIsAntialiasingEnabledChanged(bool value)
    {
        if (_isSyncingUserSettings)
            return;

        ApplyUserSettingsFromToolbar();
    }

    partial void OnIsTextAntialiasingEnabledChanged(bool value)
    {
        if (_isSyncingUserSettings)
            return;

        ApplyUserSettingsFromToolbar();
    }

    partial void OnSelectedEntityColorTextChanged(string value)
    {
        if (_isSyncingUserSettings)
            return;

        ApplyUserSettingsFromToolbar();
    }

    partial void OnSelectionWindowStrokeColorTextChanged(string value)
    {
        if (_isSyncingUserSettings)
            return;

        ApplyUserSettingsFromToolbar();
    }

    partial void OnSelectionWindowFillColorTextChanged(string value)
    {
        if (_isSyncingUserSettings)
            return;

        ApplyUserSettingsFromToolbar();
    }

    partial void OnSelectionCrossingStrokeColorTextChanged(string value)
    {
        if (_isSyncingUserSettings)
            return;

        ApplyUserSettingsFromToolbar();
    }

    partial void OnSelectionCrossingFillColorTextChanged(string value)
    {
        if (_isSyncingUserSettings)
            return;

        ApplyUserSettingsFromToolbar();
    }

    [RelayCommand]
    private Task SaveFileAsync()
    {
        return TrySaveFileAsync();
    }

    private async Task<bool> TrySaveFileAsync()
    {
        if (IsCompatibilityReadOnly) return await SaveCompatibleCopyCoreAsync();
        if (string.IsNullOrWhiteSpace(CurrentFilePath))
            return await TrySaveAsFileAsync();

        return await SaveToAsync();
    }

    [RelayCommand]
    private Task SaveAsFileAsync()
    {
        return TrySaveAsFileAsync();
    }

    private async Task<bool> TrySaveAsFileAsync()
    {
        if (IsCompatibilityReadOnly) return await SaveCompatibleCopyCoreAsync();
        var fileName = string.IsNullOrWhiteSpace(CadDocumentViewModel.CadEditor.Document.Name)
                  ? "Untitled.d2cad"
                  : $"{CadDocumentViewModel.CadEditor.Document.Name}.d2cad";
        var selectedFileName = _fileDialogService.SaveAsD2cad(fileName);
        if (selectedFileName is null)
            return false;

        var authorized = await Task.Run(() => CadFileRevision.Capture(selectedFileName));
        return await SaveToAsync(selectedFileName, overwriteAuthorization: authorized);
    }

    [RelayCommand]
    private Task SaveCompatibleCopyAsync() => SaveCompatibleCopyCoreAsync();

    private async Task<bool> SaveCompatibleCopyCoreAsync()
    {
        var target = _fileDialogService.ChooseCompatibleCopyPath(DocumentName + ".compatible.d2cad");
        if (target is null) return false;
        if(!string.IsNullOrWhiteSpace(CurrentFilePath) && string.Equals(Path.GetFullPath(target),Path.GetFullPath(CurrentFilePath),StringComparison.OrdinalIgnoreCase))
        { await _dialogService.ShowOrReplaceMessageDialogAsync(Direct2dCad.Lang.CadUiText.Get("CompatibleCopyDifferentPath"),Strings.SaveAs);return false; }
        var expected = await Task.Run(() => CadFileRevision.Capture(target));
        var warning=Direct2dCad.Lang.CadUiText.Get(Direct2dCad.Lang.LangKeys.CompatibilityCopyWarning);
        if(expected.Exists) warning+=Environment.NewLine+Direct2dCad.Lang.CadUiText.Get("CompatibleCopyOverwrite")+Environment.NewLine+target;
        if (!await _dialogService.ShowOrReplaceMessageDialogWithCancelAsync(
            warning,
            Direct2dCad.Lang.CadUiText.Get(Direct2dCad.Lang.LangKeys.SaveCompatibleCopy))) return false;
        try
        {
            return await Operation.RunAsync(Direct2dCad.Lang.CadUiText.Get(Direct2dCad.Lang.LangKeys.SavingDrawing), async token =>
            {
                if (!await _saveSession.SaveCompatibilityCopyAsync(target, expected, token)) return false;
                var document = await new CadDocumentStorage().LoadAsync(target, token);
                if (_disposed) return false;
                Load(document, target);
                return true;
            });
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) { await _dialogService.ShowOrReplaceMessageDialogAsync(ex.Message, Strings.SaveAs); return false; }
    }

    private bool CanPrint() => !_disposed;

    [RelayCommand(CanExecute = nameof(CanPrint))]
    private async Task PrintAsync()
    {
        IDisposable? progressDialog = null;

        void SetPrintBusy(bool isBusy)
        {
            if (isBusy)
            {
                progressDialog ??= _dialogService.ShowProgressBarDialog();
                return;
            }

            progressDialog?.Dispose();
            progressDialog = null;
        }

        try
        {
            var editor=CadDocumentViewModel.CadEditor;
            var version=editor.DocumentChangeVersion;
            var request=CadDocumentViewModel.CreatePrintRequest(DocumentName);
            var snapshot=await new CadDocumentStorage().CreateIndependentSnapshotAsync(editor.Document,
                new(()=>!_disposed && ReferenceEquals(editor,CadDocumentViewModel.CadEditor) && editor.DocumentChangeVersion==version,async ct=>await Task.Delay(1,ct)));
            var printed = await _printService.PrintAsync(
                request with {Document=snapshot},
                onPrintStarted: () => _snackbarService.Enqueue(
                    Strings.PrintStarted,
                    neverConsiderToBeDuplicate: true),
                onBusyChanged: SetPrintBusy,
                onPrintCompleted: () => _snackbarService.Enqueue(
                    Strings.PrintCompleted,
                    neverConsiderToBeDuplicate: true),
                onPrintFinished: result =>
                {
                    if (_disposed) return;
                    if (result.Status == CadPrintCompletionStatus.Failed)
                        _snackbarService.Enqueue($"{Strings.PrintFailed}: {result.Error}", neverConsiderToBeDuplicate: true);
                    else if (result.Status == CadPrintCompletionStatus.Cancelled)
                        _snackbarService.Enqueue(Strings.AiCancelled, neverConsiderToBeDuplicate: true);
                });
            if (printed)
            {
                _snackbarService.Enqueue(
                    Strings.PrintSubmitted,
                    neverConsiderToBeDuplicate: true);
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowOrReplaceMessageDialogAsync(ex.Message, Strings.PrintFailed);
        }
        finally
        {
            progressDialog?.Dispose();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateBlockFromSelection))]
    private async Task CreateBlockFromSelectionAsync()
    {
        var editor = CadDocumentViewModel.CadEditor;
        var entityIds = GetBlockCreationSelection();
        if (entityIds.Length == 0)
            return;

        var document = editor.Document;
        var bounds = entityIds
            .Select(id => document.GetEntity(id).Bounds)
            .Aggregate(CadRectD.Empty, static (current, entityBounds) => current.Union(entityBounds));
        var basePoint = bounds.IsEmpty
            ? document.ViewSettings.Origin.Position
            : bounds.Center;
        var referenceLayer = document.Layers.Values
            .Where(layer => CadEntityAccessPolicy.CanAddToLayer(document, layer.Id))
            .OrderByDescending(layer => layer.Id.Equals(CadDocumentViewModel.DrawingLayerId))
            .ThenByDescending(layer => document.DocumentSettings.LayerDrawingPriority.GetPriority(layer.Id))
            .FirstOrDefault();
        if (referenceLayer is null)
            return;

        var result = await _dialogService.ShowCreateBlockDialogAsync(
            new CreateBlockDialogRequest(
                CreateUniqueBlockName(document),
                basePoint,
                entityIds.Length,
                document.Blocks.Values.Select(block => block.Name).ToArray()));
        if (result is null)
            return;

        try
        {
            var command = editor.CreateBlock(
                entityIds,
                result.Name,
                result.BasePoint,
                referenceLayer.Id);
            if (command.CreatedReferenceId is { } referenceId)
                CadDocumentViewModel.SelectEntities([referenceId]);
        }
        catch (Exception ex)
        {
            _snackbarService.Enqueue(ex.Message, level: CadMessageLevel.Error);
        }
    }

    private bool CanCreateBlockFromSelection()
    {
        return _selectionAvailability.Get(CadDocumentViewModel.CadEditor).CanCreateBlock;
    }

    private EntityId[] GetBlockCreationSelection()
    {
        var editor = CadDocumentViewModel.CadEditor;
        return editor.Selection.EntityIds
            .Where(id =>
                editor.Document.TryGetEntity(id, out var entity) &&
                entity is not null &&
                entity.OwnerBlockId.Equals(editor.ActiveOwnerBlockId) &&
                CadEntityAccessPolicy.IsEditable(editor.Document, entity))
            .ToArray();
    }

    private static string CreateUniqueBlockName(CadDocument document)
    {
        for (var index = 1; ; index++)
        {
            var name = $"Block {index}";
            if (document.Blocks.Values.All(block =>
                    !string.Equals(block.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
        }
    }

    private async Task<bool> SaveToAsync(string? filePath = null, CancellationToken cancellationToken = default,
        bool interactiveConflict = true, CadFileRevision? overwriteAuthorization = null)
    {
        try
        {
            void OnCommitted()
            {
                CurrentFilePath = _saveSession.FilePath;
                SaveWorkspaceSettings();
                RefreshModifiedState();
            }

            var saved = await Operation.RunAsync(Direct2dCad.Lang.CadUiText.Get(Direct2dCad.Lang.LangKeys.SavingDrawing), token => filePath is null
                ? _saveSession.SaveCurrentAsync(OnCommitted, cancellationToken: token, overwriteAuthorization: overwriteAuthorization)
                : _saveSession.SaveAsync(filePath, OnCommitted, cancellationToken: token, overwriteAuthorization: overwriteAuthorization), cancellationToken);
            if (saved && !IsModified) await RemoveRecoveryCopiesAsync();
            return saved;
        }
        catch (CadFileConflictException conflict) when (interactiveConflict && overwriteAuthorization is null)
        {
            var choice=await _dialogService.ShowFileConflictDialogAsync(conflict.CurrentRevision.FullPath);
            if(choice==CadFileConflictChoice.Cancel) return false;
            if(choice==CadFileConflictChoice.SaveAs) return await TrySaveAsFileAsync();
            return await SaveToAsync(filePath, cancellationToken, interactiveConflict: false, overwriteAuthorization: conflict.CurrentRevision);
        }
        catch (CadFileConflictException) when (!interactiveConflict)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            await _dialogService.ShowOrReplaceMessageDialogAsync(ex.Message, "Save failed");
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        CadDocumentViewModel.Undo();
        ApplyDocumentViewSettingsToToolbar();
        LayoutWorkspace.RefreshDocumentStructure();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        CadDocumentViewModel.Redo();
        ApplyDocumentViewSettingsToToolbar();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelection))]
    private void DeleteSelectedEntities()
    {
        CadDocumentViewModel.DeleteSelection();
    }

    [RelayCommand(CanExecute = nameof(CanCopySelection))]
    private void CopySelectedEntities()
    {
        CadDocumentViewModel.CopySelection();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelection))]
    private void CutSelectedEntities()
    {
        if (CadDocumentViewModel.CopySelection() is not null)
            CadDocumentViewModel.DeleteSelection();
    }

    [RelayCommand]
    private void PasteEntities()
    {
        CadDocumentViewModel.BeginClipboardPastePreview();
    }

    [RelayCommand]
    private void SelectAllEntities()
    {
        CadDocumentViewModel.SelectAllEntities();
    }

    [RelayCommand(CanExecute = nameof(CanClearSelection))]
    private void ClearSelection()
    {
        CadDocumentViewModel.ClearSelection();
    }

    [RelayCommand]
    private void CancelCurrentInteraction()
    {
        CadDocumentViewModel.Escape();
    }

    private bool CanDeleteSelection()
    {
        return _selectionAvailability.Get(CadDocumentViewModel.CadEditor).CanDelete;
    }

    private bool CanCopySelection()
    {
        return CadDocumentViewModel.CadEditor.Selection.EntityIds.Count > 0;
    }

    private bool CanClearSelection()
    {
        return CadDocumentViewModel.CadEditor.Selection.EntityIds.Count > 0;
    }

    private bool CanUndo()
    {
        return CadDocumentViewModel.CadEditor.DocumentCommands.CanUndo;
    }

    private bool CanRedo()
    {
        return CadDocumentViewModel.CadEditor.DocumentCommands.CanRedo;
    }

    [RelayCommand]
    private void SetCadCanvasToolMode(string mode_string)
    {
        if (!Enum.TryParse<CadCanvasToolMode>(mode_string, out var mode))
            return;
        CadCanvasToolMode = mode;
        CadDocumentViewModel.SetToolMode(mode);
    }

    [RelayCommand]
    private void ExecuteRadialMenuAction(CadRadialMenuAction action)
    {
        if (CadRadialMenuActionMapper.TryGetToolMode(action, out var toolMode))
        {
            SetCadCanvasToolMode(toolMode.ToString());
            return;
        }

        switch (action)
        {
            case CadRadialMenuAction.Undo:
                if (UndoCommand.CanExecute(null))
                    UndoCommand.Execute(null);
                break;
            case CadRadialMenuAction.Redo:
                if (RedoCommand.CanExecute(null))
                    RedoCommand.Execute(null);
                break;
            case CadRadialMenuAction.CopySelection:
                if (CopySelectedEntitiesCommand.CanExecute(null))
                    CopySelectedEntitiesCommand.Execute(null);
                break;
            case CadRadialMenuAction.CutSelection:
                if (CutSelectedEntitiesCommand.CanExecute(null))
                    CutSelectedEntitiesCommand.Execute(null);
                break;
            case CadRadialMenuAction.Paste:
                PasteEntitiesCommand.Execute(null);
                break;
            case CadRadialMenuAction.DeleteSelection:
                if (DeleteSelectedEntitiesCommand.CanExecute(null))
                    DeleteSelectedEntitiesCommand.Execute(null);
                break;
            case CadRadialMenuAction.SelectAll:
                SelectAllEntitiesCommand.Execute(null);
                break;
            case CadRadialMenuAction.ClearSelection:
                if (ClearSelectionCommand.CanExecute(null))
                    ClearSelectionCommand.Execute(null);
                break;
            case CadRadialMenuAction.CancelCurrentInteraction:
                CancelCurrentInteractionCommand.Execute(null);
                break;
            case CadRadialMenuAction.FitToWindow:
                FitToWindowCommand.Execute(null);
                break;
            case CadRadialMenuAction.Save:
                SaveFileCommand.Execute(null);
                break;
            case CadRadialMenuAction.SaveAs:
                SaveAsFileCommand.Execute(null);
                break;
            case CadRadialMenuAction.CreateBlock:
                if (CreateBlockFromSelectionCommand.CanExecute(null))
                    CreateBlockFromSelectionCommand.Execute(null);
                break;
        }
    }


    [RelayCommand]
    public void FitToWindow()
    {
        CadDocumentViewModel.FitToWindow();
    }

    public bool TryRenameDocument(string name)
    {
        if (IsCompatibilityReadOnly) return false;
        if (!TryNormalizeDocumentName(name, out var normalizedName))
            return false;

        if (CadDocumentViewModel.CadEditor.Document.Name == normalizedName)
            return true;

        CadDocumentViewModel.CadEditor.Document.Rename(normalizedName);
        ToolTip = Title = normalizedName;
        MarkDirectDocumentChanged();
        OnPropertyChanged(nameof(DocumentName));
        PublishDocumentSummaryChanged();
        return true;
    }

    private void ApplyUserSettingsToToolbar()
    {
        _isSyncingUserSettings = true;
        try
        {
            IsAntialiasingEnabled = _userSettings.Rendering.IsAntialiasingEnabled;
            IsTextAntialiasingEnabled = _userSettings.Rendering.IsTextAntialiasingEnabled;
            SelectedEntityColorText = FormatColor(_userSettings.Interaction.SelectedEntityStrokeColor);
            SelectionWindowStrokeColorText = FormatColor(_userSettings.Interaction.SelectionWindowStrokeColor);
            SelectionWindowFillColorText = FormatColor(_userSettings.Interaction.SelectionWindowFillColor);
            SelectionCrossingStrokeColorText = FormatColor(_userSettings.Interaction.SelectionCrossingStrokeColor);
            SelectionCrossingFillColorText = FormatColor(_userSettings.Interaction.SelectionCrossingFillColor);
        }
        finally
        {
            _isSyncingUserSettings = false;
        }
    }

    private void ApplyUserSettingsFromToolbar()
    {
        if (!TryParseColor(SelectedEntityColorText, out var selectedEntityColor) ||
            !TryParseColor(SelectionWindowStrokeColorText, out var selectionWindowStrokeColor) ||
            !TryParseColor(SelectionWindowFillColorText, out var selectionWindowFillColor) ||
            !TryParseColor(SelectionCrossingStrokeColorText, out var selectionCrossingStrokeColor) ||
            !TryParseColor(SelectionCrossingFillColorText, out var selectionCrossingFillColor))
        {
            return;
        }

        _userSettings.Rendering.IsAntialiasingEnabled = IsAntialiasingEnabled;
        _userSettings.Rendering.IsTextAntialiasingEnabled = IsTextAntialiasingEnabled;
        _userSettings.Interaction.SelectedEntityStrokeColor = selectedEntityColor;
        _userSettings.Interaction.SelectionWindowStrokeColor = selectionWindowStrokeColor;
        _userSettings.Interaction.SelectionWindowFillColor = selectionWindowFillColor;
        _userSettings.Interaction.SelectionCrossingStrokeColor = selectionCrossingStrokeColor;
        _userSettings.Interaction.SelectionCrossingFillColor = selectionCrossingFillColor;
        _userSettings.Normalize();

        CadDocumentViewModel.ApplyUserSettings(_userSettings);
        SaveUserSettings();
    }

    private void SaveUserSettings()
    {
        try
        {
            _userSettingsStore.Save(_userSettings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = _dialogService.ShowOrReplaceMessageDialogAsync(ex.Message, "User settings save failed");
        }
    }

    private void ApplyDocumentViewSettingsToToolbar()
    {
        _isSyncingViewSettings = true;
        try
        {
            var grid = CadDocumentViewModel.CadEditor.Document.ViewSettings.Grid;
            ViewModelCadUnit = (ViewModelCadUnit)CadDocumentViewModel.CadEditor.Document.DocumentSettings.Unit;
            ViewModelCadGridType = (ViewModelCadGridType)grid.Type;
            ViewModelCadSnapMarkerType = (ViewModelCadSnapMarkerType)grid.SnapMarkerType;
            GridSpacingPresets.Clear();
            foreach (var preset in grid.SpacingPresets)
                GridSpacingPresets.Add(GridSpacingPresetItemViewModel.From(
                    preset,
                    CadDocumentViewModel.CadEditor.Document.DocumentSettings.Unit));
            GridSpacingPresets.Add(GridSpacingPresetItemViewModel.CreateGridSettingsAction());
            SelectedMajorGridSpacingPreset = FindGridSpacingPreset(grid.MajorSpacingPresetId, grid.SpacingX, grid.SpacingY);
            SelectedMinorGridSpacingPreset = FindGridSpacingPreset(
                grid.MinorSpacingPresetId,
                grid.GetMinorSpacingX(),
                grid.GetMinorSpacingY());
            ViewModelCadOriginDisplayType = (ViewModelCadOriginDisplayType)CadDocumentViewModel.CadEditor.Document.ViewSettings.Origin.DisplayType;
            ViewModelCadOriginMarkerType = (ViewModelCadOriginMarkerType)CadDocumentViewModel.CadEditor.Document.ViewSettings.Origin.MarkerType;
            ViewModelCadOriginLinePattern = (ViewModelCadOriginLinePattern)CadDocumentViewModel.CadEditor.Document.ViewSettings.Origin.LinePattern;
            ViewModelCadOriginSize = CadDocumentViewModel.CadEditor.Document.ViewSettings.Origin.Size;
            ViewModelCadOriginStrokeWidth = CadDocumentViewModel.CadEditor.Document.ViewSettings.Origin.StrokeWidth;
            ViewModelCadOriginColorText = FormatColor(CadDocumentViewModel.CadEditor.Document.ViewSettings.Origin.Color);
            ViewModelCadBackgroundColor = CadDocumentViewModel.CadEditor.Document.ViewSettings.BackgroundColor;
            var originPosition = CadDocumentViewModel.CadEditor.Document.ViewSettings.Origin.Position;
            var documentUnit = CadDocumentViewModel.CadEditor.Document.DocumentSettings.Unit;
            ViewModelCadOriginX = CadUnitConversion.FromMillimeters(originPosition.X, documentUnit);
            ViewModelCadOriginY = CadUnitConversion.FromMillimeters(originPosition.Y, documentUnit);
        }
        finally
        {
            _isSyncingViewSettings = false;
        }
    }

    private void ApplyGridSpacingPresetsFromToolbar()
    {
        if (_isSyncingViewSettings ||
            SelectedMajorGridSpacingPreset is null ||
            SelectedMinorGridSpacingPreset is null)
        {
            return;
        }

        if (SelectedMajorGridSpacingPreset.OpensGridSettings ||
            SelectedMinorGridSpacingPreset.OpensGridSettings)
        {
            OpenGridSettingsDialog();
            ApplyDocumentViewSettingsToToolbar();
            return;
        }

        if (!CadDocumentViewModel.CadEditor.TrySetGridSpacingPresets(
                SelectedMajorGridSpacingPreset.Id,
                SelectedMinorGridSpacingPreset.Id))
        {
            ApplyDocumentViewSettingsToToolbar();
        }
    }

    private void OpenGridSettingsDialog()
    {
        var viewModel = new DocumentSettingsViewModel(this, _dialogService);
        viewModel.SelectedSection = viewModel.GridAndSnapping;
        _dialogService.ShowDocumentSettingsDialog(viewModel);
    }

    private GridSpacingPresetItemViewModel? FindGridSpacingPreset(
        Guid? id,
        double spacingX,
        double spacingY)
    {
        return (id is null
                ? null
                : GridSpacingPresets.FirstOrDefault(item => !item.OpensGridSettings && item.Id == id.Value))
            ?? GridSpacingPresets.FirstOrDefault(item =>
                !item.OpensGridSettings &&
                NearlyEqual(item.SpacingX, spacingX) && NearlyEqual(item.SpacingY, spacingY));
    }

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= Math.Max(1.0, Math.Max(Math.Abs(left), Math.Abs(right))) * 1e-9;

    private void ApplyOriginSettingsFromToolbar()
    {
        if (!TryParseColor(ViewModelCadOriginColorText, out var color))
            return;

        if (!IsPositiveFinite(ViewModelCadOriginSize) ||
            !IsPositiveFinite(ViewModelCadOriginStrokeWidth))
        {
            return;
        }

        CadDocumentViewModel.CadEditor.SetOriginSettings(
            (CadOriginDisplayType)ViewModelCadOriginDisplayType,
            (CadOriginMarkerType)ViewModelCadOriginMarkerType,
            (CadOriginLinePattern)ViewModelCadOriginLinePattern,
            color,
            ViewModelCadOriginSize,
            ViewModelCadOriginStrokeWidth);
    }

    private void ApplyOriginPositionFromToolbar()
    {
        var unit = CadDocumentViewModel.CadEditor.Document.DocumentSettings.Unit;
        var x = CadUnitConversion.ToMillimeters(ViewModelCadOriginX, unit);
        var y = CadUnitConversion.ToMillimeters(ViewModelCadOriginY, unit);
        if (!IsFinite(x) || !IsFinite(y))
        {
            ApplyDocumentViewSettingsToToolbar();
            return;
        }

        CadDocumentViewModel.CadEditor.SetOriginPosition(new CadPointD(x, y));
    }

    public void ApplyUserSettings(CadUserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _userSettings.CopyFrom(settings);
        CadDocumentViewModel.ApplyUserSettings(_userSettings);
        ApplyUserSettingsToToolbar();
        NotifyBooleanCommands();
    }

    public void ApplyDocumentViewSettings(CadViewSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        CadDocumentViewModel.CadEditor.SetViewSettings(settings);
        ApplyDocumentViewSettingsToToolbar();
        LayoutWorkspace.RefreshDocumentStructure();
    }

    private void ApplyBackgroundColorFromToolbar()
    {
        if (CadDocumentViewModel.CadEditor.Document.ViewSettings.BackgroundColor == ViewModelCadBackgroundColor)
            return;

        CadDocumentViewModel.SetBackgroundColor(ViewModelCadBackgroundColor);
    }

    private static string FormatColor(CadColor color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static bool TryParseColor(string? text, out CadColor color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var hex = text.Trim();
        if (hex.StartsWith('#'))
            hex = hex[1..];

        if (hex.Length == 6)
            hex = "FF" + hex;

        if (hex.Length != 8)
            return false;

        try
        {
            color = CadColor.FromArgb(
                Convert.ToByte(hex[0..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16),
                Convert.ToByte(hex[6..8], 16));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool IsPositiveFinite(double value)
    {
        return value > 0 && IsFinite(value);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    internal void OwnDocumentScope(IDisposable scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_documentScope is not null)
            throw new InvalidOperationException("The document already owns a service scope.");
        _documentScope = scope;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            Operation.Dispose();
            _recoveryLifetime.Cancel();
            if (!IsModified || _discardRecoveryOnClose) _ = RemoveRecoveryCopiesAsync();
            _recoveryLifetime.Dispose();
            _saveSession.Dispose();
            SaveWorkspaceSettings();
            SaveUserSettings();
        }
        finally
        {
            try
            {
                DetachDocumentChangeTracking();
                CadDocumentViewModel.PropertyChanged -= OnCadDocumentViewModelPropertyChanged;
                _viewSettingsChangedSubscription.Dispose();
                _selectionFilterChangedSubscription.Dispose();
                _interactionStateChangedSubscription.Dispose();
                CadDocumentViewModel.Dispose();
            }
            finally
            {
                // Scope disposal calls Dispose on this tab again; _disposed makes that safe.
                Interlocked.Exchange(ref _documentScope, null)?.Dispose();
            }
        }
    }

    private void OnCadDocumentViewSettingsChanged(CadDocumentViewSettingsChangedMessage message)
    {
        if (!ReferenceEquals(message.DocumentViewModel, CadDocumentViewModel))
            return;

        ApplyDocumentViewSettingsToToolbar();
    }

    private void OnSelectionFilterChanged(CadSelectionFilterChangedMessage message)
    {
        if (!ReferenceEquals(message.DocumentViewModel, CadDocumentViewModel) ||
            _isRestoringWorkspaceSettings)
        {
            return;
        }

        SaveWorkspaceSettings();
    }

    private void OnInteractionStateChanged(CadDocumentInteractionStateChangedMessage message)
    {
        if (!ReferenceEquals(message.DocumentViewModel, CadDocumentViewModel))
            return;

        NotifyBooleanCommands();
        _selectionAvailability.Get(CadDocumentViewModel.CadEditor);
        if (_notifiedSelectionAvailabilityVersion != _selectionAvailability.Version)
        {
            _notifiedSelectionAvailabilityVersion = _selectionAvailability.Version;
            CreateBlockFromSelectionCommand.NotifyCanExecuteChanged();
            DeleteSelectedEntitiesCommand.NotifyCanExecuteChanged();
            CopySelectedEntitiesCommand.NotifyCanExecuteChanged();
            CutSelectedEntitiesCommand.NotifyCanExecuteChanged();
            ClearSelectionCommand.NotifyCanExecuteChanged();
        }
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void OnCadDocumentViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CadDocumentViewModel.DocumentUnit) or
            nameof(CadDocumentViewModel.DocumentLengthPrecision) or
            nameof(CadDocumentViewModel.DocumentAnglePrecision))
        {
            MarkDirectDocumentChanged();
            return;
        }

        if (e.PropertyName == nameof(CadDocumentViewModel.CadCanvasToolMode))
        {
            CadCanvasToolMode = CadDocumentViewModel.CadCanvasToolMode;
            if (CadCanvasToolMode == CadCanvasToolMode.LayoutViewport)
                LayoutWorkspace.IsSettingsOpen = false;
            return;
        }

        if (e.PropertyName is nameof(CadDocumentViewModel.ActiveLayoutId) or
            nameof(CadDocumentViewModel.ActiveLayoutViewportId))
        {
            PrintCommand.NotifyCanExecuteChanged();
            LayoutWorkspace.RefreshDocumentStructure();
            return;
        }

        if (e.PropertyName == nameof(CadDocumentViewModel.CadEditor))
        {
            _saveSession.Reset(CadDocumentViewModel.CadEditor, "");
            CurrentFilePath = "";
            AttachDocumentChangeTracking(CadDocumentViewModel.CadEditor);
            ApplyDocumentViewSettingsToToolbar();
            CreateBlockFromSelectionCommand.NotifyCanExecuteChanged();
            DeleteSelectedEntitiesCommand.NotifyCanExecuteChanged();
            CopySelectedEntitiesCommand.NotifyCanExecuteChanged();
            CutSelectedEntitiesCommand.NotifyCanExecuteChanged();
            ClearSelectionCommand.NotifyCanExecuteChanged();
            PrintCommand.NotifyCanExecuteChanged();
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        }
    }

    private void AttachDocumentChangeTracking(CadEditor editor)
    {
        if (ReferenceEquals(_trackedEditor, editor))
            return;

        DetachDocumentChangeTracking();
        _trackedEditor = editor;
        _trackedEditor.DocumentChanged += OnEditorDocumentChanged;
    }

    private void DetachDocumentChangeTracking()
    {
        if (_trackedEditor is null)
            return;

        _trackedEditor.DocumentChanged -= OnEditorDocumentChanged;
        _trackedEditor = null;
    }

    private void OnEditorDocumentChanged(object? sender, CadDocumentChangeSet e)
    {
        if (e.AffectsDocumentStructure || e.AffectsLayoutStructure)
            LayoutWorkspace.HandleDocumentStructureChanged();
        else if (e.AffectsLayouts)
            LayoutWorkspace.HandleLayoutSettingsChanged();
        RefreshModifiedState();
        NotifyBooleanCommands();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void MarkDirectDocumentChanged()
    {
        _saveSession.MarkDirectChange();
        RefreshModifiedState();
    }

    private void RefreshModifiedState()
    {
        IsModified = _saveSession.IsModified;
        PublishDocumentSummaryChanged();
    }

    internal void Load(CadDocument document, string fileName)
    {
        ArgumentNullException.ThrowIfNull(document);
        CadDocumentViewModel.ReplaceEditor(new CadEditor(document));
        CadDocumentViewModel.ActivateModelSpace();
        CadDocumentViewModel.FitToWindow();
        LayoutWorkspace.RefreshDocumentStructure();
        CurrentFilePath = fileName;
        _saveSession.Reset(CadDocumentViewModel.CadEditor, fileName);
        OnPropertyChanged(nameof(IsCompatibilityReadOnly));
        RestoreWorkspaceSettings();
        Title = CadDocumentViewModel.CadEditor.Document.Name;
        RefreshModifiedState();
        OnPropertyChanged(nameof(DocumentName));
        PublishDocumentSummaryChanged();
    }

    private void PublishDocumentSummaryChanged()
    {
        _documentSummaryChangedPublisher.Publish(new EditorTabDocumentSummaryChangedMessage(this));
    }

    private void RestoreWorkspaceSettings()
    {
        if (string.IsNullOrWhiteSpace(CurrentFilePath))
            return;

        _isRestoringWorkspaceSettings = true;
        try
        {
            var settings = _workspaceSettingsStore.LoadDocument(CurrentFilePath);
            CadDocumentViewModel.ApplyDisabledSelectionEntityTypeKeys(
                settings.DisabledSelectionEntityTypes);
        }
        finally
        {
            _isRestoringWorkspaceSettings = false;
        }
    }

    private void SaveWorkspaceSettings()
    {
        if (string.IsNullOrWhiteSpace(CurrentFilePath))
            return;

        try
        {
            _workspaceSettingsStore.SaveDocument(
                CurrentFilePath,
                new CadDocumentWorkspaceSettings
                {
                    DisabledSelectionEntityTypes = new HashSet<string>(
                        CadDocumentViewModel.GetDisabledSelectionEntityTypeKeys(),
                        StringComparer.Ordinal)
                });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = _dialogService.ShowOrReplaceMessageDialogAsync(
                ex.Message,
                "Workspace settings save failed");
        }
    }

    private static bool TryNormalizeDocumentName(string? name, out string normalizedName)
    {
        normalizedName = name?.Trim() ?? string.Empty;
        return normalizedName.Length > 0 &&
               normalizedName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }
}
