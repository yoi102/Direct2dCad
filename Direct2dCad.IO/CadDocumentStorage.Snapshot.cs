using System.Diagnostics;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.IO.FileFormat.Container;

namespace Direct2dCad.IO;

public sealed partial class CadDocumentStorage
{
    public async Task SaveAsync(CadDocument document, string filePath, CadSnapshotCaptureOptions capture,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(capture);
        void CheckCurrent()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!capture.IsCurrent())
                throw new CadSnapshotChangedException();
        }

        CheckCurrent();
        var lastYield = Stopwatch.GetTimestamp();
        async ValueTask CheckpointAsync(bool force = false)
        {
            CheckCurrent();
            if (!force && Stopwatch.GetElapsedTime(lastYield) < capture.MaximumSliceDuration)
                return;
            await capture.YieldAsync(cancellationToken);
            CheckCurrent();
            lastYield = Stopwatch.GetTimestamp();
        }

        // Let the progress surface render before capturing. Never enumerate live state on Task.Run.
        await CheckpointAsync(force: true);
        var entities = CadDocumentMapper.IndexEntities(document);
        var payloads = new Queue<ISectionPayload>();
        try
        {
            payloads.Enqueue(Capture(CadSectionKind.Document, CadDocumentMapper.ToDocumentSection(document)));
            payloads.Enqueue(Capture(CadSectionKind.Settings, CadDocumentMapper.ToSettingsSection(document)));
            await CheckpointAsync();
            payloads.Enqueue(Capture(CadSectionKind.Layers, CadDocumentMapper.ToLayerSection(document)));
            await CheckpointAsync();
            payloads.Enqueue(Capture(CadSectionKind.Styles, CadDocumentMapper.ToStylesSection(document)));
            await CheckpointAsync();
            payloads.Enqueue(Capture(CadSectionKind.Layouts, CadDocumentMapper.ToLayoutsSection(document)));
            payloads.Enqueue(Capture(CadSectionKind.Blocks, CadDocumentMapper.ToBlocksSection(document)));
            await CheckpointAsync();

            async Task AddEntities<TSection>(CadSectionKind kind, IEnumerable<CadEntity> source,
                Func<ILookup<Type, CadEntity>, TSection> map, Action<TSection, TSection> append)
            {
                var section = map(Array.Empty<CadEntity>().ToLookup(entity => entity.GetType()));
                foreach (var batch in source.Chunk(128))
                {
                    CheckCurrent();
                    append(section, map(batch.ToLookup(entity => entity.GetType())));
                    await CheckpointAsync();
                }
                payloads.Enqueue(Capture(kind, section));
            }

            await AddEntities(CadSectionKind.Lines, entities[typeof(CadLine)], CadDocumentMapper.ToLinesSection, (a, b) => a.Lines.AddRange(b.Lines));
            await AddEntities(CadSectionKind.Circles, entities[typeof(CadCircle)], CadDocumentMapper.ToCirclesSection, (a, b) => a.Circles.AddRange(b.Circles));
            await AddEntities(CadSectionKind.Ellipses, entities[typeof(CadEllipse)].Concat(entities[typeof(CadEllipseArc)]), CadDocumentMapper.ToEllipsesSection,
                (a, b) => { a.Ellipses.AddRange(b.Ellipses); a.EllipseArcs.AddRange(b.EllipseArcs); });
            await AddEntities(CadSectionKind.Arcs, entities[typeof(CadArc)], CadDocumentMapper.ToArcsSection, (a, b) => a.Arcs.AddRange(b.Arcs));
            await AddEntities(CadSectionKind.Rectangles, entities[typeof(CadRectangle)], CadDocumentMapper.ToRectanglesSection, (a, b) => a.Rectangles.AddRange(b.Rectangles));
            await AddEntities(CadSectionKind.Polylines, entities[typeof(CadPolyline)], CadDocumentMapper.ToPolylinesSection, (a, b) => a.Polylines.AddRange(b.Polylines));
            await AddEntities(CadSectionKind.Splines, entities[typeof(CadSpline)], CadDocumentMapper.ToSplinesSection, (a, b) => a.Splines.AddRange(b.Splines));
            await AddEntities(CadSectionKind.CompositePaths, entities[typeof(CadCompositePath)], CadDocumentMapper.ToCompositePathsSection, (a, b) => a.CompositePaths.AddRange(b.CompositePaths));
            await AddEntities(CadSectionKind.Texts, entities[typeof(CadText)], CadDocumentMapper.ToTextsSection, (a, b) => a.Texts.AddRange(b.Texts));
            await AddEntities(CadSectionKind.ShapeTexts, entities[typeof(CadShapeText)], CadDocumentMapper.ToShapeTextsSection, (a, b) => a.ShapeTexts.AddRange(b.ShapeTexts));
            await AddEntities(CadSectionKind.Images, entities[typeof(CadImage)], CadDocumentMapper.ToImagesSection, (a, b) => a.Images.AddRange(b.Images));
            await AddEntities(CadSectionKind.OleObjects, entities[typeof(CadOleObject)], CadDocumentMapper.ToOleObjectsSection, (a, b) => a.OleObjects.AddRange(b.OleObjects));
            await AddEntities(CadSectionKind.BlockReferences, entities[typeof(CadBlockReference)], index => CadDocumentMapper.ToBlockReferencesSection(document, index), (a, b) => a.BlockReferences.AddRange(b.BlockReferences));
            CheckCurrent();
            await Task.Run(() => WriteSectionsAsync(payloads, filePath, true, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally { payloads.Clear(); }
    }
}
