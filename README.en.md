# Direct2dCad

[中文](README.md) | [日本語](README.ja.md) | [English](README.en.md)

Direct2dCad is a 2D CAD editor for Windows, with WPF and Avalonia desktop clients using Direct2D and DirectWrite. It brings drawing, precise numeric input, editing, dimensions, and drawing management into one desktop workspace, with a command line and AI assistance.

## Features

- **2D drawing**: Lines, polylines, polygons, rectangles, circles, arcs, ellipses, ellipse arcs, splines, and NURBS curves, plus text, images, and OLE objects.
- **Precise input**: Enter coordinates, lengths, radii, diameters, angles, and ellipse semiaxes directly on the canvas. Use object snaps, grid snaps, orthogonal constraints, and polar tracking.
- **Editing**: Click, window, and crossing selection, selection filters, and grips; move, rotate, mirror, and scale. Compatible curves support offset, trim, extend, fillet, chamfer, join, break, and arrays. Closed contours support Boolean union, intersection, and difference.
- **Appearance and dimensions**: Colors, line weights, dashes, caps, joins, fills, and patterns; horizontal, vertical, aligned, radius, diameter, and angular dimensions, plus leaders with configurable fonts and arrows.
- **Drawing organization**: Multiple documents, layers, blocks, nested block references, block editing, layouts, and model-space viewports, with undo/redo and copying between drawings.
- **Files and output**: Native `.d2cad` files, DXF import/export for common 2D entities, automatic recovery, print preview, and printing to scale.
- **Command line and AI**: Terminal provides help, completion, and command history. Connect LM Studio or Codex to query drawings, create geometry, and perform undoable edits.

The interface supports Chinese, Japanese, and English, with light and dark themes, dockable toolboxes, and configurable radial menus.

## Download and install

Download the Windows x64 version from [GitHub Releases](https://github.com/yoi102/Direct2dCad/releases):

- **WPF edition**: Choose an MSI or ZIP with `Wpf` in its filename. Run `Direct2dCad.exe` from the portable package.
- **Avalonia edition**: Choose an MSI or ZIP with `Avalonia` in its filename. Run `Direct2dCad.Avalonia.exe` from the portable package.

Neither edition requires a separately installed .NET Runtime: WPF bundles its runtime, while Avalonia uses NativeAOT. Both MSI installers offer an installation directory choice and desktop and Start menu shortcuts. The editions can be installed side by side.

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

## MCP connection

Both desktop clients expose the live workspace through MCP Streamable HTTP using the official `ModelContextProtocol.AspNetCore` NuGet package. Launch either executable with `--mcp` to listen at `http://127.0.0.1:5277/mcp`, or use `--mcp-port 5278` to select another loopback port. The Messages toolbox reports startup results. An optional access token (generate one under Application settings → MCP, or set `DIRECT2DCAD_MCP_TOKEN`; `--mcp-token <token>` also works but is visible in the process list) makes the server require `Authorization: Bearer <token>` and answer 401 otherwise. Agent tools can neither read nor change the token.

Application Settings → MCP provides an enable switch, port, live status, failure details, and a button to copy the running address. Apply starts, stops, or changes the listener immediately and saves the preferences for the next launch. MCP defaults to disabled with port 5277. Command-line options (and a token from the environment) apply to the current launch only: saving other settings keeps the stored MCP section, and only an explicit MCP change in settings or through `set_application_settings` saves the values then in effect, including the token. A `set_application_settings` reset does not stop the running MCP listener. When a settings tool stops or changes its own listener, its response completes before the service switches. Reconnect using the new address after changing ports.

### stdio transport

Clients that only launch MCP servers as child processes (for example Claude Desktop) can run the same executable with `--mcp-stdio`. This mode opens no window: it serves MCP on stdin/stdout and forwards each tool call to the Direct2dCad instance running on this machine (`http://127.0.0.1:<port>/mcp`). The client therefore edits the drawing that is open on screen, and authentication, origin checks and UI-thread serialization stay in one place.

```json
{
  "mcpServers": {
    "direct2dcad": {
      "command": "C:\\Program Files\\Direct2dCad\\Direct2dCad.exe",
      "args": ["--mcp-stdio"],
      "env": { "DIRECT2DCAD_MCP_TOKEN": "<token from settings; omit when no token is set>" }
    }
  }
}
```

Claude Code: `claude mcp add direct2dcad -e DIRECT2DCAD_MCP_TOKEN=<token> -- "C:\Program Files\Direct2dCad\Direct2dCad.exe" --mcp-stdio`. Use `Direct2dCad.Avalonia.exe` for the Avalonia client.

- The bridge serves the tool list itself, so starting the client does not start the CAD. On the first tool call, if nothing listens on the port and no Direct2dCad window is open, it starts Direct2dCad with `--mcp-port <port>` and waits up to 60 seconds. The token travels in the environment, never on the command line. The started app breaks away from the client's process job (when the job allows it), so quitting the client does not close the drawing. Pass `--no-launch` to report an error instead.
- If Direct2dCad is open with MCP turned off, the bridge does not open a second window; it asks you to turn on MCP for that port under Application settings → MCP.
- Options: `--mcp-port <port>` (default 5277), `--mcp-token <token>` (prefer the environment variable), `--no-launch`. Logs go to stderr; stdout carries only protocol messages.
- The bridge reconnects after the app restarts. If the connection drops during a call, the tool result says the call may have run, so inspect the drawing before retrying. Requests with duplicate property names are refused (`-32700`), as over HTTP.
- The port and token of an app started by the bridge apply to that run only and are never written to the settings.

The endpoint supports `2025-11-25` initialize sessions and the `2026-07-28` per-request protocol, including cancellation notifications and HTTP disconnect cancellation. Unknown tool names and invalid catalog cursors return the standard `-32602` protocol error. Tool annotations distinguish read-only queries, additive operations and destructive changes.

MCP exposes the existing CAD tool schemas, editing operations, file operations and captured images. Start with `list_documents` and `get_agent_capabilities`. Use `document_id` to address a drawing. Calls execute serially on the UI thread with an independent undo batch for each call. Use `add_entities` to create multiple entities in one undo step.

`transfer_entities` supports cross-document copy and move with clipboard resource import, rollback on failure and independent document undo. `list_document_templates` discovers engineering templates for `create_document(template=...)`. Recovery versions use `list_recovery_copies`, `open_recovery_copy` and `clear_recovery_copies`.

For unattended saves, pass `interactive=false` and a `file_path` for new drawings. `close_document(on_unsaved=save/discard/cancel)` supplies the close decision. Replacement requires `overwrite=true`; `expected_sha256` can restrict it to a previously inspected version from `get_file_revision` or a conflict receipt. `save_compatible_copy` requires a separate path and `allow_loss=true` and switches the tab to the saved copy.

`get_application_settings` and `set_application_settings` cover all general, rendering, interaction and radial-menu preferences. `insert_image_from_clipboard`, `set_block_base_point` and the update/reorder operations of `manage_grid_presets` expose the corresponding editing features with drawing undo.

## Run from source

Requires Windows x64 and the .NET 11 SDK. The repository's `global.json` specifies SDK 11.0.100 and permits patch updates within the same feature band.

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

- [Basic editing 1](https://github.com/user-attachments/assets/53180795-5870-42c7-9148-5586ca1bfd6b)
- [Basic editing 2](https://github.com/user-attachments/assets/5515d18a-1d88-4851-a8d9-54f10bdee5ed)
- [Basic editing 3](https://github.com/user-attachments/assets/bc4d198b-6fc4-41e9-8b05-926b85c0c560)
- [Basic editing 4](https://github.com/user-attachments/assets/f5e29d71-08de-4a61-8dda-ee3dab1f81be)
- [Blocks](https://github.com/user-attachments/assets/45c5e49e-c59a-4f80-aaf3-de8ec7680310)
- [Layouts](https://github.com/user-attachments/assets/847600ec-c82e-4ed0-82d9-443d59339906)
- [OLE objects](https://github.com/user-attachments/assets/ab1f207f-48c2-40a8-b698-496c6077a0a3)
- [Terminal](https://github.com/user-attachments/assets/fc7236e2-93e8-44f3-800d-b00bfd54f761)
- [LM Studio AI 1](https://github.com/user-attachments/assets/ebb26f5b-63a1-4159-a101-69da56e776a7), [AI 2](https://github.com/user-attachments/assets/63a6763b-b63c-4a29-a499-cadb94242509)
- [MCP](https://github.com/user-attachments/assets/cacc50eb-0692-46c5-b2b3-a94aa6c3e61a)
- [Figma design](https://www.figma.com/board/wZWqWgQ9dd1p4KQVBakqmS/Direct2dCad?node-id=52-299&t=jXGAkAOnYQmodsTk-4)

## License

This project is licensed under the [MIT License](LICENSE.txt).
