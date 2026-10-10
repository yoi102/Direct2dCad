using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace Direct2dCad.Avalonia.Services;

internal static partial class WindowsSystemMenu
{
    internal static void Show(Window window, PixelPoint point)
    {
        var hwnd=window.TryGetPlatformHandle()?.Handle??0;
        if(hwnd==0 || !OperatingSystem.IsWindows()) return;
        var menu=GetSystemMenu(hwnd,0);
        if(menu==0) return;
        void Enable(uint command,bool enabled)=>EnableMenuItem(menu,command,enabled?0u:1u);
        Enable(0xF120,window.WindowState!=WindowState.Normal);
        Enable(0xF010,window.WindowState==WindowState.Normal);
        Enable(0xF000,window.CanResize && window.WindowState==WindowState.Normal);
        Enable(0xF020,window.CanMinimize);
        Enable(0xF030,window.CanMaximize && window.WindowState!=WindowState.Maximized);
        var command=TrackPopupMenu(menu,0x100u|0x2u,point.X,point.Y,0,hwnd,0);
        if(command!=0)SendMessage(hwnd,0x112,(nint)command,0); // WM_SYSCOMMAND retains the normal closing/unsaved path.
    }
    [LibraryImport("user32.dll")] private static partial nint GetSystemMenu(nint hwnd,int revert);
    [LibraryImport("user32.dll")] private static partial uint EnableMenuItem(nint menu,uint command,uint flags);
    [LibraryImport("user32.dll")] private static partial uint TrackPopupMenu(nint menu,uint flags,int x,int y,int reserved,nint hwnd,nint rect);
    [LibraryImport("user32.dll",EntryPoint="SendMessageW")] private static partial nint SendMessage(nint hwnd,uint message,nint wParam,nint lParam);
}
