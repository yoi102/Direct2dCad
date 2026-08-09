using Direct2dCad.Client.Common.Settings;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Platform.Notifications;
using Direct2dCad.ViewModels.Toolboxes;

namespace Direct2dCad.ViewModels.Tests;

public sealed class MessageToolboxViewModelTests
{
    [Fact]
    public void FiltersMessagesBySearchAndLevel()
    {
        var log = new CadMessageLog();
        log.Add("Document opened", source: "File");
        log.Add("Document failed", CadMessageLevel.Error, "File");
        log.Add("Layer warning", CadMessageLevel.Warning, "Layers");
        using var viewModel = CreateViewModel(log);

        viewModel.SearchText = "document";
        Assert.Equal(["Document failed", "Document opened"],
            viewModel.VisibleEntries.Select(entry => entry.Text));

        viewModel.SelectedLevel = CadMessageLevel.Error;
        var entry = Assert.Single(viewModel.VisibleEntries);
        Assert.Equal("Document failed", entry.Text);
    }

    [Fact]
    public void ClearCommandClearsLogAndVisibleEntries()
    {
        var log = new CadMessageLog();
        log.Add("message");
        using var viewModel = CreateViewModel(log);

        viewModel.ClearMessagesCommand.Execute(null);

        Assert.Empty(log.Entries);
        Assert.Empty(viewModel.VisibleEntries);
    }

    private static MessageToolboxViewModel CreateViewModel(ICadMessageLog log)
    {
        return new MessageToolboxViewModel(
            new InMemoryToolboxLayoutSettingsStore(),
            new TestToolboxIconProvider(),
            log);
    }

    private sealed class InMemoryToolboxLayoutSettingsStore : IToolboxLayoutSettingsStore
    {
        public CadToolboxState? Load(string contentId) => null;

        public void Save(IEnumerable<KeyValuePair<string, CadToolboxState>> toolboxes)
        {
        }
    }

    private sealed class TestToolboxIconProvider : IToolboxIconProvider
    {
        public object Explorer => string.Empty;
        public object Layers => string.Empty;
        public object Blocks => string.Empty;
        public object Terminal => string.Empty;
        public object Search => string.Empty;
        public object Filter => string.Empty;
        public object Git => string.Empty;
        public object Problems => string.Empty;
        public object Assistant => string.Empty;
        public object Messages => string.Empty;
    }
}
