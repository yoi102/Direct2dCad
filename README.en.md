# Direct2dCad

[中文](README.md) | [日本語](README.ja.md) | [English](README.en.md)

Direct2dCad is a 2D CAD editor for Windows, built with WPF, Direct2D, and DirectWrite. It brings drawing, precise numeric input, editing, dimensions, and drawing management into one desktop workspace, with a command line and AI assistance.

## Features

- **2D drawing**: Lines, polylines, polygons, rectangles, circles, arcs, ellipses, ellipse arcs, and splines, plus text, images, and OLE objects.
- **Precise input**: Enter coordinates, lengths, radii, diameters, angles, and ellipse semiaxes directly on the canvas. Use object snaps, grid snaps, orthogonal constraints, and polar tracking.
- **Editing**: Click, window, and crossing selection, selection filters, and grips; move, rotate, mirror, and scale. Compatible curves support offset, trim, extend, fillet, chamfer, join, break, and arrays. Closed contours support Boolean union, intersection, and difference.
- **Appearance and dimensions**: Colors, line weights, dashes, caps, joins, fills, and patterns; horizontal, vertical, aligned, radius, diameter, and angular dimensions, plus leaders with configurable fonts and arrows.
- **Drawing organization**: Multiple documents, layers, blocks, nested block references, block editing, layouts, and model-space viewports, with undo/redo and copying between drawings.
- **Files and output**: Native `.d2cad` files, DXF import/export for common 2D entities, automatic recovery, print preview, and printing to scale.
- **Command line and AI**: Terminal provides help, completion, and command history. Connect LM Studio or Codex to query drawings, create geometry, and perform undoable edits.

The interface supports Chinese, Japanese, and English, with light and dark themes, dockable toolboxes, and configurable radial menus.

## Download and install

Download the Windows x64 version from [GitHub Releases](https://github.com/yoi102/Direct2dCad/releases):

- **MSI installer**: Installs the application and creates desktop and Start menu shortcuts.
- **Portable ZIP**: Extract the archive and run `Direct2dCad.exe`.

Both packages include the required .NET Runtime; no separate runtime installation is needed.

## Quick start

1. Create a drawing or open a `.d2cad` or DXF file.
2. Choose a tool on the Draw tab. Place points with the mouse or enter parameters in the compact fields on the canvas.
3. Select geometry to adjust its layer and appearance in the Properties panel, edit it from the Modify tab, or add dimensions from the Dimensions tab.
4. Save as `.d2cad`, export DXF, or use layouts and print preview to print the drawing.

Common controls:

- `Tab` / `Shift+Tab` cycles through numeric fields. `Enter` accepts the current input; for multipoint drawing, press `Enter` again after accepting the final point to finish.
- `Esc` cancels the current operation and returns to selection.
- Use the mouse wheel to zoom and the right or middle button to pan. Grip edits show a preview and commit with another left click.
- Enter `HELP` in Terminal for commands, or `TOOLS` / `TOOLHELP` for AI tools.

## AI connections

Open connection settings with the gear button in the AI toolbox:

- **LM Studio**: Start Local Server and load a model that supports tool calling. The default endpoint is `http://localhost:1234/v1`.
- **Codex**: Connect to `codex app-server` using the local Codex CLI login.

AI can query entities, layers, and blocks, create or modify geometry, and open, save, and switch drawings. Edits enter the drawing's undo history and remain available for manual adjustment.

## Run from source

Requires Windows x64 and the .NET 10 SDK. The repository's `global.json` specifies SDK 10.0.401 and permits patch updates within the same feature band.

Run from the repository root:

```powershell
dotnet build .\Direct2dCad.slnx -c Release
dotnet run -c Release --project .\Direct2dCad.wpf\Direct2dCad.wpf.csproj
```

Create a publish directory that includes the runtime:

```powershell
dotnet publish .\Direct2dCad.wpf\Direct2dCad.wpf.csproj -c Release -r win-x64 --self-contained true
```

## Demos and design

- [Basic editing 1](https://github.com/user-attachments/assets/53180795-5870-42c7-9148-5586ca1bfd6b), [Basic editing 2](https://github.com/user-attachments/assets/5515d18a-1d88-4851-a8d9-54f10bdee5ed)
- [Blocks](https://github.com/user-attachments/assets/45c5e49e-c59a-4f80-aaf3-de8ec7680310)
- [Layouts](https://github.com/user-attachments/assets/847600ec-c82e-4ed0-82d9-443d59339906)
- [OLE objects](https://github.com/user-attachments/assets/ab1f207f-48c2-40a8-b698-496c6077a0a3)
- [Terminal](https://github.com/user-attachments/assets/fc7236e2-93e8-44f3-800d-b00bfd54f761)
- [LM Studio AI 1](https://github.com/user-attachments/assets/ebb26f5b-63a1-4159-a101-69da56e776a7), [AI 2](https://github.com/user-attachments/assets/63a6763b-b63c-4a29-a499-cadb94242509)
- [Figma design](https://www.figma.com/board/wZWqWgQ9dd1p4KQVBakqmS/Direct2dCad?node-id=52-299&t=jXGAkAOnYQmodsTk-4)

## License

This project is licensed under the [MIT License](LICENSE.txt).
