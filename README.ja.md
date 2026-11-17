# Direct2dCad

[中文](README.md) | [日本語](README.ja.md) | [English](README.en.md)

Direct2dCad は、Direct2D と DirectWrite を使用した Windows 向けの 2D CAD エディターで、WPF 版と Avalonia 版を提供します。作図、数値入力、図形編集、寸法記入、図面管理を一つのデスクトップ環境にまとめ、コマンドラインと AI による操作も提供します。

## 主な機能

- **2D 作図**：直線、ポリライン、多角形、矩形、円、円弧、楕円、楕円弧、スプライン、NURBS 曲線に加え、文字、画像、OLE オブジェクト。
- **精密な入力**：座標、長さ、半径、直径、角度、楕円の半軸をキャンバス上で直接入力。オブジェクトスナップ、グリッドスナップ、直交拘束、極トラッキングに対応。
- **図形編集**：クリック選択、窓選択、交差選択、選択フィルター、グリップ編集、移動、回転、鏡像、拡大縮小。対応する曲線ではオフセット、トリム、延長、フィレット、面取り、結合、分割、配列を利用でき、閉じた輪郭ではブール和・積・差を実行できます。
- **外観と寸法**：色、線幅、破線、端部、接合部、塗りつぶし、パターン。水平・垂直・平行・半径・直径・角度寸法と引出線に対応し、フォントや矢印も変更できます。
- **図面の整理**：複数ドキュメント、レイヤー、ブロック、入れ子のブロック参照、ブロック編集、レイアウト、モデルビューポート。元に戻す・やり直す操作と図面間のコピーに対応。
- **ファイルと出力**：独自形式の `.d2cad`、一般的な 2D エンティティの DXF 入出力、自動復元、印刷プレビュー、尺度を指定した印刷。
- **コマンドラインと AI**：Terminal のヘルプ、補完、コマンド履歴。LM Studio または Codex に接続し、図面の検索、図形の作成、元に戻せる編集を実行できます。

UI は中国語、日本語、英語に対応し、ライト・ダークテーマ、ドッキング可能なツールボックス、設定可能なラジアルメニューを備えています。

## ダウンロードとインストール

[GitHub Releases](https://github.com/yoi102/Direct2dCad/releases) から Windows x64 版をダウンロードしてください。

- **WPF 版**：ファイル名に `Wpf` を含む MSI または ZIP を選びます。ポータブル版は `Direct2dCad.exe` を実行します。
- **Avalonia 版**：ファイル名に `Avalonia` を含む MSI または ZIP を選びます。ポータブル版は `Direct2dCad.Avalonia.exe` を実行します。

WPF 版はランタイムを同梱し、Avalonia 版は NativeAOT を使用するため、どちらも .NET Runtime の別途インストールは不要です。MSI ではインストール先を選択でき、デスクトップとスタートメニューにショートカットを作成します。両方の版を同時にインストールできます。

## はじめに

1. 図面を新規作成するか、`.d2cad` または DXF ファイルを開きます。
2. 作図タブでツールを選び、マウスで点を指定するか、キャンバス上の小さな入力欄に数値を入力します。
3. 図形を選択し、プロパティパネルでレイヤーや外観を変更します。修正タブで図形を編集し、寸法タブで寸法を追加できます。
4. `.d2cad` として保存するほか、DXF に書き出したり、レイアウトと印刷プレビューから図面を印刷したりできます。

基本操作：

- `Tab` / `Shift+Tab` で数値欄を切り替え、`Enter` で現在の入力を確定します。複数点の作図は最後の点を確定した後、もう一度 `Enter` を押して終了します。
- `Esc` で現在の操作をキャンセルし、選択モードに戻ります。
- ホイールでズーム、右ボタンまたは中ボタンでパンします。グリップ編集はプレビューを確認し、次の左クリックで確定します。
- Terminal に `HELP` を入力するとコマンドを、`TOOLS` / `TOOLHELP` を入力すると AI ツールを確認できます。

## AI 接続

AI ツールボックスの歯車ボタンから接続を設定します。

- **LM Studio**：Local Server を起動し、ツール呼び出しに対応したモデルを読み込みます。既定のアドレスは `http://localhost:1234/v1` です。
- **Codex**：ローカルの Codex CLI のログイン状態を使い、`codex app-server` に接続します。

AI はエンティティ、レイヤー、ブロックを検索し、図形の作成・変更や図面を開く・保存する・切り替える操作を実行できます。編集は図面の履歴に入り、元に戻したり、手動で調整したりできます。

## MCP 接続

両デスクトップクライアントは、公式の `ModelContextProtocol.AspNetCore` NuGet パッケージを使用して MCP Streamable HTTP サービスを提供します。実行ファイルに `--mcp` を付けると `http://127.0.0.1:5277/mcp` で接続できます。`--mcp-port 5278` でループバックポートを指定できます。起動結果は Messages ツールボックスに表示されます。任意のアクセス トークン（設定の MCP で「生成」、または環境変数 `DIRECT2DCAD_MCP_TOKEN`。`--mcp-token <トークン>` も使えますがプロセス一覧から見えます）を設定すると、`Authorization: Bearer <トークン>` がない要求は 401 になります。エージェントのツールからトークンを読み取ったり変更したりすることはできません。

「アプリケーション設定 → MCP」で有効化、ポート変更、状態とエラーの確認、実際の接続アドレスのコピーができます。「適用」で直ちに反映され、次回起動時にも復元されます。初期状態は無効で、既定のポートは 5277 です。コマンドライン引数（と環境変数のトークン）は今回の起動にだけ適用され、設定には書き込まれません。ほかの設定を保存しても MCP の設定は元のまま保たれ、設定画面または `set_application_settings` で MCP を明示的に変更したときだけ、その時点の値（トークンを含む）が保存されます。`set_application_settings` の `reset` は動作中の MCP サービスを止めません。MCP 経由で自身の停止やポート変更を要求した場合、応答の完了後にサービスを切り替えます。ポート変更後は新しいアドレスで再接続してください。

### stdio トランスポート

MCP サーバーを子プロセスとしてのみ起動するクライアント（Claude Desktop など）では、同じ実行ファイルを `--mcp-stdio` 付きで実行します。このモードはウィンドウを開かず、標準入出力で MCP を提供し、各ツール呼び出しをこの PC で動作中の Direct2dCad（`http://127.0.0.1:<ポート>/mcp`）へ転送します。そのため、クライアントが編集するのは画面で開いている図面であり、認証・オリジン検証・UI スレッドでの直列実行は一か所にまとまります。

```json
{
  "mcpServers": {
    "direct2dcad": {
      "command": "C:\\Program Files\\Direct2dCad\\Direct2dCad.exe",
      "args": ["--mcp-stdio"],
      "env": { "DIRECT2DCAD_MCP_TOKEN": "<設定のトークン。トークン未設定なら削除>" }
    }
  }
}
```

Claude Code：`claude mcp add direct2dcad -e DIRECT2DCAD_MCP_TOKEN=<トークン> -- "C:\Program Files\Direct2dCad\Direct2dCad.exe" --mcp-stdio`。Avalonia 版は `Direct2dCad.Avalonia.exe` を指定します。

- ツール一覧はブリッジ自身が返すため、クライアントの起動だけでは CAD は起動しません。最初のツール呼び出しでポートに応答がなく、Direct2dCad のウィンドウも開いていなければ、`--mcp-port <ポート>` で Direct2dCad を起動し最大 60 秒待ちます。トークンは環境変数で渡し、コマンドラインには現れません。起動したアプリはクライアントのプロセス ジョブから切り離される（ジョブが許可する場合）ため、クライアントを終了しても編集中の図面は閉じません。`--no-launch` を付けると自動起動せずエラーを返します。
- Direct2dCad が開いていて MCP が無効な場合は 2 つ目のウィンドウを開かず、「アプリケーション設定 → MCP」で該当ポートを有効にするよう案内します。
- オプション：`--mcp-port <ポート>`（既定 5277）、`--mcp-token <トークン>`（環境変数を推奨）、`--no-launch`。ログは標準エラーに出力され、標準出力にはプロトコル メッセージのみが流れます。
- アプリの再起動後は自動で再接続します。呼び出し中に接続が切れた場合、ツール結果は実行済みの可能性を示すので、図面を確認してから再試行してください。重複したプロパティ名を含む要求は HTTP と同様に拒否されます（`-32700`）。
- ブリッジが起動したアプリのポートとトークンはその実行中だけ有効で、設定には書き込まれません。

`2025-11-25` の初期化セッションと `2026-07-28` のリクエスト単位プロトコルに対応し、キャンセル通知と HTTP 切断を処理します。未知のツール名と無効なカーソルには標準の `-32602` エラーを返します。ツールには読み取り専用、追加操作、破壊的変更を示す注釈を付けています。

MCP は既存の CAD ツール定義、編集、ファイル操作、画面キャプチャに対応します。最初に `list_documents` と `get_agent_capabilities` を呼び出し、`document_id` で対象の図面を指定します。各呼び出しは UI スレッドで順番に実行され、それぞれ独立した元に戻す操作になります。複数の図形をまとめて作成するには `add_entities` を使用します。

`transfer_entities` は図面間のコピーと移動に対応します。テンプレートは `list_document_templates` と `create_document` の `template` 引数、復元版は `list_recovery_copies`、`open_recovery_copy`、`clear_recovery_copies` で操作できます。

ダイアログを使わずに保存する場合は `interactive=false` を指定し、新規図面には `file_path` も渡します。閉じる際は `on_unsaved=save/discard/cancel` を指定します。上書きには `overwrite=true` を使い、`expected_sha256` で対象版を限定できます。`get_file_revision` はファイルの版を返します。`save_compatible_copy` は別のパスと `allow_loss=true` を要求し、保存した互換コピーにタブを切り替えます。

`get_application_settings` と `set_application_settings` は一般、描画、操作、ラジアルメニューの全設定に対応します。クリップボード画像、ブロック基点、グリッドプリセットの値と順序も各ツールで編集できます。

## ソースから実行

Windows x64 と .NET 11 SDK が必要です。リポジトリの `global.json` は SDK 11.0.100 を指定し、同じ機能バンド内のパッチ更新を許可しています。

リポジトリのルートで実行します。

```powershell
dotnet build .\Direct2dCad.slnx -c Release
dotnet run -c Release --project .\Direct2dCad.wpf\Direct2dCad.wpf.csproj
```
基本操作
ランタイムを含む発行用フォルダーを作成するには、次のコマンドを実行します。

```powershell
dotnet publish .\Direct2dCad.wpf\Direct2dCad.wpf.csproj -c Release -r win-x64 --self-contained true
```

## デモとデザイン

- [基本操作 1](https://github.com/user-attachments/assets/53180795-5870-42c7-9148-5586ca1bfd6b)
- [基本操作 2](https://github.com/user-attachments/assets/5515d18a-1d88-4851-a8d9-54f10bdee5ed)
- [基本操作 3](https://github.com/user-attachments/assets/bc4d198b-6fc4-41e9-8b05-926b85c0c560)
- [基本操作 4](https://github.com/user-attachments/assets/f5e29d71-08de-4a61-8dda-ee3dab1f81be)
- [ブロック](https://github.com/user-attachments/assets/45c5e49e-c59a-4f80-aaf3-de8ec7680310)
- [レイアウト](https://github.com/user-attachments/assets/847600ec-c82e-4ed0-82d9-443d59339906)
- [OLE オブジェクト](https://github.com/user-attachments/assets/ab1f207f-48c2-40a8-b698-496c6077a0a3)
- [Terminal](https://github.com/user-attachments/assets/fc7236e2-93e8-44f3-800d-b00bfd54f761)
- [LM Studio AI 1](https://github.com/user-attachments/assets/ebb26f5b-63a1-4159-a101-69da56e776a7)、[AI 2](https://github.com/user-attachments/assets/63a6763b-b63c-4a29-a499-cadb94242509)
- [MCP](https://github.com/user-attachments/assets/cacc50eb-0692-46c5-b2b3-a94aa6c3e61a)
- [Figma デザイン](https://www.figma.com/board/wZWqWgQ9dd1p4KQVBakqmS/Direct2dCad?node-id=52-299&t=jXGAkAOnYQmodsTk-4)

## ライセンス

本プロジェクトは [MIT License](LICENSE.txt) を採用しています。
