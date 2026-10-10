using System.Runtime.InteropServices;
using System.Text;

namespace Direct2dCad.Avalonia.Services;

internal sealed record WindowsPaper(short Id, string Name, double WidthMm, double HeightMm)
{
    public override string ToString() => $"{Name} · {WidthMm:0.#} × {HeightMm:0.#} mm";
}

/// <summary>Owns a full driver DEVMODE, including private bytes. No System.Printing or runtime type generation.</summary>
internal sealed class WindowsPrinterSettings : IDisposable
{
    public string Name { get; }
    public nint Mode { get; private set; }
    private readonly int _size;
    public IReadOnlyList<WindowsPaper> Papers { get; }
    public short PaperId => Marshal.ReadInt16(Mode, 78);
    public bool Landscape => Marshal.ReadInt16(Mode, 76) == 2;
    public int Copies => Math.Clamp((int)Marshal.ReadInt16(Mode, 86), 1, 999);
    public int Dpi => Math.Clamp((int)Marshal.ReadInt16(Mode, 90), 72, 1200);
    public static IReadOnlyList<string> Printers()
    {
        EnumPrintersW(6, null, 4, 0, 0, out var bytes, out _);
        if (bytes == 0) return [];
        var buffer = Marshal.AllocHGlobal(checked((int)bytes));
        try
        {
            if (!EnumPrintersW(6, null, 4, buffer, bytes, out _, out var count)) throw new System.ComponentModel.Win32Exception();
            var result = new List<string>(); var stride = IntPtr.Size == 8 ? 24 : 12;
            for (var i = 0; i < count; i++) if (Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer, i * stride)) is { Length: > 0 } name) result.Add(name);
            return result.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    public static string? DefaultPrinter()
    {
        uint size = 0; GetDefaultPrinterW(null, ref size);
        if (size == 0) return null;
        var name = new StringBuilder((int)size); return GetDefaultPrinterW(name, ref size) ? name.ToString() : null;
    }
    public WindowsPrinterSettings(string name)
    {
        Name = name;
        if (!OpenPrinterW(name, out var printer, 0)) throw new System.ComponentModel.Win32Exception();
        try
        {
            _size = DocumentPropertiesW(0, printer, name, 0, 0, 0);
            if (_size < 220) throw new IOException("The printer returned an invalid DEVMODE.");
            Mode = Marshal.AllocHGlobal(_size);
            if (DocumentPropertiesW(0, printer, name, Mode, 0, 2) != 1) throw new IOException("The printer configuration could not be read.");
            Papers = ReadPapers();
        }
        catch { Dispose(); throw; }
        finally { ClosePrinter(printer); }
    }
    private IReadOnlyList<WindowsPaper> ReadPapers()
    {
        var count = DeviceCapabilitiesW(Name, null, 2, 0, Mode);
        if (count <= 0 || count > 4096) throw new IOException("The printer returned no paper sizes.");
        var ids = Marshal.AllocHGlobal(count * 2); var names = Marshal.AllocHGlobal(count * 128); var sizes = Marshal.AllocHGlobal(count * 8);
        try
        {
            if (DeviceCapabilitiesW(Name, null, 2, ids, Mode) != count || DeviceCapabilitiesW(Name, null, 16, names, Mode) != count || DeviceCapabilitiesW(Name, null, 3, sizes, Mode) != count) throw new IOException("The printer paper capabilities changed during enumeration.");
            var papers = new List<WindowsPaper>();
            for (var i = 0; i < count; i++)
            {
                var title = Marshal.PtrToStringUni(names + i * 128, 64)!.Split('\0')[0].Trim();
                var width = Marshal.ReadInt32(sizes, i * 8) / 10d; var height = Marshal.ReadInt32(sizes, i * 8 + 4) / 10d;
                if (width > 0 && height > 0) papers.Add(new(Marshal.ReadInt16(ids, i * 2), title, width, height));
            }
            return papers;
        }
        finally { Marshal.FreeHGlobal(ids); Marshal.FreeHGlobal(names); Marshal.FreeHGlobal(sizes); }
    }
    public void Apply(short paper, bool landscape, int copies, int dpi)
    {
        if (!Papers.Any(p => p.Id == paper)) throw new ArgumentOutOfRangeException(nameof(paper));
        Marshal.WriteInt32(Mode, 72, Marshal.ReadInt32(Mode, 72) | 0x1 | 0x2 | 0x100 | 0x400 | 0x2000);
        Marshal.WriteInt16(Mode, 76, (short)(landscape ? 2 : 1)); Marshal.WriteInt16(Mode, 78, paper);
        Marshal.WriteInt16(Mode, 86, (short)Math.Clamp(copies, 1, 999));
        Marshal.WriteInt16(Mode, 90, (short)Math.Clamp(dpi, 72, 1200)); Marshal.WriteInt16(Mode, 96, (short)Math.Clamp(dpi, 72, 1200));
        Validate(0, false);
    }
    public bool ShowDriverSettings(nint owner) => Validate(owner, true);
    private bool Validate(nint owner, bool prompt)
    {
        if (!OpenPrinterW(Name, out var printer, 0)) throw new System.ComponentModel.Win32Exception();
        var next = Marshal.AllocHGlobal(_size);
        try
        {
            var result = DocumentPropertiesW(owner, printer, Name, next, Mode, prompt ? 14 : 10);
            if (prompt && result == 2) return false;
            if (result != 1) throw new IOException("The printer rejected the selected settings.");
            var old = Mode; Mode = next; next = old; return true;
        }
        finally { Marshal.FreeHGlobal(next); ClosePrinter(printer); }
    }
    public nint CreateContext()
    {
        var dc = CreateDCW("WINSPOOL", Name, null, Mode); if (dc == 0) throw new System.ComponentModel.Win32Exception(); return dc;
    }
    public void Dispose() { if (Mode != 0) { Marshal.FreeHGlobal(Mode); Mode = 0; } }
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumPrintersW(uint flags, string? name, uint level, nint buffer, uint bytes, out uint needed, out uint count);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetDefaultPrinterW(StringBuilder? name, ref uint size);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenPrinterW(string name, out nint printer, nint defaults);
    [DllImport("winspool.drv")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClosePrinter(nint printer);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode)] private static extern int DocumentPropertiesW(nint owner, nint printer, string name, nint output, nint input, int flags);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode)] private static extern int DeviceCapabilitiesW(string device, string? port, short capability, nint output, nint mode);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateDCW(string driver, string device, string? output, nint mode);
}
