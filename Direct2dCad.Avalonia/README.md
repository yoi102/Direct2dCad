# Direct2dCad Avalonia for Windows

Windows x64 client using Avalonia 12.0.5, .NET 10 NativeAOT and typed compiled XAML bindings. Material.Avalonia and its DataGrid styles 3.19.0 provide the core control themes; DialogHost.Avalonia 0.12.3 provides in-window modal overlays. Dock.Avalonia / Dock.Model.Mvvm / Dock.Avalonia.Themes.Fluent 12.0.0.2 provide native docking with compact initial tool groups, six available destinations, floating, automatic hiding and layout persistence. The target is equivalent operations and approximate WPF layout; all third-party UI libraries may retain similar native control styles. Official ColorPicker retains its Fluent fallback theme. The executable is self-contained; keep its bundled graphics DLLs beside it. OLE and vector printing use C# with Microsoft.Windows.CsWin32 0.3.333; no custom C++ source or Direct2dCad.Ole.Native.dll is required.

Managed build prerequisites: .NET 10 SDK. NativeAOT publication additionally requires the Windows SDK and MSVC native linker through Visual Studio Build Tools, as required by .NET NativeAOT. There is no custom C++ compilation step. Run from PowerShell:

```powershell
./Direct2dCad.Avalonia/publish-windows.ps1 -ReleaseVersion 0.1.6 -InstallerVersion 1.0.6
```

Use `-HeadlessValidation` to run the same in-process checks without any desktop windows, mouse or keyboard input. This uses Avalonia.Headless for test windowing while retaining the actual NativeAOT executable, Skia text/control rendering and hardware Direct2D CAD rendering. It does not certify Win32 input, focus, dialogs or physical display behavior.

The script publishes versioned NativeAOT binaries, runs isolated native and GPU validation, checks the executable SHA256 against its report, and creates a portable ZIP plus a separate Windows MSI under `.artifacts/`. The MSI is a distinct product from the WPF edition, so both can be installed side by side; its installer lets the user choose the destination folder and creates Start menu and desktop shortcuts. Each module set has its own portable folder with a hash suffix, so an already running previous build is preserved. Detailed publication warnings are retained in `.artifacts/avalonia-native-publish.log`. Use `-SkipValidation` only for an explicitly unvalidated package.

Generated views are checked in. After changing WPF property/ribbon forms, regenerate them at development time:

```powershell
dotnet run --project tools/Direct2dCad.Avalonia.Generate -- .
```

The generator uses WPF at development time to read original icon geometries and view metadata. The deployed Avalonia application has no WPF reference. All application XAML views declare a typed data context and compile their bindings; view selection uses a static type switch. Storage/settings use generated MessagePack and JSON metadata. Heterogeneous AI results use preserved DTO property metadata without runtime code generation.

The client shares the existing document/editor, tools, undo/redo, IO, recovery, AI and Direct2D rendering engine. It includes multiple document tabs, layouts/viewports, ribbon, property forms, terminal, layers/blocks/search/filter/messages/recovery/AI panels, docking/floating, settings, file/image/clipboard/OLE services and Windows printing.

Settings are stored under `%APPDATA%/Direct2dCad/Avalonia`. When no Avalonia settings exist, general settings are initially read from the WPF settings without rewriting the WPF file. Validation uses separate directories through `DIRECT2DCAD_SETTINGS_DIRECTORY` and `DIRECT2DCAD_RECOVERY_DIRECTORY`.

The canvas uses keyed-mutex shared D3D11 textures imported by the Avalonia compositor, with a CPU bitmap fallback for unsupported/headless configurations. Windows printing records Direct2D command lists and submits vector pages through the Windows print pipeline. Ribbon grouping/icons, 43 canvas context commands, six dock zones, status controls, dynamic input and property forms follow the WPF client. Earlier binaries were exercised with actual Excel embedding/edit/save-back and Microsoft Print to PDF. The C# interop migration passes native OLE storage/drawing/callback, vector XPS and installed-driver PrintTicket checks; actual Office editing and submitted print jobs have not been repeated on this binary. Full visual and interaction parity across every tool, DPI, IME and physical device is not yet certified. NativeAOT publication retains dependency trimming/AOT analysis warnings; see the status document and publish log.

Property forms preserve WPF titles, nested expandable sections and expansion defaults, numeric formats/ranges/steps, ByLayer read-only values and color-source gating. Numeric display never writes rounded geometry back to the model. Panel edge buttons restore hidden tools; numeric property focus supports document undo/redo. Layers expose priority/count/weight, blocks expose identifiers/counts, and terminal entries include timestamps. Ribbon accents retain readable contrast in both themes.

Delivery evidence and verification boundaries: `AVALONIA-WINDOWS.md` and `validation.json` in the portable folder (repository copies live under `docs/`). The current executable check count and GPU frames are recorded in validation.json; real Windows input, Excel roundtrip, local model tools and vector PDF output are recorded separately by executable hash. UI changes and remaining differences are listed in AVALONIA-UI-PARITY.md.



UI library review and remaining WPF differences: `AVALONIA-UI-LIBRARIES.md` and `AVALONIA-UI-PARITY.md`. New UI library and font license texts are included in `THIRD-PARTY-UI-LICENSES.txt`. The latest validation.json is bound to the executable SHA256; historical desktop observations do not certify a new binary.


Docking uses a local VS-style compass: center joins tabs and directional targets perform native splits. Unfit directions and whole-workspace arrows are hidden. The default has Documents/Layers on the left, full-height Properties on the right and Terminal at the bottom. View/shortcuts reopen closed tools; native auto-hide tabs are the sole edge strip. Layout version 2 preserves new layouts and backs up legacy layouts before starting from these defaults.

Ribbon file dropdowns use native menus with Down/Enter/Escape navigation and close after selection. Escape lets the focused control dismiss its popup first. Property Enter validates and returns to the canvas without finishing the drawing; invalid numeric values block Save/Save As/Print shortcuts, and Escape restores their current source value. The same rules apply in floating tools. File/panel menu shortcuts are displayed, and standalone vector paths retain their original Viewbox measurement.

Drag a pane title to move its group, or a tab to move one tool. The floating pane menu offers Dock to return to its previous pane. Escape cancels an active dock drag without cancelling an unfinished CAD command.

The main window integrates File, theme, language and topmost controls into one 32px title bar. Avalonia's decoration infrastructure owns its caption actions and exposes Windows non-client roles, including the maximize-button Snap Layouts flyout when enabled by the OS. Settings and previews use full system title bars. Floating Dock panes are borderless and use Dock's own title grip, maximize/restore and Close actions, with a 4 DIP gutter for edge/corner resizing. The same rules apply to single tools, multiple tabs and restored floating layouts. Drag the Dock title or a tool tab to move or dock a floating pane.

Closing the main window always asks for exit confirmation, matching WPF even without open or modified drawings. Confirming exit then checks unsaved drawings; Cancel or Escape keeps the application open. Save exits only after saving succeeds; Don't save exits without writing changes. Repeated Close requests reuse the pending confirmation.

The original application icon opens the Windows system menu, and View is beside File. Docking context menus belong to pane titles and tool tabs. Layer content instead exposes layer actions using the existing command and undo paths. Documents and Recovery accept context clicks across row padding and blank list space, targeting the clicked row or current selection. Canvas right clicks open the compiled editor menu explicitly after capture is released; a right drag remains pan. The 248px radial menu uses a transparent vector overlay inside the owner window, with no separate opaque popup surface.
