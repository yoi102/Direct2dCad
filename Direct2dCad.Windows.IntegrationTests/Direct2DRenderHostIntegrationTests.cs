using System.Diagnostics;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ChangeTracking;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.Rendering.Direct2D.Ole;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using SharpGen.Runtime;
using Vortice.Mathematics;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class Direct2DRenderHostIntegrationTests
{
    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_RendersEntityChunksOnIndependentDevices()
    {
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(640, 480);
        host.AttachImageSource(imageSource);
        host.SetSize(640, 480);

        var document = CadDocument.Create("Multi-device rendering");
        for (var index = 0; index < 96; index++)
        {
            var row = index / 16;
            var column = index % 16;
            document.AddLine(
                new CadPointD(column * 4 - 30, row * 4 - 10),
                new CadPointD(column * 4 - 28, row * 4 - 8));
        }

        var viewport = new CadViewport();
        viewport.SetSize(640, 480);
        viewport.SetView(6, new CadPointD(320, 240));
        host.SetScene(document, viewport);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false,
            IsMultiDeviceRenderingEnabled = true,
            MultiDeviceRenderingDeviceCount = 2,
            MultiDeviceRenderingEntityThreshold = 32
        });

        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);

        Assert.Equal(1, host.RenderStatistics.MultiDeviceFrameCount);
        Assert.Equal(2, host.RenderStatistics.MultiDeviceWorkerCount);
        Assert.Equal(96, host.RenderStatistics.MultiDeviceEntityCount);
        Assert.Equal(96, host.RenderStatistics.VisibleEntityCount);
        Assert.True(host.RenderStatistics.MultiDeviceRenderMilliseconds > 0);
        Assert.Equal(1, imageSource.PresentCount);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_MultiDeviceWorkersReuseGeometryRealizations()
    {
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(640, 480);
        host.AttachImageSource(imageSource);
        host.SetSize(640, 480);

        var document = CadDocument.Create("Multi-device geometry realization");
        for (var entityIndex = 0; entityIndex < 4; entityIndex++)
        {
            var yOffset = entityIndex * 18.0 - 27.0;
            document.AddPolyline(
                Enumerable
                    .Range(0, 257)
                    .Select(index =>
                    {
                        var x = -50.0 + index * 100.0 / 256.0;
                        return new CadPointD(
                            x,
                            yOffset + Math.Sin(index * Math.PI / 16.0) * 6.0);
                    })
                    .ToArray());
        }

        var viewport = new CadViewport();
        viewport.SetSize(640, 480);
        viewport.SetView(4, new CadPointD(320, 240));
        host.SetScene(document, viewport);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false,
            IsLevelOfDetailEnabled = true,
            IsMultiDeviceRenderingEnabled = true,
            MultiDeviceRenderingDeviceCount = 2,
            MultiDeviceRenderingEntityThreshold = 2
        });

        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);

        Assert.Equal(1, host.RenderStatistics.MultiDeviceFrameCount);
        Assert.True(host.RenderStatistics.GeometryRealizationBuildCount >= 1);
        Assert.True(host.RenderStatistics.GeometryRealizationCacheMissCount >= 1);
        Assert.True(host.RenderStatistics.MultiDeviceGpuCacheBytes > 0);
        var firstFrameBuildCount =
            host.RenderStatistics.GeometryRealizationBuildCount;

        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);

        Assert.Equal(1, host.RenderStatistics.MultiDeviceFrameCount);
        Assert.True(
            host.RenderStatistics.GeometryRealizationBuildCount <=
            firstFrameBuildCount);
        Assert.True(host.RenderStatistics.GeometryRealizationCacheHitCount >= 1);
        Assert.True(host.RenderStatistics.GeometryRealizationStrokeDrawCount >= 1);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_RecordsSceneChunkOnBackgroundDeviceContext()
    {
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(640, 480);
        host.AttachImageSource(imageSource);
        host.SetSize(640, 480);

        var document = CadDocument.Create("Background chunk recording");
        for (var index = 0; index < 1152; index++)
        {
            var row = index / 48;
            var column = index % 48;
            document.AddLine(
                new CadPointD(column * 2 - 48, row * 2 - 24),
                new CadPointD(column * 2 - 47, row * 2 - 23));
        }

        var viewport = new CadViewport();
        viewport.SetSize(640, 480);
        viewport.SetView(4, new CadPointD(320, 240));
        host.SetScene(document, viewport);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false,
            IsBackgroundChunkRecordingEnabled = true
        });

        host.Render();
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 10;
        while (host.RenderStatistics.BackgroundCommandListBuildCount == 0 &&
               Stopwatch.GetTimestamp() < deadline)
        {
            host.PrepareRenderCacheStep();
            Thread.Yield();
            host.Render();
        }

        Assert.True(host.RenderStatistics.BackgroundCommandListBuildCount > 0);
        Assert.True(
            host.RenderStatistics.BackgroundCommandListBuildMilliseconds > 0);
        Assert.True(host.RenderStatistics.CommandListBuildCount > 0);
        Assert.True(host.RenderStatistics.CommandListCacheBytes > 0);
    }

    [Theory]
    [InlineData(-524287, -524287, 524287, 524287, true)]
    [InlineData(-524288, 0, 10, 10, false)]
    [InlineData(0, 0, 524288, 10, false)]
    [InlineData(double.NaN, 0, 10, 10, false)]
    [Trait("Category", "WindowsIntegration")]
    public void GeometryRealizationBounds_RejectsCoordinatesOutsideDirect2DSafeRange(
        double minX,
        double minY,
        double maxX,
        double maxY,
        bool expected)
    {
        Assert.Equal(
            expected,
            Direct2DGeometryRealizationCache.IsWithinSafeRealizationBounds(
                CadRectD.FromLTRB(minX, minY, maxX, maxY)));
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_FallsBackToDirectGeometryForLargeCoordinates()
    {
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(640, 480);
        host.AttachImageSource(imageSource);
        host.SetSize(640, 480);

        var document = CadDocument.Create("Large coordinate geometry");
        document.AddPolyline(
            Enumerable
                .Range(0, 64)
                .Select(index => new CadPointD(
                    600_000.0 + index * 2.0,
                    Math.Sin(index * Math.PI / 8.0) * 20.0))
                .ToArray());
        var viewport = new CadViewport();
        viewport.SetSize(640, 480);
        viewport.SetView(1.0, new CadPointD(320 - 600_063, 240));

        host.SetScene(document, viewport);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false
        });
        host.RebuildAll(document);

        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);

        Assert.True(host.RenderStatistics.EntitySubmissionCount >= 1);
        Assert.Equal(0, host.RenderStatistics.GeometryRealizationBuildCount);
        Assert.Equal(0, host.RenderStatistics.GeometryRealizationCacheMissCount);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_ReusesLevelOfDetailGeometryRealizationAfterColorChange()
    {
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(640, 480);
        host.AttachImageSource(imageSource);
        host.SetSize(640, 480);

        var document = CadDocument.Create("LOD geometry realization");
        var points = Enumerable
            .Range(0, 257)
            .Select(index =>
            {
                var x = -50.0 + index * 100.0 / 256.0;
                return new CadPointD(x, Math.Sin(index * Math.PI / 16.0) * 20.0);
            })
            .ToArray();
        var polyline = document.AddPolyline(points);
        var viewport = new CadViewport();
        viewport.SetSize(640, 480);
        viewport.SetView(1.0, new CadPointD(320, 240));

        host.SetScene(document, viewport);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false,
            IsLevelOfDetailEnabled = true
        });
        host.RebuildAll(document);

        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);

        Assert.True(host.RenderStatistics.GeometryRealizationBuildCount >= 1);
        Assert.True(host.RenderStatistics.GeometryRealizationCacheMissCount >= 1);
        Assert.True(host.RenderStatistics.GeometryRealizationBuildMilliseconds >= 0);

        document.Layers[polyline.LayerId].SetColor(CadColor.Green);
        host.ApplyChanges(
            document,
            CadDocumentChangeSet.ForEntity(
                polyline.Id,
                CadEntityChangeKind.Appearance));
        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);

        Assert.Equal(0, host.RenderStatistics.GeometryRealizationBuildCount);
        Assert.True(host.RenderStatistics.GeometryRealizationCacheHitCount >= 1);
        Assert.True(host.RenderStatistics.GeometryRealizationStrokeDrawCount >= 1);

        polyline.SetLineWeight(new CadLineWeight(2.0));
        host.ApplyChanges(
            document,
            CadDocumentChangeSet.ForEntity(
                polyline.Id,
                CadEntityChangeKind.Appearance));
        host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);

        Assert.True(host.RenderStatistics.GeometryRealizationBuildCount >= 1);
        Assert.True(host.RenderStatistics.GeometryRealizationCacheMissCount >= 1);
    }

    [Theory]
    [InlineData(0.49, 0)]
    [InlineData(0.5, 1)]
    [InlineData(1.49, 1)]
    [InlineData(1.5, 2)]
    [InlineData(-0.49, 0)]
    [InlineData(-0.5, -1)]
    [Trait("Category", "WindowsIntegration")]
    public void PanPreviewTranslation_QuantizesFractionalOffsetForGridContinuity(
        double translation,
        double expected)
    {
        Assert.Equal(
            expected,
            Direct2DImageRenderHost.QuantizePanPreviewTranslation(translation));
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void DeviceFailureClassifier_RecognizesOnlyRecoverableDeviceFailures()
    {
        Result[] recoverableResults =
        [
            Vortice.Direct2D1.ResultCode.RecreateTarget,
            Vortice.DXGI.ResultCode.DeviceRemoved,
            Vortice.DXGI.ResultCode.DeviceReset,
            Vortice.DXGI.ResultCode.DeviceHung,
            Vortice.DXGI.ResultCode.DriverInternalError,
            Vortice.DXGI.ResultCode.AccessLost
        ];

        Assert.All(
            recoverableResults,
            result => Assert.True(Direct2DDeviceFailureClassifier.IsRecoverable(result)));
        Assert.False(Direct2DDeviceFailureClassifier.IsRecoverable(Result.Ok));
        Assert.False(Direct2DDeviceFailureClassifier.IsRecoverable(
            Vortice.Direct2D1.ResultCode.WrongState));
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void DeviceResourceRecovery_RebindsSurfaceAndRemainsDrawable()
    {
        using var target = new ImageSourceDirect2DResource();
        var imageSource = new RecordingImageSource(160, 120);
        target.SetTarget(imageSource);
        target.DrawFrame(
            context => context.Clear(new Color4(0.1f, 0.2f, 0.3f, 1.0f)));
        Assert.True(target.CaptureBaseScene(null));
        Assert.True(target.HasBaseSceneSnapshot);
        var setSurfaceCountBeforeRecovery = imageSource.SetSurfaceCount;

        target.RecoverFromDeviceLoss();

        Assert.True(target.IsTargetReady);
        Assert.False(target.HasBaseSceneSnapshot);
        Assert.Equal(setSurfaceCountBeforeRecovery + 2, imageSource.SetSurfaceCount);
        Assert.Equal(nint.Zero, imageSource.SurfaceAssignments[^2]);
        Assert.NotEqual(nint.Zero, imageSource.SurfaceAssignments[^1]);

        target.DrawFrame(
            context => context.Clear(new Color4(0.3f, 0.2f, 0.1f, 1.0f)));

        Assert.Equal(2, imageSource.PresentCount);
        Assert.NotEqual(nint.Zero, imageSource.SurfacePointer);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_EndDrawDeviceLossRecreatesResourcesAndPresentsRetriedFrameOnce()
    {
        var endDrawCallCount = 0;
        var injectedFailureCount = 0;
        using var host = new Direct2DImageRenderHost(context =>
        {
            endDrawCallCount++;
            if (injectedFailureCount == 0)
            {
                injectedFailureCount++;
                return Vortice.Direct2D1.ResultCode.RecreateTarget;
            }

            return context.EndDraw();
        });
        var imageSource = new RecordingImageSource(240, 160);
        host.AttachImageSource(imageSource);
        host.SetSize(240, 160);
        var document = CadDocument.Create("Device loss retry");
        document.AddLine(new CadPointD(-10, 0), new CadPointD(10, 0));
        var viewport = new CadViewport();
        viewport.SetSize(240, 160);
        viewport.SetView(6, new CadPointD(120, 80));
        host.SetScene(document, viewport);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false
        });
        host.RebuildAll(document);

        host.Render();

        Assert.Equal(1, injectedFailureCount);
        Assert.True(endDrawCallCount >= 2);
        Assert.Equal(1, imageSource.PresentCount);
        Assert.Equal(1, imageSource.SurfaceAssignments.Count(pointer => pointer == nint.Zero));
        Assert.NotEqual(nint.Zero, imageSource.SurfaceAssignments[^1]);
        Assert.True(host.RenderStatistics.IsFullFrame);
        Assert.True(host.RenderStatistics.EntitySubmissionCount >= 1);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_CreatesNativeResourcesAndPresentsFullAndPartialFrames()
    {
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(320, 240);
        host.AttachImageSource(imageSource);
        host.SetSize(320, 240);

        var document = CadDocument.Create("Native render integration");
        var solidFillStyleId = document.CreateSolidFillStyle(
            "Integration solid",
            CadColor.Green);
        var hatchPatternId = document.CreateHatchPattern(
            "Integration grid",
            CadHatchPatternLines.Grid(3));
        var hatchFillStyleId = document.CreateHatchFillStyle(
            "Integration hatch",
            hatchPatternId,
            CadColor.Blue,
            hatchScale: 0.75,
            hatchAngle: Math.PI / 8,
            hatchOrigin: new CadPointD(-12, -8));
        var line = document.AddLine(
            new CadPointD(-20, -10),
            new CadPointD(20, 10));
        document.AddCircle(
            new CadPointD(-15, 0),
            5,
            fillStyleId: solidFillStyleId);
        document.AddEllipse(
            new CadPointD(14, 0),
            6,
            3,
            fillStyleId: hatchFillStyleId);
        document.AddArcDegrees(new CadPointD(-12, -12), 6, 15, 235);
        document.AddEllipseArc(
            new CadPointD(13, -12),
            7,
            3,
            0.2,
            2.4);
        document.AddRectangle(
            CadRectD.FromLTRB(-6, -7, 6, 1),
            cornerRadiusX: 1.5,
            cornerRadiusY: 1.5,
            fillStyleId: hatchFillStyleId);
        document.AddPolyline(
            [
                new CadPointD(-8, 8),
                new CadPointD(-2, 14),
                new CadPointD(3, 8)
            ],
            isClosed: true,
            fillStyleId: solidFillStyleId);
        document.AddSpline(
            [
                new CadPointD(5, 8),
                new CadPointD(10, 15),
                new CadPointD(18, 9),
                new CadPointD(12, 6)
            ],
            closed: true,
            fillStyleId: hatchFillStyleId);
        var text = document.AddText("DirectWrite", new CadPointD(-10, 15), 5);
        var viewport = new CadViewport();
        viewport.SetSize(320, 240);
        viewport.SetView(5, new CadPointD(160, 120));

        host.SetScene(document, viewport);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false
        });
        var transientStyle = new CadTransientStyle(CadColor.Green, 1.5);
        var transientScene = new CadTransientScene();
        transientScene.Replace(
        [
            new CadTransientLine(
                new CadPointD(-18, 18),
                new CadPointD(18, 18),
                transientStyle),
            new CadTransientCircle(new CadPointD(18, -12), 4, transientStyle)
        ]);
        var handleScene = new CadHandleScene();
        handleScene.Replace(
        [
            new CadSelectionEntityReference(
                line.Id,
                line.Bounds,
                CadVectorD.Zero,
                CadHandleStyle.SelectionOutline),
            new CadGripHandle(
                line.Id,
                line.Start,
                CadHandleType.Vertex,
                CadHandleStyle.Grip)
        ]);
        host.SetTransientScene(transientScene);
        host.SetHandleScene(handleScene);
        host.RebuildAll(document);

        var textChanges = host.UpdateTextMeasurements(document);
        Assert.False(text.RequiresBoundsMeasurement);
        Assert.Contains(textChanges.EntityChanges, change => change.EntityId == text.Id);

        host.Render();

        Assert.NotEqual(nint.Zero, imageSource.SurfacePointer);
        Assert.Equal(320, host.TargetWidth);
        Assert.Equal(240, host.TargetHeight);
        Assert.Equal(1, imageSource.PresentCount);
        Assert.True(host.RenderStatistics.IsFullFrame);
        Assert.True(host.RenderStatistics.VisibleEntityCount >= 9);
        Assert.True(host.RenderStatistics.EntitySubmissionCount >= 9);
        Assert.Equal(1, host.RenderStatistics.SelectionEntityCount);
        Assert.True(host.RenderStatistics.TransientRenderMilliseconds >= 0);
        Assert.True(host.RenderStatistics.SelectionRenderMilliseconds >= 0);

        var previousBounds = line.Bounds;
        line.SetGeometry(new CadPointD(-10, -5), new CadPointD(25, 12));
        host.ApplyChanges(
            document,
            CadDocumentChangeSet.ForEntity(line.Id, CadEntityChangeKind.Geometry));
        var dirtyBounds = previousBounds.Union(line.Bounds);
        var invalidation = CadRenderInvalidation.FromWorldBounds(
            viewport,
            dirtyBounds,
            host.TargetWidth,
            host.TargetHeight);

        host.Render(invalidation, baseSceneChanged: true);

        Assert.Equal(2, imageSource.PresentCount);
        var dirtyRects = Assert.IsAssignableFrom<IReadOnlyList<IntRect>>(
            imageSource.LastDirtyRects);
        Assert.NotEmpty(dirtyRects);
        Assert.All(dirtyRects, rect =>
        {
            Assert.True(rect.Width > 0);
            Assert.True(rect.Height > 0);
            Assert.InRange(rect.X, 0, 319);
            Assert.InRange(rect.Y, 0, 239);
        });
        Assert.False(host.RenderStatistics.IsFullFrame);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_RecreatesTargetWhenSurfaceSizeChanges()
    {
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(64, 64);
        host.AttachImageSource(imageSource);

        host.SetSize(192, 128);

        Assert.Equal(192, host.TargetWidth);
        Assert.Equal(128, host.TargetHeight);
        Assert.Equal(192, imageSource.SurfaceWidth);
        Assert.Equal(128, imageSource.SurfaceHeight);
        Assert.NotEqual(nint.Zero, imageSource.SurfacePointer);
        Assert.True(imageSource.SetSurfaceCount >= 2);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_DrawsGridAndOriginWithoutAntialiasing()
    {
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(256, 192);
        host.AttachImageSource(imageSource);
        host.SetSize(256, 192);

        var document = CadDocument.Create("Native background integration");
        var viewport = new CadViewport();
        viewport.SetSize(256, 192);
        viewport.SetView(8, new CadPointD(128, 96));
        host.SetScene(document, viewport);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = true,
            DrawOrigin = true,
            DrawGripHandles = false,
            IsAntialiasingEnabled = false,
            IsTextAntialiasingEnabled = false
        });

        host.Render();

        Assert.Equal(1, imageSource.PresentCount);
        Assert.True(host.RenderStatistics.IsFullFrame);
        Assert.Equal(0, host.RenderStatistics.VisibleEntityCount);
        Assert.NotEqual(nint.Zero, imageSource.SurfacePointer);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void RenderHost_RendersShapeTextImageOleAndBlockReferenceWithPartialInvalidation()
    {
        using var host = new Direct2DImageRenderHost();
        var imageSource = new RecordingImageSource(420, 320);
        host.AttachImageSource(imageSource);
        host.SetSize(420, 320);

        var oleDrawRequests = new List<Direct2DOleDrawRequest>();
        host.SetOleDrawCallback(request =>
        {
            oleDrawRequests.Add(request);
            return new Direct2DOleDrawData(
                request.PixelWidth,
                request.PixelHeight,
                checked(request.PixelWidth * 4),
                CreatePixels(request.PixelWidth, request.PixelHeight));
        });

        var document = CadDocument.Create("Binary entity integration");
        document.AddShapeText("CAD", new CadPointD(-45, 20), 8);
        var image = document.AddImage(
            CadRectD.FromXYWH(-20, -15, 10, 8),
            pixelWidth: 2,
            pixelHeight: 2,
            stride: 8,
            CreatePixels(2, 2),
            opacity: 0.7,
            rotationRadians: Math.PI / 12);
        var ole = document.AddOleObject(
            CadRectD.FromXYWH(0, -15, 12, 8),
            [1, 2, 3, 4],
            opacity: 0.65);
        var blockId = document.CreateBlockDefinition("Integration block", CadPointD.Origin);
        var blockChild = document.AddLine(CadPointD.Origin, new CadPointD(8, 6));
        document.MoveEntityToBlock(blockChild.Id, blockId);
        var blockReference = document.AddBlockReference(
            blockId,
            new CadPointD(20, 10),
            scaleX: 2,
            scaleY: 2);
        var viewport = new CadViewport();
        viewport.SetSize(420, 320);
        viewport.SetView(4, new CadPointD(210, 160));

        host.SetScene(document, viewport);
        host.SetRenderOptions(new CadRenderOptions
        {
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false,
            IsLevelOfDetailEnabled = false
        });
        host.RebuildAll(document);

        host.Render();

        Assert.Equal(1, imageSource.PresentCount);
        Assert.True(host.RenderStatistics.IsFullFrame);
        Assert.True(host.RenderStatistics.VisibleEntityCount >= 4);
        Assert.True(host.RenderStatistics.EntitySubmissionCount >= 4);
        Assert.True(host.RenderStatistics.BlockReferenceCount >= 1);
        Assert.True(host.RenderStatistics.ExpandedBlockEntityCount >= 1);
        Assert.True(host.RenderStatistics.ImageBitmapCacheBytes > 0);
        Assert.True(host.RenderStatistics.OleTileBuildCount >= 1);
        Assert.NotEmpty(oleDrawRequests);
        Assert.All(oleDrawRequests, request =>
        {
            Assert.Equal(ole.Id, request.RenderKey.EntityId);
            Assert.True(request.PixelWidth > 0);
            Assert.True(request.PixelHeight > 0);
            Assert.True(request.FullPixelWidth >= request.PixelWidth);
            Assert.True(request.FullPixelHeight >= request.PixelHeight);
        });

        var oldBounds = image.Bounds.Union(ole.Bounds).Union(blockReference.Bounds);
        image.SetBounds(CadRectD.FromXYWH(-32, -18, 18, 12));
        ole.SetBounds(CadRectD.FromXYWH(2, -20, 18, 12));
        blockReference.SetPosition(new CadPointD(12, 18));
        document.RefreshBlockReferenceBounds();
        var changes = CadDocumentChangeSet.Combine(
        [
            CadDocumentChangeSet.ForEntity(image.Id, CadEntityChangeKind.Geometry),
            CadDocumentChangeSet.ForEntity(ole.Id, CadEntityChangeKind.Geometry),
            CadDocumentChangeSet.ForEntity(blockReference.Id, CadEntityChangeKind.Geometry)
        ]);
        host.ApplyChanges(document, changes);
        var dirtyBounds = oldBounds
            .Union(image.Bounds)
            .Union(ole.Bounds)
            .Union(blockReference.Bounds);
        var invalidation = CadRenderInvalidation.FromWorldBounds(
            viewport,
            dirtyBounds,
            host.TargetWidth,
            host.TargetHeight);

        host.Render(invalidation, baseSceneChanged: true);

        Assert.Equal(2, imageSource.PresentCount);
        Assert.False(host.RenderStatistics.IsFullFrame);
        var dirtyRects = Assert.IsAssignableFrom<IReadOnlyList<IntRect>>(
            imageSource.LastDirtyRects);
        Assert.NotEmpty(dirtyRects);
        Assert.All(dirtyRects, rect =>
        {
            Assert.True(rect.Width > 0);
            Assert.True(rect.Height > 0);
            Assert.InRange(rect.X, 0, 419);
            Assert.InRange(rect.Y, 0, 319);
        });

        var requestCountBeforeDataChange = oleDrawRequests.Count;
        var updatedOleBytes = new byte[] { 9, 8, 7, 6 };
        ole.SetOleData(updatedOleBytes, "application/updated", "updated.ole");
        host.ApplyChanges(
            document,
            CadDocumentChangeSet.ForEntity(
                ole.Id,
                CadEntityChangeKind.Appearance | CadEntityChangeKind.EmbeddedData));
        var oleInvalidation = CadRenderInvalidation.FromWorldBounds(
            viewport,
            ole.Bounds,
            host.TargetWidth,
            host.TargetHeight);

        host.Render(oleInvalidation, baseSceneChanged: true);

        Assert.Equal(3, imageSource.PresentCount);
        Assert.True(oleDrawRequests.Count > requestCountBeforeDataChange);
        Assert.All(
            oleDrawRequests.Skip(requestCountBeforeDataChange),
            request => Assert.Equal(updatedOleBytes, request.OleBytes.ToArray()));
    }

    private static byte[] CreatePixels(int width, int height)
    {
        var pixels = new byte[checked(width * height * 4)];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 0x30;
            pixels[index + 1] = 0x90;
            pixels[index + 2] = 0xE0;
            pixels[index + 3] = 0xFF;
        }

        return pixels;
    }

    private sealed class RecordingImageSource(int width, int height) : ID3D11ImageSource
    {
        public int SurfaceWidth { get; private set; } = width;
        public int SurfaceHeight { get; private set; } = height;
        public nint SurfacePointer { get; private set; }
        public int SetSurfaceCount { get; private set; }
        public int PresentCount { get; private set; }
        public IReadOnlyList<IntRect>? LastDirtyRects { get; private set; }
        public List<nint> SurfaceAssignments { get; } = [];

        public void SetSize(int width, int height)
        {
            SurfaceWidth = width;
            SurfaceHeight = height;
        }

        public void SetSurface(nint surface9Ptr)
        {
            SurfacePointer = surface9Ptr;
            SetSurfaceCount++;
            SurfaceAssignments.Add(surface9Ptr);
        }

        public void Present(Action presentAction, IReadOnlyList<IntRect>? dirtyRects = null)
        {
            presentAction();
            PresentCount++;
            LastDirtyRects = dirtyRects?.ToArray();
        }

        public void Invalidate()
        {
        }

        public void Invalidate(IntRect dirtyRect)
        {
        }

        public void Invalidate(IReadOnlyList<IntRect> dirtyRects)
        {
        }
    }
}
