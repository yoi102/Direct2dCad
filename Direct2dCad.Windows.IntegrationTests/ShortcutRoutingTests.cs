using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;
using Direct2dCad.Application.Tools;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.CommandLine;
using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.ViewModels;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Interactions;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Platform.Notifications;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.ViewModels.Toolboxes.EntityProperty;
using Direct2dCad.wpf;
using Direct2dCad.wpf.Controls;
using Direct2dCad.wpf.Services.Input;
using Direct2dCad.wpf.Views;
using Direct2dCad.wpf.Views.Toolboxes;
using Direct2dCad.wpf.Views.Toolboxes.EntityProperty;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.Windows.IntegrationTests;

[Collection(WpfBindingDiagnosticsCollection.Name)]
public sealed partial class ShortcutRoutingTests
{
    [Fact]
    public void GesturesAreUnambiguousAndDocumentNavigationIsNotShadowed() => RunSta(() =>
    {
        Assert.All(CadShortcutCatalog.All.GroupBy(s => (s.Scope, s.Key, s.Modifiers)), g => Assert.Single(g));
        Assert.DoesNotContain(CadShortcutCatalog.All, s => s.Key == Key.Tab && s.Modifiers.HasFlag(ModifierKeys.Control));
        foreach (var action in new[] { CadShortcutAction.Copy, CadShortcutAction.Cut, CadShortcutAction.Paste,
            CadShortcutAction.Undo, CadShortcutAction.Redo, CadShortcutAction.SelectAll })
            Assert.DoesNotContain(CadShortcutCatalog.All, s => s.Scope == CadShortcutScope.Application && s.Action == action);
        foreach (var text in new[] { CadShortcutCatalog.NewGesture, CadShortcutCatalog.OpenGesture,
            CadShortcutCatalog.SaveGesture, CadShortcutCatalog.SaveAsGesture, CadShortcutCatalog.PrintGesture })
        {
            var gesture = Assert.IsType<KeyGesture>(new KeyGestureConverter().ConvertFromInvariantString(text));
            Assert.NotNull(CadShortcutCatalog.Find(CadShortcutScope.Application, gesture.Key, gesture.Modifiers));
        }
    });

    [Theory]
    [InlineData(Key.S, ModifierKeys.Control)]
    [InlineData(Key.S, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.P, ModifierKeys.Control)]
    public void FileCommandsReadTheValidatedPropertyBeforeExecuting(Key key, ModifierKeys modifiers) => RunSta(() =>
    {
        var model = new PropertyModel();
        var section = new EntityHeaderPropertySection { ViewModel = model };
        var text = Descendants(section).OfType<TextBox>().Single(b => !b.IsReadOnly);
        Flush(); text.SetCurrentValue(TextBox.TextProperty, "Edited without losing focus");
        Assert.Equal("Original", model.EntityName);
        var command = new RecordingCommand(() => Assert.Equal("Edited without losing focus", model.EntityName));
        var owner = new Window();
        using var router = new CadWindowShortcutRouter(owner, _ => command, () => false, MainWindow.TryCommitFocusedPropertyEdit);
        Assert.True(router.Process(key, modifiers, text));
        Assert.Equal(1, command.Calls);
        Assert.Equal("Edited without losing focus", text.Text);
        owner.Close();
    });

    [Theory]
    [InlineData(Key.S, ModifierKeys.Control)]
    [InlineData(Key.S, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.P, ModifierKeys.Control)]
    public void InvalidPropertyBlocksFileCommandAndPreservesTheInput(Key key, ModifierKeys modifiers) => RunSta(() =>
    {
        var model = new PropertyModel();
        var section = new EntitySettingsPropertySection { ViewModel = model };
        var text = Descendants(section).OfType<TextBox>().Single(control => !control.IsReadOnly);
        Flush(); text.SetCurrentValue(TextBox.TextProperty, "invalid angle");
        var command = new RecordingCommand(); var owner = new Window();
        using var router = new CadWindowShortcutRouter(owner, _ => command, () => false, MainWindow.TryCommitFocusedPropertyEdit);
        Assert.True(router.Process(key, modifiers, text));
        Assert.Equal(0, command.Calls); Assert.Equal("invalid angle", text.Text);
        Assert.Equal(12, model.GeometryRotationDegrees); Assert.True(Validation.GetHasError(text));
        owner.Close();
    });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualFloatingCadWindowsResolveTheOwnerRouterAndDetachWithIt(bool documentWindow) => RunSta(() =>
    {
        var manager = new ToggleDockingManager { Layout = new LayoutRoot() };
        var owner = new Window { Content = manager }; _ = new WindowInteropHelper(owner).EnsureHandle();
        LayoutFloatingWindow model;
        Type windowType;
        if (documentWindow)
        {
            var pane = new LayoutDocumentPane(); pane.Children.Add(new LayoutDocument { Content = new TextBox() });
            var group = new LayoutDocumentPaneGroup(); group.Children.Add(pane);
            var floating = new LayoutDocumentFloatingWindow { RootPanel = group };
            model = floating; windowType = typeof(LayoutDocumentFloatingWindowControl);
        }
        else
        {
            var pane = new LayoutAnchorablePane(); pane.Children.Add(new LayoutAnchorable { Content = new TextBox() });
            var group = new LayoutAnchorablePaneGroup(); group.Children.Add(pane);
            model = new LayoutAnchorableFloatingWindow { RootPanel = group };
            windowType = typeof(LayoutAnchorableFloatingWindowControl);
        }
        manager.Layout.FloatingWindows.Add(model);
        var floatingWindow = Assert.IsAssignableFrom<LayoutFloatingWindowControl>(Activator.CreateInstance(windowType,
            BindingFlags.Instance | BindingFlags.NonPublic, null, [model], null));
        var command = new RecordingCommand();
        var router = new CadWindowShortcutRouter(owner, _ => command, () => false, MainWindow.TryCommitFocusedPropertyEdit);
        Assert.Same(router, CadWindowShortcutRouter.Resolve(owner));
        Assert.Same(router, CadWindowShortcutRouter.Resolve(floatingWindow));
        var draft = new TextBox { Text = "unsubmitted draft" };
        Assert.True(CadWindowShortcutRouter.Resolve(floatingWindow)!.Process(Key.S, ModifierKeys.Control, draft));
        Assert.Equal(1, command.Calls); Assert.Equal("unsubmitted draft", draft.Text);
        var dialog = new Window { Owner = owner };
        Assert.Null(CadWindowShortcutRouter.Resolve(dialog)); dialog.Close();
        router.Dispose();
        Assert.Null(CadWindowShortcutRouter.Resolve(owner)); Assert.Null(CadWindowShortcutRouter.Resolve(floatingWindow));
        floatingWindow.Close(); owner.Close();
    });

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void ApplicationCommandsRespectDisabledAndModalStateBeforeCommittingProperties(bool modal, bool enabled) => RunSta(() =>
    {
        var model = new PropertyModel();
        var section = new EntityHeaderPropertySection { ViewModel = model };
        var draft = Descendants(section).OfType<TextBox>().Single(b => !b.IsReadOnly);
        Flush(); draft.SetCurrentValue(TextBox.TextProperty, "draft retained");
        var command = new RecordingCommand { Enabled = enabled }; var owner = new Window();
        using var router = new CadWindowShortcutRouter(owner, _ => command, () => modal, MainWindow.TryCommitFocusedPropertyEdit);
        Assert.True(router.Process(Key.S, ModifierKeys.Control, draft));
        Assert.Equal(modal || !enabled ? 0 : 1, command.Calls);
        Assert.Equal(modal || !enabled ? "Original" : "draft retained", model.EntityName);
        Assert.Equal("draft retained", draft.Text);
        owner.Close();
    });

    [Theory]
    [InlineData(Key.S, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.S, ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)]
    [InlineData(Key.O, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.N, ModifierKeys.None)]
    [InlineData(Key.Z, ModifierKeys.Control)]
    [InlineData(Key.C, ModifierKeys.Control)]
    [InlineData(Key.X, ModifierKeys.Control)]
    [InlineData(Key.V, ModifierKeys.Control)]
    public void ApplicationRouterLeavesTextGesturesAndUnassignedModifiersAlone(Key key, ModifierKeys modifiers) => RunSta(() =>
    {
        var owner = new Window(); var command = new RecordingCommand(); var text = new TextBox { Text = "selected text" };
        using var router = new CadWindowShortcutRouter(owner, _ => command, () => false, _ => throw new Exception("Unexpected commit"));
        Assert.False(router.Process(key, modifiers, text)); Assert.Equal(0, command.Calls); Assert.Equal("selected text", text.Text);
        owner.Close();
    });

    [Fact]
    public void CanvasUndoThenControlShiftZRestoresTheEntity() => RunSta(() =>
    {
        using var context = new Context(); using var canvas = new CadCanvas { DocumentViewModel = context.Document };
        var line = context.Document.CadEditor.AddLine(default, new(20, 0));
        var undo = KeyEvent(Key.Z); canvas.HandleCanvasKey(Key.Z, ModifierKeys.Control, undo);
        Assert.True(undo.Handled); Assert.True(context.Document.CadEditor.Document.GetEntity(line).IsErased);
        var redo = KeyEvent(Key.Z); canvas.HandleCanvasKey(Key.Z, ModifierKeys.Control | ModifierKeys.Shift, redo);
        Assert.True(redo.Handled); Assert.False(context.Document.CadEditor.Document.GetEntity(line).IsErased);
    });

    [Theory]
    [InlineData(Key.Z, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.Z, ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)]
    [InlineData(Key.Delete, ModifierKeys.Control)]
    [InlineData(Key.Delete, ModifierKeys.Shift)]
    [InlineData(Key.Delete, ModifierKeys.Alt)]
    [InlineData(Key.Enter, ModifierKeys.Control)]
    [InlineData(Key.A, ModifierKeys.Alt)]
    public void UnassignedCanvasGesturesCannotDeleteUndoOrFinishDrawing(Key key, ModifierKeys modifiers) => RunSta(() =>
    {
        using var context = new Context(); using var canvas = new CadCanvas { DocumentViewModel = context.Document };
        var line = context.Document.CadEditor.AddLine(default, new(20, 0)); context.Document.SelectEntities([line]);
        context.Document.SetToolMode(CadCanvasToolMode.Line);
        var history = context.Document.CadEditor.CreateDocumentHistorySnapshot();
        var e = KeyEvent(key); canvas.HandleCanvasKey(key, modifiers, e);
        Assert.False(e.Handled); Assert.False(context.Document.CadEditor.Document.GetEntity(line).IsErased);
        Assert.Equal(CadCanvasToolMode.Line, context.Document.CadCanvasToolMode);
        Assert.True(context.Document.CadEditor.DocumentHistoryEquals(history));
    });

    [Theory]
    [InlineData(Key.Tab, ModifierKeys.Shift)]
    [InlineData(Key.Tab, ModifierKeys.Control)]
    [InlineData(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.Enter, ModifierKeys.Control)]
    [InlineData(Key.Enter, ModifierKeys.Alt)]
    [InlineData(Key.Up, ModifierKeys.Control)]
    [InlineData(Key.Down, ModifierKeys.Shift)]
    [InlineData(Key.Escape, ModifierKeys.Control)]
    public void TerminalModifiedKeysPreserveDraftSuggestionsAndDrawing(Key key, ModifierKeys modifiers) => RunSta(() =>
    {
        using var context = new Context(); using var terminal = context.CreateTerminal();
        context.Document.SetToolMode(CadCanvasToolMode.Line); terminal.CommandText = "CIR";
        Assert.True(terminal.HasSuggestions); var suggestions = terminal.Suggestions.ToArray();
        Assert.False(CommandLineToolboxView.HandleTerminalKey(terminal, key, modifiers,
            () => throw new Exception("Caret moved"), _ => throw new Exception("Suggestion moved")));
        Assert.Equal("CIR", terminal.CommandText); Assert.Equal(suggestions, terminal.Suggestions);
        Assert.Equal(CadCanvasToolMode.Line, context.Document.CadCanvasToolMode);
    });

    [Fact]
    public void TerminalTabCompletesOnlyWhenSuggestionsExistAndEscapeCancelsInStages() => RunSta(() =>
    {
        using var context = new Context(); using var terminal = context.CreateTerminal();
        bool Handle(Key key) => CommandLineToolboxView.HandleTerminalKey(terminal, key, ModifierKeys.None, () => { }, _ => { });
        Assert.False(Handle(Key.Tab));
        terminal.CommandText = "CIR"; Assert.True(Handle(Key.Tab)); Assert.StartsWith("CIRCLE ", terminal.CommandText);
        context.Document.SetToolMode(CadCanvasToolMode.Line); terminal.CommandText = "CIR";
        Assert.True(Handle(Key.Escape)); Assert.False(terminal.HasSuggestions); Assert.Equal("CIR", terminal.CommandText);
        Assert.Equal(CadCanvasToolMode.Line, context.Document.CadCanvasToolMode);
        Assert.False(Handle(Key.Escape)); Assert.Equal("CIR", terminal.CommandText);
        var owner = new Window();
        using var router = new CadWindowShortcutRouter(owner, _ => null, () => false, _ => true,
            focused => CadEscapeHandler.Process(owner, context.Document, focused));
        Assert.True(router.ProcessEscape(Key.Escape, ModifierKeys.None, null));
        Assert.Equal("CIR", terminal.CommandText);
        Assert.Equal(CadCanvasToolMode.Select, context.Document.CadCanvasToolMode);
        owner.Close();
    });

    [Fact]
    public void F4CyclesActualSnapCandidatesAndControlTabIsLeftToDocumentNavigation() => RunSta(() =>
    {
        using var context = new Context();
        context.Document.CadEditor.AddLine(new(0, 0), new(3, 0));
        context.Document.CadEditor.AddLine(new(0, .1), new(3, .1));
        context.Document.CadEditor.Viewport.SetView(2, default);
        context.Document.SetToolMode(CadCanvasToolMode.Line);
        context.Document.PointerMove(context.Document.CadEditor.Viewport.WorldToScreen(default));
        Assert.True(context.Document.HasSnapCandidates);
        var controller = typeof(CadDocumentViewModel).GetField("_objectSnap", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(context.Document)!;
        CadPointD CurrentPoint()
        {
            var current = controller.GetType().GetProperty("Current")!.GetValue(controller)!;
            return (CadPointD)current.GetType().GetProperty("Point")!.GetValue(current)!;
        }
        var before = CurrentPoint();
        Assert.False(CadDocumentView.HandleSnapCandidateKey(context.Document, Key.Tab, ModifierKeys.Control));
        Assert.Equal(before, CurrentPoint());
        Assert.True(CadDocumentView.HandleSnapCandidateKey(context.Document, Key.F4, ModifierKeys.None));
        Assert.NotEqual(before, CurrentPoint());
    });

    private static KeyEventArgs KeyEvent(Key key) => new(Keyboard.PrimaryDevice, new EventSource(), 0, key)
        { RoutedEvent = Keyboard.KeyDownEvent };

    private sealed class EventSource : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void RunSta(Action action) => WpfTestDispatcher.Run(action);
    private sealed class RecordingCommand(Action? execute = null) : ICommand
    {
        public int Calls { get; private set; }
        public bool Enabled { get; init; } = true;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => Enabled;
        public void Execute(object? parameter) { Calls++; execute?.Invoke(); }
    }
    private sealed class PropertyModel : IEntityHeaderPropertySectionViewModel, IEntitySettingsPropertySectionViewModel
    {
        public string EntityIdText => "1";
        public string EntityName { get; set; } = "Original";
        public IReadOnlyList<EntityLayerOption> LayerOptions => [];
        public EntityLayerOption? SelectedLayerOption { get; set; }
        public int ZIndex { get; set; }
        public bool IsVisible { get; set; } = true;
        public bool SupportsGeometryOrientation => true;
        public double GeometryRotationDegrees { get; set; } = 12;
    }
    private sealed class Context : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly PlatformStub _platform = new();
        public CadDocumentViewModel Document { get; }
        public Context()
        {
            var services = new ServiceCollection(); services.AddMessagePipe();
            services.AddSingleton<ICadRenderSessionFactory>(new Direct2DRenderSessionFactory(new CadRenderResourceBudget()));
            services.AddSingleton<IImageImportService>(_platform); services.AddSingleton<IClipboardTextService>(_platform);
            services.AddSingleton<IOleHostService>(_platform); services.AddSingleton<ISnackbarService>(_platform);
            _services = services.BuildServiceProvider();
            Document = ActivatorUtilities.CreateInstance<CadDocumentViewModel>(_services, new CadClipboardStore());
            Document.SetViewportSize(800, 600);
        }
        public CommandLineToolboxViewModel CreateTerminal()
        {
            var terminal = new CommandLineToolboxViewModel(_platform, _platform, new CadCommandLineService(), new NoTools(),
                _services.GetRequiredService<IAsyncSubscriber<CadCommandActivityMessage>>(),
                _services.GetRequiredService<IAsyncSubscriber<CadInteractionActivityMessage>>());
            terminal.Attach(Document); return terminal;
        }
        public void Dispose() { Document.Dispose(); _services.Dispose(); }
    }
    private sealed class NoTools : ICadToolCommandLineService
    {
        public Task<CadToolCommandLineExecution?> TryExecuteAsync(string commandLine, CancellationToken token = default) => Task.FromResult<CadToolCommandLineExecution?>(null);
        public IReadOnlyList<string> Complete(string commandText, int maximumCount = 12) => [];
    }
    private sealed class PlatformStub : IImageImportService, IClipboardTextService, IOleHostService, ISnackbarService,
        IToolboxIconProvider, IToolboxLayoutSettingsStore
    {
        public CadToolboxState? Load(string contentId) => null;
        public void Save(IEnumerable<KeyValuePair<string, CadToolboxState>> toolboxes) { }
        public object Explorer => ""; public object Layers => ""; public object Blocks => ""; public object Terminal => "";
        public object Search => ""; public object Filter => ""; public object Git => ""; public object Problems => "";
        public object Assistant => ""; public object Messages => "";
        public CadImageImportData LoadFromFile(string filePath) => throw new NotSupportedException();
        CadImageImportData? IImageImportService.LoadFromClipboard() => null;
        public string CreatePngDataUrl(CadImageImportData image) => throw new NotSupportedException();
        string? IClipboardTextService.LoadFromClipboard() => null;
        CadOleImportData? IOleHostService.LoadFromClipboard() => null;
        public CadOleDrawData? DrawOleObject(Guid sessionId, CadOleDrawRequest request) => null;
        public void BeginEdit(Guid sessionId, EntityId entityId, byte[] oleBytes, string objectName) { }
        public void EndEditSession(Guid sessionId, EntityId entityId) { }
        public void EndEditSessions(Guid sessionId) { }
        public void ReleaseRenderSession(Guid sessionId, EntityId entityId) { }
        public void ReleaseTransientRenderSession(Guid sessionId, Guid renderId) { }
        public void ReleaseRenderSessions(Guid sessionId) { }
        public void Enqueue(object content, TimeSpan? durationOverride = null, bool promote = false, bool neverConsiderToBeDuplicate = false, CadMessageLevel level = CadMessageLevel.Information) { }
        public void EnqueueInAll(object content, TimeSpan? durationOverride = null, bool promote = false, bool neverConsiderToBeDuplicate = false, CadMessageLevel level = CadMessageLevel.Information) { }
        public void Enqueue(object identifier, object content, TimeSpan? durationOverride = null, bool promote = false, bool neverConsiderToBeDuplicate = false, CadMessageLevel level = CadMessageLevel.Information) { }
    }
}
