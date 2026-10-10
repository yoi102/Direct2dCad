using System.Diagnostics;
using System.Runtime.InteropServices;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.Avalonia.Services;

internal sealed class FileDialogs : IFileDialogService
{
    public string? SaveAsD2cad(string name) => Choose("Direct2dCad\0*.d2cad\0", name, true, "d2cad");
    public string? OpenD2cadFile() => Choose("CAD files\0*.d2cad;*.dxf\0All files\0*.*\0", "", false);
    public string? OpenFile() => Choose("All files\0*.*\0", "", false);
    public string? OpenImageFile() => Choose("Images\0*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp\0", "", false);
    public string? OpenDxfFile() => Choose("DXF\0*.dxf\0", "", false);
    public string? ExportDxfFile(string name) => Choose("DXF\0*.dxf\0", name, true, "dxf");
    private static string? Choose(string filter, string name, bool save, string extension = "")
    {
        var file = Marshal.AllocHGlobal(32768 * 2);
        var filterPointer = Marshal.StringToHGlobalUni(filter + "\0");
        var extensionPointer = Marshal.StringToHGlobalUni(extension);
        try
        {
            Marshal.WriteInt16(file, 0); var chars = (name + '\0').ToCharArray(); Marshal.Copy(chars, 0, file, chars.Length);
            var options = new OpenFileName { Size = Marshal.SizeOf<OpenFileName>(), Owner = App.OwnerHandle, Filter = filterPointer, File = file, MaximumFile = 32768, Flags = 0x00080000 | 0x00000008 | (save ? 0x2 : 0x1000), DefaultExtension = extensionPointer };
            var success = save ? GetSaveFileNameW(ref options) : GetOpenFileNameW(ref options);
            if (success) return Marshal.PtrToStringUni(file);
            var error = CommDlgExtendedError(); if (error != 0) throw new IOException($"Windows file dialog failed: 0x{error:X}");
            return null;
        }
        finally { Marshal.FreeHGlobal(file); Marshal.FreeHGlobal(filterPointer); Marshal.FreeHGlobal(extensionPointer); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct OpenFileName
    {
        public int Size; public nint Owner, Instance, Filter, CustomFilter; public int MaximumCustomFilter, FilterIndex; public nint File; public int MaximumFile; public nint FileTitle; public int MaximumFileTitle; public nint InitialDirectory, Title; public int Flags; public short FileOffset, FileExtension; public nint DefaultExtension, CustomData, Hook, TemplateName, Reserved; public int Reserved2, FlagsEx;
    }
    [DllImport("comdlg32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetOpenFileNameW(ref OpenFileName options);
    [DllImport("comdlg32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSaveFileNameW(ref OpenFileName options);
    [DllImport("comdlg32.dll")] private static extern int CommDlgExtendedError();
}
internal sealed class ClipboardTextService : IClipboardTextService
{
    internal static bool HasAttachment() => IsClipboardFormatAvailable(8) || IsClipboardFormatAvailable(15);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsClipboardFormatAvailable(uint format);
    public string? LoadFromClipboard()
    {
        if (!OpenClipboard(App.OwnerHandle)) return null;
        try { var data = GetClipboardData(13); if (data == 0) return null; var pointer = GlobalLock(data); try { return pointer == 0 ? null : Marshal.PtrToStringUni(pointer); } finally { if (pointer != 0) GlobalUnlock(data); } }
        finally { CloseClipboard(); }
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseClipboard();
    [DllImport("user32.dll")] internal static extern nint GetClipboardData(uint format);
    [DllImport("kernel32.dll")] internal static extern nint GlobalLock(nint handle);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GlobalUnlock(nint handle);
}
internal sealed class FileLocationService : IFileLocationService
{
    public bool CanOpenContainingFolder(string? path) { try { return !string.IsNullOrWhiteSpace(path) && Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(path))); } catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException) { return false; } }
    public void OpenContainingFolder(string path) { if (CanOpenContainingFolder(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Path.GetFullPath(path)}\"") { UseShellExecute = true }); }
}

