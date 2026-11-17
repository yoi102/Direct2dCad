using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.CommandLine;
using Direct2dCad.Lang;
using Direct2dCad.Commands;
using Direct2dCad.Commands.Clipboard;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;
using Direct2dCad.HitTesting;
using Direct2dCad.Lang.Strings;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Drawing;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Drawing;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Interactions;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Platform.Notifications;
using Direct2dCad.ViewModels.Services.Platform.Printing;
using Direct2dCad.ViewModels.Services.Rendering;
using Direct2dCad.ViewModels.Services.Snapping;
using Direct2dCad.ViewModels.Services.Styling;
using Direct2dCad.ViewModels.Services.Text;
using MessagePipe;

namespace Direct2dCad.ViewModels;

public partial class CadDocumentViewModel
{
    string ICadCommandLineContext.DocumentName => CadEditor.Document.Name;

    int ICadCommandLineContext.EntityCount => CadEditor.Document.Entities.Values.Count(entity => !entity.IsErased);

    int ICadCommandLineContext.SelectionCount => CadEditor.Selection.EntityIds.Count;

    CadCommandLineDrawingMode ICadCommandLineContext.ToolMode =>
        Enum.Parse<CadCommandLineDrawingMode>(CadCanvasToolMode.ToString());

    bool ICadCommandLineContext.CanUndo => CadEditor.DocumentCommands.CanUndo;

    bool ICadCommandLineContext.CanRedo => CadEditor.DocumentCommands.CanRedo;

    CadCommandLinePoint? ICadCommandLineContext.LastInputPoint => _lastCommandLineInputPoint;

    CadUnit ICadCommandLineContext.Unit => CadEditor.Document.DocumentSettings.Unit;

    void ICadCommandLineContext.SetToolMode(CadCommandLineDrawingMode mode)
    {
        if (mode == CadCommandLineDrawingMode.LayoutViewport)
        {
            BeginLayoutViewportCreation();
            return;
        }
        SetToolMode(Enum.Parse<CadCanvasToolMode>(mode.ToString()));
    }

    bool ICadCommandLineContext.TrySetToolMode(CadCommandLineDrawingMode mode)
    {
        if (!CanEditDocument && mode != CadCommandLineDrawingMode.Select)
        {
            StepInputError = CadUiText.Get(Direct2dCad.Lang.LangKeys.CompatibilityReadOnly);
            return false;
        }
        if (mode == CadCommandLineDrawingMode.LayoutViewport && ActiveLayoutId is null)
        {
            StepInputError = "MVIEW requires an active paper layout.";
            return false;
        }
        ((ICadCommandLineContext)this).SetToolMode(mode);
        return ((ICadCommandLineContext)this).ToolMode == mode;
    }

    void ICadCommandLineContext.Cancel() => Escape();

    void ICadCommandLineContext.Undo() => Undo();

    bool ICadCommandLineContext.UndoCurrentDrawingStep() => UndoCurrentDrawingStep().Handled;

    void ICadCommandLineContext.Redo() => Redo();

    void ICadCommandLineContext.FitToWindow() => FitToWindow();

    int ICadCommandLineContext.SelectAll()
    {
        var entityIds = GetSelectableEntityIds();
        SelectAllEntities();
        return entityIds.Length;
    }

    int ICadCommandLineContext.DeleteSelection()
    {
        var count = CadEditor.Selection.EntityIds.Count;
        return DeleteSelection().Handled ? count : 0;
    }

    CadCommandLineClipboardSummary? ICadCommandLineContext.CopySelection()
    {
        if (CadEditor.Selection.EntityIds.Count == 0)
            return null;

        return CreateClipboardSummary(CopySelection());
    }

    CadCommandLineClipboardSummary? ICadCommandLineContext.BeginPaste()
    {
        return BeginClipboardPastePreview().Handled
            ? CreateClipboardSummary(_paste.Snapshot)
            : null;
    }

    bool ICadCommandLineContext.SubmitDrawingPoint(CadCommandLinePoint point) =>
        HandleDrawingWorldPoint(new CadPointD(point.X, point.Y));

    string? ICadCommandLineContext.DrawingInputError => string.IsNullOrEmpty(StepInputError) ? null : StepInputError;

    bool ICadCommandLineContext.CompleteCurrentDrawing() => CompleteCurrentDrawing().Handled && string.IsNullOrEmpty(StepInputError);

    CadCommandLineRenderStatistics? ICadCommandLineContext.GetRenderStatistics()
    {
        var statistics = RenderSession.RenderStatistics;
        return new CadCommandLineRenderStatistics(
            RenderSession.FramesPerSecond,
            RenderSession.AverageFrameRenderTimeMilliseconds,
            statistics.RenderDurationMilliseconds,
            statistics.IsFullFrame,
            statistics.DirtyRegionCount,
            statistics.ScenePassCount,
            statistics.VisibleEntityCount,
            statistics.EntitySubmissionCount,
            statistics.BlockReferenceCount,
            statistics.ExpandedBlockEntityCount,
            statistics.BlockDefinitionCommandListReplayCount,
            statistics.BlockDefinitionCommandListBuildCount,
            statistics.SelectionEntityCount,
            statistics.SelectionCommandListReplayCount,
            statistics.SelectionCommandListBuildCount,
            statistics.CommandListReplayCount,
            statistics.CommandListBuildCount,
            statistics.TileReplayCount,
            statistics.TileBuildCount,
            statistics.FallbackEntityCount,
            statistics.GeometryRealizationFillDrawCount,
            statistics.GeometryRealizationStrokeDrawCount,
            statistics.GeometryRealizationBuildCount,
            statistics.GeometryRealizationFallbackCount,
            statistics.GeometryRealizationCacheHitCount,
            statistics.GeometryRealizationCacheMissCount,
            statistics.GeometryRealizationBuildMilliseconds,
            statistics.HatchLineSubmissionCount,
            statistics.HatchSimplifiedLineFamilyCount,
            statistics.OleTileBuildCount,
            statistics.SceneTileCacheBytes,
            statistics.CommandListCacheBytes,
            statistics.SelectionCommandListCacheBytes,
            statistics.BlockDefinitionCacheBytes,
            statistics.GeometryRealizationCacheBytes,
            statistics.HatchTileCacheBytes,
            statistics.ImageBitmapCacheBytes,
            statistics.OleTileCacheBytes,
            statistics.GpuCacheBytes,
            statistics.GpuCachePeakBytes,
            statistics.GpuCacheBudgetBytes,
            statistics.GpuCacheEvictionCount,
            statistics.CachePreparationMilliseconds,
            statistics.BackgroundRenderMilliseconds,
            statistics.EntityRenderMilliseconds,
            statistics.TransientRenderMilliseconds,
            statistics.SelectionRenderMilliseconds,
            statistics.OlePreparationMilliseconds,
            statistics.SurfaceDrawMilliseconds,
            statistics.ParallelFrameCount,
            statistics.ParallelMode?.ToString(),
            statistics.ParallelWorkerCount,
            statistics.ParallelEntityCount,
            statistics.ParallelRenderMilliseconds,
            statistics.ParallelGpuCacheBytes)
        {
            GridTileCacheBytes = statistics.GridTileCacheBytes,
            TransientGroupCacheBytes = statistics.TransientGroupCacheBytes,
            DocumentRetainedCacheBytes = RenderSession.ResourceStatistics.EstimatedRetainedCacheBytes,
            DocumentRetainedCacheLimitBytes = RenderSession.ResourceStatistics.RetainedCacheLimitBytes,
            ProcessRetainedCacheBytes = RenderSession.ProcessResourceStatistics.EstimatedRetainedCacheBytes,
            ProcessRetainedCacheLimitBytes = RenderSession.ProcessResourceStatistics.RetainedCacheLimitBytes,
            RenderSessionCount = RenderSession.ProcessResourceStatistics.DocumentCount,
            DocumentRendererCount = RenderSession.ResourceStatistics.RendererCount
        };
    }

    private static CadCommandLineClipboardSummary? CreateClipboardSummary(CadClipboardSnapshot? snapshot)
    {
        return snapshot is null
            ? null
            : new CadCommandLineClipboardSummary(
                snapshot.Items.Count,
                snapshot.Items.Count(item => item.Entity is CadBlockReferenceClipboardSnapshot),
                snapshot.BlockDefinitions.Count);
    }
}
