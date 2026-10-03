using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Rendering.Direct2D.Resources;
using Vortice.Direct2D1;
namespace Direct2dCad.Windows.IntegrationTests;
public sealed class VisiblePreparationIntegrationTests
{
    [Fact][Trait("Category","WindowsIntegration")]
    public void VisiblePriorityIgnoresUnscheduledIdsAndErasedEntitiesAndDoesNotWaitForOffscreenCapture()
    {
        var doc=CadDocument.Create("visible");var first=doc.AddLine(default,new(10,0));var erased=doc.AddLine(new(0,10),new(10,10));
        for(var i=0;i<100;i++)doc.AddLine(new(1000+i,0),new(1000+i,10));
        using var factory=D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.MultiThreaded);
        using var preparation=new Direct2DGeometryPreparationService(factory);preparation.Schedule(doc);
        var unscheduled=doc.AddLine(new(0,20),new(10,20));erased.Erase();
        preparation.Prioritize([unscheduled.Id,first.Id,erased.Id]);preparation.CaptureStep(new ResourcePreparationBudget(4,TimeSpan.FromMilliseconds(50)));
        var stop=System.Diagnostics.Stopwatch.StartNew();
        while(preparation.HasVisiblePending)
        {
            if(preparation.TryTakeNext(out var item)){preparation.MarkApplied(item!.EntityId);item.Dispose();}
            if(stop.Elapsed>TimeSpan.FromSeconds(5))throw new TimeoutException("Visible preparation did not complete.");Thread.Sleep(1);
        }
        Assert.True(preparation.IsPending); // Offscreen entities have not been captured yet.
    }
}
