using Direct2dCad.Db.Cad;

namespace Direct2dCad.IO.Tests;

public sealed class CadDocumentStorageFailureTests
{
    private const int ContainerVersionOffset = 5;
    private const int SectionCountOffset = 9;
    private const int SectionTableOffsetOffset = 13;
    private const int SectionTableLengthOffset = 21;
    private const int FirstSectionKindOffset = 25;
    private const int FirstSectionCompressionOffset = 31;
    private const int FirstSectionPayloadOffset = 32;
    private const int SectionEntryLength = 19;
    private const int SecondSectionPayloadOffset = FirstSectionPayloadOffset + SectionEntryLength;

    [Fact]
    public async Task LoadAsync_WithPreCanceledTokenDoesNotAccessFileSystem()
    {
        var missingPath = CreateTempPath();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new CadDocumentStorage().LoadAsync(missingPath, cancellation.Token));

        Assert.False(File.Exists(missingPath));
    }

    [Fact]
    public async Task LoadAsync_UnsupportedContainerVersionThrowsNotSupportedException()
    {
        await AssertCorruptFileThrowsAsync<NotSupportedException>(
            bytes => WriteInt32(bytes, ContainerVersionOffset, int.MaxValue));
    }

    [Fact]
    public async Task LoadAsync_ExcessiveSectionCountThrowsInvalidDataExceptionBeforeAllocation()
    {
        await AssertCorruptFileThrowsAsync<InvalidDataException>(bytes =>
        {
            const int excessiveSectionCount = 4097;
            WriteInt32(bytes, SectionCountOffset, excessiveSectionCount);
            WriteInt32(
                bytes,
                SectionTableLengthOffset,
                excessiveSectionCount * SectionEntryLength);
        });
    }

    [Fact]
    public async Task SaveAsync_WithPreCanceledTokenPreservesExistingFile()
    {
        var path = CreateTempPath();
        var originalBytes = "existing document"u8.ToArray();
        try
        {
            await File.WriteAllBytesAsync(path, originalBytes);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => new CadDocumentStorage().SaveAsync(
                    CadDocument.Create("Canceled save"),
                    path,
                    cancellation.Token));

            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task SaveAsync_ReplacesExistingFileAndLeavesNoTemporaryFile()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "drawing.d2cad");
        try
        {
            await File.WriteAllBytesAsync(path, "old contents"u8.ToArray());
            var storage = new CadDocumentStorage();

            await storage.SaveAsync(CadDocument.Create("Atomic save"), path);

            Assert.Equal("Atomic save", (await storage.LoadAsync(path)).Name);
            Assert.Empty(Directory.EnumerateFiles(directory, ".drawing.d2cad.*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_WhenCommitFailsPreservesExistingFileAndCleansTemporaryFile()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "drawing.d2cad");
        var originalBytes = "original contents"u8.ToArray();
        try
        {
            await File.WriteAllBytesAsync(path, originalBytes);
            await using (var lockedDestination = new FileStream(
                             path,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read))
            {
                var exception = await Record.ExceptionAsync(
                    () => new CadDocumentStorage().SaveAsync(
                        CadDocument.Create("Must not replace"),
                        path));
                Assert.True(
                    exception is IOException or UnauthorizedAccessException,
                    $"Unexpected commit exception: {exception?.GetType().FullName ?? "none"}");
            }

            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.EnumerateFiles(directory, ".drawing.d2cad.*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_SectionTableOutsideFileThrowsInvalidDataException()
    {
        await AssertCorruptFileThrowsAsync<InvalidDataException>(
            bytes => WriteInt64(bytes, SectionTableOffsetOffset, long.MaxValue));
    }

    [Fact]
    public async Task LoadAsync_SectionPayloadOutsideFileThrowsInvalidDataException()
    {
        await AssertCorruptFileThrowsAsync<InvalidDataException>(
            bytes => WriteInt64(bytes, FirstSectionPayloadOffset, long.MaxValue));
    }

    [Fact]
    public async Task LoadAsync_SectionPayloadInsideTableThrowsInvalidDataException()
    {
        await AssertCorruptFileThrowsAsync<InvalidDataException>(
            bytes => WriteInt64(bytes, FirstSectionPayloadOffset, 25));
    }

    [Fact]
    public async Task LoadAsync_OverlappingSectionPayloadsThrowInvalidDataException()
    {
        await AssertCorruptFileThrowsAsync<InvalidDataException>(bytes =>
        {
            var firstPayloadOffset = BitConverter.ToInt64(bytes, FirstSectionPayloadOffset);
            WriteInt64(bytes, SecondSectionPayloadOffset, firstPayloadOffset);
        });
    }

    [Fact]
    public async Task LoadAsync_MissingRequiredDocumentSectionThrowsInvalidDataException()
    {
        await AssertCorruptFileThrowsAsync<InvalidDataException>(bytes =>
        {
            bytes[FirstSectionKindOffset] = 0xFE;
            bytes[FirstSectionKindOffset + 1] = 0x7F;
        });
    }

    [Fact]
    public async Task LoadAsync_UnsupportedCompressionThrowsNotSupportedException()
    {
        await AssertCorruptFileThrowsAsync<NotSupportedException>(
            bytes => bytes[FirstSectionCompressionOffset] = byte.MaxValue);
    }

    private static async Task AssertCorruptFileThrowsAsync<TException>(
        Action<byte[]> corrupt)
        where TException : Exception
    {
        var path = CreateTempPath();
        try
        {
            var storage = new CadDocumentStorage();
            await storage.SaveAsync(CadDocument.Create("Corrupt input"), path);
            var bytes = await File.ReadAllBytesAsync(path);
            corrupt(bytes);
            await File.WriteAllBytesAsync(path, bytes);

            await Assert.ThrowsAsync<TException>(() => storage.LoadAsync(path));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void WriteInt32(byte[] bytes, int offset, int value) =>
        BitConverter.GetBytes(value).CopyTo(bytes, offset);

    private static void WriteInt64(byte[] bytes, int offset, long value) =>
        BitConverter.GetBytes(value).CopyTo(bytes, offset);

    private static string CreateTempPath() =>
        Path.Combine(Path.GetTempPath(), $"Direct2dCad-{Guid.NewGuid():N}.d2cad");

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Direct2dCad-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
