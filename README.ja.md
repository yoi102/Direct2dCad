# Direct2dCad

[中文](README.md) | [日本語](README.ja.md) | [English](README.en.md)

Direct2dCad は、WPF、Direct2D、DirectWrite を使用した Windows 向けの 2D CAD エディターです。作図、数値入力、図形編集、寸法記入、図面管理を一つのデスクトップ環境にまとめ、コマンドラインと AI による操作も提供します。

## 主な機能

- **2D 作図**：直線、ポリライン、多角形、矩形、円、円弧、楕円、楕円弧、スプラインに加え、文字、画像、OLE オブジェクト。
- **精密な入力**：座標、長さ、半径、直径、角度、楕円の半軸をキャンバス上で直接入力。オブジェクトスナップ、グリッドスナップ、直交拘束、極トラッキングに対応。
- **図形編集**：クリック選択、窓選択、交差選択、選択フィルター、グリップ編集、移動、回転、鏡像、拡大縮小。対応する曲線ではオフセット、トリム、延長、フィレット、面取り、結合、分割、配列を利用でき、閉じた輪郭ではブール和・積・差を実行できます。
- **外観と寸法**：色、線幅、破線、端部、接合部、塗りつぶし、パターン。水平・垂直・平行・半径・直径・角度寸法と引出線に対応し、フォントや矢印も変更できます。
- **図面の整理**：複数ドキュメント、レイヤー、ブロック、入れ子のブロック参照、ブロック編集、レイアウト、モデルビューポート。元に戻す・やり直す操作と図面間のコピーに対応。
- **ファイルと出力**：独自形式の `.d2cad`、一般的な 2D エンティティの DXF 入出力、自動復元、印刷プレビュー、尺度を指定した印刷。
- **コマンドラインと AI**：Terminal のヘルプ、補完、コマンド履歴。LM Studio または Codex に接続し、図面の検索、図形の作成、元に戻せる編集を実行できます。

UI は中国語、日本語、英語に対応し、ライト・ダークテーマ、ドッキング可能なツールボックス、設定可能なラジアルメニューを備えています。

## ダウンロードとインストール

[GitHub Releases](https://github.com/yoi102/Direct2dCad/releases) から Windows x64 版をダウンロードしてください。

- **MSI インストーラー**：アプリをインストールし、デスクトップとスタートメニューにショートカットを作成します。
- **ポータブル ZIP**：展開して `Direct2dCad.exe` を実行します。

どちらも必要な .NET Runtime を含むため、ランタイムを別途インストールする必要はありません。

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

## ソースから実行

Windows x64 と .NET 10 SDK が必要です。リポジトリの `global.json` は SDK 10.0.401 を指定し、同じ機能バンド内のパッチ更新を許可しています。

リポジトリのルートで実行します。

```powershell
dotnet build .\Direct2dCad.slnx -c Release
dotnet run -c Release --project .\Direct2dCad.wpf\Direct2dCad.wpf.csproj
```

ランタイムを含む発行用フォルダーを作成するには、次のコマンドを実行します。

```powershell
dotnet publish .\Direct2dCad.wpf\Direct2dCad.wpf.csproj -c Release -r win-x64 --self-contained true
```

## デモとデザイン

- [基本操作 1](https://github.com/user-attachments/assets/53180795-5870-42c7-9148-5586ca1bfd6b)、[基本操作 2](https://github.com/user-attachments/assets/5515d18a-1d88-4851-a8d9-54f10bdee5ed)
- [ブロック](https://github.com/user-attachments/assets/45c5e49e-c59a-4f80-aaf3-de8ec7680310)
- [レイアウト](https://github.com/user-attachments/assets/847600ec-c82e-4ed0-82d9-443d59339906)
- [OLE オブジェクト](https://github.com/user-attachments/assets/ab1f207f-48c2-40a8-b698-496c6077a0a3)
- [Terminal](https://github.com/user-attachments/assets/fc7236e2-93e8-44f3-800d-b00bfd54f761)
- [LM Studio AI 1](https://github.com/user-attachments/assets/ebb26f5b-63a1-4159-a101-69da56e776a7)、[AI 2](https://github.com/user-attachments/assets/63a6763b-b63c-4a29-a499-cadb94242509)
- [Figma デザイン](https://www.figma.com/board/wZWqWgQ9dd1p4KQVBakqmS/Direct2dCad?node-id=52-299&t=jXGAkAOnYQmodsTk-4)

## ライセンス

本プロジェクトは [MIT License](LICENSE.txt) を採用しています。
