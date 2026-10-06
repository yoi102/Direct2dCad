using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Db;
using Direct2dCad.IO;
using Direct2dCad.Lang;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.ViewModels;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Interactions;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Platform.Notifications;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace Direct2dCad.Windows.IntegrationTests;

[Collection(WpfBindingDiagnosticsCollection.Name)]
public sealed class BooleanAvailabilityBindingTests
{
    [Theory]
    [InlineData(true, "en-US")]
    [InlineData(false, "en-US")]
    [InlineData(true, "zh-CN")]
    [InlineData(false, "zh-CN")]
    public void ActualViewBindingsExposeDisabledReasonsAndFollowSelection(bool ribbon, string culture)
    {
        RunSta(() =>
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            using var services = CreateServices();
            using var document = ActivatorUtilities.CreateInstance<CadDocumentViewModel>(services, new CadClipboardStore());
            using var tab = new EditorTabViewModel(document, new SettingsStub(), new SettingsStub(),
                null!, null!, null!, services.GetRequiredService<ISnackbarService>(), null!,
                services.GetRequiredService<ISubscriber<CadDocumentViewSettingsChangedMessage>>(),
                services.GetRequiredService<ISubscriber<CadSelectionFilterChangedMessage>>(),
                services.GetRequiredService<ISubscriber<CadDocumentInteractionStateChangedMessage>>(),
                services.GetRequiredService<IPublisher<EditorTabDocumentSummaryChangedMessage>>(),
                new CadDocumentStorage());
            var (owner, controls) = CreateControlsFromView(ribbon, tab);

            AssertAvailability(controls, enabled: false, CadUiText.Get("BooleanSelectionMinimum"));

            var first = document.CadEditor.AddCircle(new(0, 0), 10);
            var second = document.CadEditor.AddCircle(new(8, 0), 10);
            document.SelectEntities([first, second]);
            AssertAvailability(controls, enabled: true, string.Empty);

            var ellipse = document.CadEditor.AddEllipse(new(5, 0), 10, 6);
            document.SelectEntities([first, ellipse]);
            AssertAvailability(controls, enabled: true, string.Empty);

            var spline = document.CadEditor.AddSpline([new(0, 0), new(5, 3), new(10, 0)]);
            document.SelectEntities([first, spline]);
            var unsupportedReason = string.Format(CultureInfo.CurrentUICulture,
                CadUiText.Get("BooleanSelectionUnsupportedType"), CadUiText.Get("Spline"));
            AssertAvailability(controls, enabled: false, unsupportedReason);

            // CanExecute remains false here; the explanation must still refresh.
            document.SelectEntities([first]);
            AssertAvailability(controls, enabled: false, CadUiText.Get("BooleanSelectionMinimum"));
            GC.KeepAlive(owner);
        });
    }

    private static void AssertAvailability(IReadOnlyList<(Control Control, string Label)> controls,
        bool enabled, string reason)
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        foreach (var (control, label) in controls)
        {
            var expected = CadUiText.Get(label) + (reason.Length == 0 ? string.Empty : Environment.NewLine + reason);
            if (control is MenuItem { Command: { } command } child)
            {
                // WPF intentionally defers child MenuItem.IsEnabled/CanExecute
                // coercion until its submenu opens. Keep this test windowless:
                // the parent IsEnabled and the child's real command gate are
                // checked independently, without opening a native Popup.
                Assert.Equal(enabled, command.CanExecute(child.CommandParameter));
                var commandBinding = Assert.IsType<BindingExpression>(BindingOperations.GetBindingExpression(child, MenuItem.CommandProperty));
                Assert.Equal(BindingStatus.Active, commandBinding.Status);
                Assert.False(commandBinding.HasError);
            }
            else Assert.Equal(enabled, control.IsEnabled);
            Assert.True(ToolTipService.GetShowOnDisabled(control));
            Assert.Equal(expected, Assert.IsType<string>(control.ToolTip));
            Assert.Equal(expected, AutomationProperties.GetHelpText(control));
            AutomationPeer peer = control switch
            {
                Button button => new ButtonAutomationPeer(button),
                MenuItem item => new MenuItemAutomationPeer(item),
                _ => throw new InvalidOperationException()
            };
            Assert.Equal(expected, peer.GetHelpText());
            foreach (var property in new[] { FrameworkElement.ToolTipProperty, AutomationProperties.HelpTextProperty })
            {
                var binding = Assert.IsType<BindingExpression>(BindingOperations.GetBindingExpression(control, property));
                Assert.False(binding.HasError);
                Assert.Equal(BindingStatus.Active, binding.Status);
            }
        }
    }

    private static (FrameworkElement Owner, IReadOnlyList<(Control Control, string Label)> Controls)
        CreateControlsFromView(bool ribbon, EditorTabViewModel tab)
    {
        var source = XDocument.Load(ViewPath(ribbon ? "MainRibbonView.xaml" : "EditorTabView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var labels = new[] { "BooleanUnion", "BooleanIntersection", "BooleanDifference" };
        if (ribbon)
        {
            var panelSource = new XElement(presentation + "StackPanel", labels.Select(label =>
                CopyBehavior(FindControl(source, label + "ToolButton"))));
            var panel = (StackPanel)XamlReader.Parse(panelSource.ToString());
            panel.DataContext = new { CurrentEditorTabViewModel = tab };
            return (panel, panel.Children.Cast<Control>().Zip(labels, (control, label) => (control, label)).ToArray());
        }

        // Keep the real detached-menu BindingProxy route, not a direct test-only DataContext.
        // Icons, styles and headers are omitted: this verifies behavior bindings and UIA text,
        // not a shown popup, tooltip placement or the complete themed ribbon layout.
        var originalMenu = FindControl(source, "BooleanOperationsMenu");
        var originalContextMenu = originalMenu.Parent!;
        var originalProxy = source.Descendants().Single(element =>
            (string?)element.Attribute(xaml + "Key") == "EditorTabCommandProxy");
        var menuSource = CopyBehavior(originalMenu);
        foreach (var label in labels)
            menuSource.Add(CopyBehavior(FindControl(source, label + "MenuItem")));
        var contextSource = new XElement(presentation + "ContextMenu",
            new XAttribute(originalContextMenu.Attribute(xaml + "Key")!),
            new XAttribute(originalContextMenu.Attribute("DataContext")!), menuSource);
        var proxyType = typeof(Direct2dCad.wpf.Views.BindingProxy);
        XNamespace views = $"clr-namespace:{proxyType.Namespace};assembly={proxyType.Assembly.GetName().Name}";
        var originalHost = Assert.Single(source.Descendants(), element =>
            (string?)element.Attribute("ContextMenu") == "{StaticResource CadCanvasContextMenu}");
        var ownerSource = new XElement(presentation + "UserControl",
            new XAttribute(XNamespace.Xmlns + "x", xaml),
            new XAttribute(XNamespace.Xmlns + "views", views),
            new XElement(presentation + "UserControl.Resources",
                new XElement(views + originalProxy.Name.LocalName, originalProxy.Attributes()), contextSource),
            new XElement(presentation + "Border", new XAttribute(originalHost.Attribute("ContextMenu")!)));
        var owner = (UserControl)XamlReader.Parse(ownerSource.ToString());
        owner.DataContext = tab;
        owner.Measure(new Size(640, 480));
        owner.Arrange(new Rect(0, 0, 640, 480));
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var context = Assert.IsType<ContextMenu>(Assert.IsType<Border>(owner.Content).ContextMenu);
        Assert.Same(tab, ((Direct2dCad.wpf.Views.BindingProxy)owner.Resources["EditorTabCommandProxy"]).Data);
        Assert.Same(tab, context.DataContext);
        context.Measure(new Size(640, 480));
        context.Arrange(new Rect(0, 0, 640, 480));
        context.UpdateLayout();
        var parent = Assert.IsType<MenuItem>(Assert.Single(context.Items.Cast<object>()));
        var controls = new List<(Control Control, string Label)> { (parent, "BooleanOperations") };
        controls.AddRange(parent.Items.Cast<Control>().Zip(labels, (control, label) => (control, label)));
        return (owner, controls);
    }

    private static XElement FindControl(XDocument source, string automationId) =>
        Assert.Single(source.Descendants(), element => (string?)element.Attribute("AutomationProperties.AutomationId") == automationId);

    private static XElement CopyBehavior(XElement source)
    {
        var copied = new XElement(source.Name);
        foreach (var name in new[] { "AutomationProperties.AutomationId", "AutomationProperties.HelpText", "ToolTip", "ToolTipService.ShowOnDisabled" })
        {
            var attribute = source.Attribute(name);
            Assert.NotNull(attribute);
            copied.Add(new XAttribute(attribute));
        }
        foreach (var name in new[] { "Command", "IsEnabled" })
        {
            if (source.Attribute(name) is { } attribute)
                copied.Add(new XAttribute(attribute));
        }
        return copied;
    }

    private static string ViewPath(string fileName, [CallerFilePath] string sourcePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", "Direct2dCad.wpf", "Views", fileName));

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddMessagePipe();
        services.AddSingleton<ICadRenderSessionFactory>(new Direct2DRenderSessionFactory(new CadRenderResourceBudget()));
        var platform = new PlatformStub();
        services.AddSingleton<IImageImportService>(platform);
        services.AddSingleton<IClipboardTextService>(platform);
        services.AddSingleton<IOleHostService>(platform);
        services.AddSingleton<ISnackbarService>(platform);
        return services.BuildServiceProvider();
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Boolean availability binding test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class SettingsStub : IUserSettingsStore, IWorkspaceSettingsStore
    {
        public CadUserSettings Load() => CadUserSettings.CreateDefault();
        public void Save(CadUserSettings settings) { }
        public CadDocumentWorkspaceSettings LoadDocument(string documentFilePath) => new();
        public void SaveDocument(string documentFilePath, CadDocumentWorkspaceSettings settings) { }
    }

    private sealed class PlatformStub : IImageImportService, IClipboardTextService, IOleHostService, ISnackbarService
    {
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
        public void Enqueue(object content, TimeSpan? durationOverride = null, bool promote = false,
            bool neverConsiderToBeDuplicate = false, CadMessageLevel level = CadMessageLevel.Information) { }
        public void EnqueueInAll(object content, TimeSpan? durationOverride = null, bool promote = false,
            bool neverConsiderToBeDuplicate = false, CadMessageLevel level = CadMessageLevel.Information) { }
        public void Enqueue(object identifier, object content, TimeSpan? durationOverride = null, bool promote = false,
            bool neverConsiderToBeDuplicate = false, CadMessageLevel level = CadMessageLevel.Information) { }
    }
}
