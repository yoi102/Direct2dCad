using Direct2dCad.IO.FileFormat.Container;
using MessagePack;

namespace Direct2dCad.IO.Versioning;

internal sealed class CadSectionDescriptor
{
    private readonly Dictionary<int, Func<byte[], MessagePackSerializerOptions, object>> _readers = [];
    private readonly Dictionary<int, Func<object, object>> _migrations = [];

    internal CadSectionKind Kind { get; }
    internal int CurrentVersion { get; }
    internal Type CurrentModelType { get; }

    internal CadSectionDescriptor(
        CadSectionKind kind,
        int currentVersion,
        Type currentModelType)
    {
        if (currentVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(currentVersion));

        Kind = kind;
        CurrentVersion = currentVersion;
        CurrentModelType = currentModelType;
    }

    internal CadSectionDescriptor ReadsVersion<TVersion>(int version)
    {
        return ReadsVersion(version, (payload, options) =>
            MessagePackSerializer.Deserialize<TVersion>(payload, options)
            ?? throw new InvalidDataException($"Section {Kind} version {version} payload is null."));
    }

    internal CadSectionDescriptor ReadsVersion(
        int version,
        Func<byte[], MessagePackSerializerOptions, object> reader)
    {
        GuardVersion(version);
        ArgumentNullException.ThrowIfNull(reader);
        _readers[version] = reader;

        return this;
    }

    internal CadSectionDescriptor Migrates<TFrom, TTo>(
        int fromVersion,
        Func<TFrom, TTo> migrate)
        where TFrom : notnull
        where TTo : notnull
    {
        GuardVersion(fromVersion);
        ArgumentNullException.ThrowIfNull(migrate);

        _migrations[fromVersion] = value =>
        {
            if (value is not TFrom typed)
            {
                throw new InvalidDataException(
                    $"Section {Kind} migration from version {fromVersion} expected {typeof(TFrom).Name}, got {value.GetType().Name}.");
            }

            return migrate(typed);
        };

        return this;
    }

    internal object ReadCurrent(
        int storedVersion,
        byte[] payload,
        MessagePackSerializerOptions options)
    {
        GuardStoredVersion(storedVersion);

        if (!_readers.TryGetValue(storedVersion, out var reader))
        {
            throw new NotSupportedException(
                $"Section {Kind} version {storedVersion} has no registered reader.");
        }

        object current = reader(payload, options);

        for (var version = storedVersion; version < CurrentVersion; version++)
        {
            if (!_migrations.TryGetValue(version, out var migration))
            {
                throw new NotSupportedException(
                    $"Section {Kind} cannot migrate from version {version} to {version + 1}.");
            }

            current = migration(current);
        }

        if (!CurrentModelType.IsInstanceOfType(current))
        {
            throw new InvalidDataException(
                $"Section {Kind} migration ended at {current.GetType().Name}, expected {CurrentModelType.Name}.");
        }

        return current;
    }

    private static void GuardVersion(int version)
    {
        if (version <= 0)
            throw new ArgumentOutOfRangeException(nameof(version));
    }

    private void GuardStoredVersion(int storedVersion)
    {
        GuardVersion(storedVersion);

        if (storedVersion > CurrentVersion)
        {
            throw new NotSupportedException(
                $"Section {Kind} version {storedVersion} is newer than supported version {CurrentVersion}.");
        }
    }
}

