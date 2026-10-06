using Direct2dCad.Rendering;

namespace Direct2dCad.Tests;

public sealed class CadRenderResourceBudgetTests
{
    [Fact]
    public void SharesProcessAllowanceAcrossDocumentsAndWorkersAndRestoresItOnClose()
    {
        var budget = new CadRenderResourceBudget(1_200, 800);
        using var first = budget.RegisterDocument();
        using var main = first.RegisterRenderer();
        Assert.Equal(800, main.LimitBytes);
        var changed = 0;
        first.BudgetChanged += (_, _) => changed++;
        using (var second = budget.RegisterDocument())
        using (var other = second.RegisterRenderer())
        using (var worker = first.RegisterRenderer())
        {
            Assert.Equal(300, main.LimitBytes);
            Assert.Equal(300, worker.LimitBytes);
            Assert.Equal(600, other.LimitBytes);
            main.Report(200);
            worker.Report(100);
            other.Report(500);
            Assert.Equal(new CadRenderResourceStatistics(1_200, 800, 2, 3), budget.Statistics);
            Assert.Equal(new CadRenderResourceStatistics(600, 300, 1, 2), first.Statistics);
        }
        Assert.True(changed >= 3);
        Assert.Equal(800, main.LimitBytes);
        Assert.Equal(200, budget.Statistics.EstimatedRetainedCacheBytes);
    }

    [Fact]
    public void DisposedDocumentAndRendererCannotRetainReportedMemory()
    {
        var budget = new CadRenderResourceBudget(100, 100);
        var document = budget.RegisterDocument();
        var renderer = document.RegisterRenderer();
        renderer.Report(60);
        document.Dispose();
        renderer.Report(80);
        renderer.Dispose();
        renderer.Dispose();
        Assert.Equal(new CadRenderResourceStatistics(100, 0, 0, 0), budget.Statistics);
        Assert.Equal(0, renderer.LimitBytes);
        Assert.Throws<ObjectDisposedException>(() => document.RegisterRenderer());
    }
}
