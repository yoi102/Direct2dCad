using Direct2dCad.ViewModels.Services.Platform.Notifications;

namespace Direct2dCad.ViewModels.Services.Tests;

public sealed class CadMessageLogTests
{
    [Fact]
    public void AddStoresNormalizedEntryAndRaisesEvent()
    {
        var log = new CadMessageLog();
        CadMessageEntry? added = null;
        log.MessageAdded += (_, entry) => added = entry;

        log.Add("  ready  ", CadMessageLevel.Warning, "  Test  ");

        var entry = Assert.Single(log.Entries);
        Assert.Equal("ready", entry.Text);
        Assert.Equal(CadMessageLevel.Warning, entry.Level);
        Assert.Equal("Test", entry.Source);
        Assert.Same(entry, added);
    }

    [Fact]
    public void AddDropsOldestEntryWhenCapacityIsReached()
    {
        var log = new CadMessageLog(maximumEntryCount: 2);

        log.Add("first");
        log.Add("second");
        log.Add("third");

        Assert.Equal(["second", "third"], log.Entries.Select(entry => entry.Text));
    }

    [Fact]
    public void BlankMessagesAreIgnored()
    {
        var log = new CadMessageLog();
        var eventCount = 0;
        log.MessageAdded += (_, _) => eventCount++;

        log.Add(" \t");

        Assert.Empty(log.Entries);
        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void ClearRemovesEntriesAndRaisesEventOnlyWhenNeeded()
    {
        var log = new CadMessageLog();
        var clearCount = 0;
        log.Cleared += (_, _) => clearCount++;

        log.Clear();
        log.Add("message");
        log.Clear();

        Assert.Empty(log.Entries);
        Assert.Equal(1, clearCount);
    }
}
