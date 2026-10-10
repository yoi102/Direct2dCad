# Windows OLE and vector printing

C# Windows API interop backed by Microsoft.Windows.CsWin32 0.3.333 (build-time NuGet dependency). Generated SDK declarations are never hand-edited. `NativeMethods.txt` selects APIs; `NativeMethods.json` requests blittable pointer bindings, and runtime marshalling is disabled for this assembly. NativeAOT compiles this project into the application executable.

CsWin32's COM callback thunks are connected to the .NET `ComWrappers` IUnknown implementation. OLE client-site and advise interfaces share identity. Sessions own storage, object, site and advise references; closing suppresses callbacks before releasing them. All OLE operations remain on their creating apartment thread. The app's existing WPF storage envelope stays outside this project.

Vector printing uses the same Direct2D command list, driver-validated DEVMODE and print-ticket APIs as before. XPS export owns a C# package target and refuses an existing output file. Windows 10 or later is required for this printing entry point.

There is no custom C++ source, compiler invocation or bridge DLL. .NET NativeAOT still requires its Windows SDK/native linker toolchain.
