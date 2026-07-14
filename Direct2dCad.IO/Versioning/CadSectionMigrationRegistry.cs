using Direct2dCad.IO.FileFormat.Container;
using Direct2dCad.IO.FileFormat.Sections;
using MessagePack;

namespace Direct2dCad.IO.Versioning;

internal static class CadSectionMigrationRegistry
{
    private static readonly IReadOnlyDictionary<CadSectionKind, CadSectionDescriptor> Descriptors =
        new[]
        {
            //Section<CadSettingsSection>(CadSectionKind.Settings, currentVersion: 2)
            //            .ReadsVersion<CadSettingsSectionV1>(1)
            //            .ReadsVersion<CadSettingsSection>(2)
            //            .Migrates<CadSettingsSectionV1, CadSettingsSection>(1, old => new CadSettingsSection
            //            {
            //                // v1 -> v2 映射
            //            });

            Section<CadDocumentSection>(CadSectionKind.Document, currentVersion: 1)
                .ReadsVersion<CadDocumentSection>(1),

            Section<CadSettingsSection>(CadSectionKind.Settings, currentVersion: 1)
                .ReadsVersion<CadSettingsSection>(1),

            Section<CadLayerSection>(CadSectionKind.Layers, currentVersion: 1)
                .ReadsVersion<CadLayerSection>(1),

            Section<CadStylesSection>(CadSectionKind.Styles, currentVersion: 1)
                .ReadsVersion<CadStylesSection>(1),

            Section<CadLayoutsSection>(CadSectionKind.Layouts, currentVersion: 1)
                .ReadsVersion<CadLayoutsSection>(1),

            Section<CadBlocksSection>(CadSectionKind.Blocks, currentVersion: 1)
                .ReadsVersion<CadBlocksSection>(1),

            Section<CadLinesSection>(CadSectionKind.Lines, currentVersion: 1)
                .ReadsVersion<CadLinesSection>(1),

            Section<CadCirclesSection>(CadSectionKind.Circles, currentVersion: 1)
                .ReadsVersion<CadCirclesSection>(1),

            Section<CadEllipsesSection>(CadSectionKind.Ellipses, currentVersion: 1)
                .ReadsVersion<CadEllipsesSection>(1),

            Section<CadArcsSection>(CadSectionKind.Arcs, currentVersion: 1)
                .ReadsVersion<CadArcsSection>(1),

            Section<CadRectanglesSection>(CadSectionKind.Rectangles, currentVersion: 1)
                .ReadsVersion<CadRectanglesSection>(1),

            Section<CadPolylinesSection>(CadSectionKind.Polylines, currentVersion: 1)
                .ReadsVersion<CadPolylinesSection>(1),

            Section<CadSplinesSection>(CadSectionKind.Splines, currentVersion: 1)
                .ReadsVersion<CadSplinesSection>(1),

            Section<CadTextsSection>(CadSectionKind.Texts, currentVersion: 1)
                .ReadsVersion<CadTextsSection>(1),

            Section<CadShapeTextsSection>(CadSectionKind.ShapeTexts, currentVersion: 1)
                .ReadsVersion<CadShapeTextsSection>(1),

            Section<CadImagesSection>(CadSectionKind.Images, currentVersion: 1)
                .ReadsVersion<CadImagesSection>(1),

            Section<CadOleObjectsSection>(CadSectionKind.OleObjects, currentVersion: 1)
                .ReadsVersion<CadOleObjectsSection>(1),

            Section<CadBlockReferencesSection>(CadSectionKind.BlockReferences, currentVersion: 1)
                .ReadsVersion<CadBlockReferencesSection>(1)
        }
        .ToDictionary(x => x.Kind);

    internal static int GetCurrentVersion(CadSectionKind kind)
    {
        return GetDescriptor(kind).CurrentVersion;
    }

    internal static IReadOnlyList<CadSectionVersionInfo> GetVersionInfo()
    {
        return Descriptors.Values
            .OrderBy(x => (ushort)x.Kind)
            .Select(x => new CadSectionVersionInfo(
                x.Kind,
                x.CurrentVersion,
                x.CurrentModelType.Name))
            .ToArray();
    }

    internal static TSection ReadCurrent<TSection>(
        CadSectionKind kind,
        int storedVersion,
        byte[] payload,
        MessagePackSerializerOptions options)
    {
        var current = GetDescriptor(kind).ReadCurrent(storedVersion, payload, options);

        if (current is not TSection typed)
        {
            throw new InvalidDataException(
                $"Section {kind} migrated to {current.GetType().Name}, not {typeof(TSection).Name}.");
        }

        return typed;
    }

    private static CadSectionDescriptor GetDescriptor(CadSectionKind kind)
    {
        return Descriptors.TryGetValue(kind, out var descriptor)
            ? descriptor
            : throw new NotSupportedException($"Unsupported section kind: {kind}");
    }

    private static CadSectionDescriptor Section<TCurrent>(
        CadSectionKind kind,
        int currentVersion)
    {
        return new CadSectionDescriptor(kind, currentVersion, typeof(TCurrent));
    }
}
