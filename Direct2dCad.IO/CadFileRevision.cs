using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.IO;

public sealed record CadFileRevision(string FullPath, bool Exists, long Length, string Hash)
{
    public static CadFileRevision Capture(string path)
    {
        path = Path.GetFullPath(path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return new(path, true, stream.Length, Convert.ToHexString(SHA256.HashData(stream)));
        }
        catch (FileNotFoundException) { return new(path, false, 0, ""); }
        catch (DirectoryNotFoundException) { return new(path, false, 0, ""); }
    }

    public bool Matches(CadFileRevision other) =>
        string.Equals(FullPath, other.FullPath, StringComparison.OrdinalIgnoreCase) &&
        Exists == other.Exists && Length == other.Length && Hash == other.Hash;

    public void Verify()
    {
        var current = Capture(FullPath);
        if (!Matches(current)) throw new CadFileConflictException(current);
    }
}

public sealed class CadFileConflictException(CadFileRevision current) : IOException(
    $"The destination already exists or changed outside this document: {current.FullPath}. Choose another path or explicitly authorize replacement of this revision.")
{
    public CadFileRevision CurrentRevision { get; } = current;
}

/// <summary>Runtime origin metadata is deliberately excluded from the document format.</summary>
public static class CadDocumentOrigin
{
    private static readonly ConditionalWeakTable<CadDocument, Origin> Origins = new();
    private sealed record Origin(CadFileRevision Revision);
    public static void Set(CadDocument document, CadFileRevision revision)
    {
        Origins.Remove(document);
        Origins.Add(document, new(revision));
    }
    public static CadFileRevision? Get(CadDocument document) => Origins.TryGetValue(document, out var origin) ? origin.Revision : null;
}
