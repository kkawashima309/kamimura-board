# クイックスタート

## 1. 必要なツールのインストール

### .NET 10 SDK
https://dotnet.microsoft.com/ja-jp/download/dotnet/10.0 から
「Windows x64 SDK インストーラー」をダウンロードしてインストール。

PowerShell で確認:
```
dotnet --version
```
`8.0.xxx` と表示されればOK。

### Visual Studio 2022（推奨）
- Community 版（無料）でOK: https://visualstudio.microsoft.com/ja/vs/community/
- インストール時に「.NET デスクトップ開発」ワークロードを選択。

または **JetBrains Rider** や **VS Code + C# Dev Kit** でも可。

---

## 2. プロジェクトを開く

zipを展開後、Visual Studio 2022 で `PdfStudio.sln` を開きます。

初回はソリューション右クリック → 「NuGet パッケージの復元」、
あるいはターミナルで:

```powershell
cd PdfStudio
dotnet restore
```

---

## 3. ビルド & 実行

### Visual Studio から
1. ソリューション エクスプローラーで `PdfStudio.Wpf` を右クリック →
   「スタートアップ プロジェクトに設定」
2. **F5** でデバッグ実行、または **Ctrl+F5** で実行のみ。

### コマンドラインから

```powershell
dotnet run --project src\PdfStudio.Wpf
```

---

## 4. リリース版の作成（自己完結型 single-file exe）

```powershell
dotnet publish src\PdfStudio.Wpf -c Release -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true
```

出力先:
```
src\PdfStudio.Wpf\bin\Release\net10.0-windows\win-x64\publish\PdfStudio.exe
```

このexeは .NET ランタイム不要で配布可能です（約 80〜100 MB）。

---

## 5. 動作確認

1. アプリを起動するとスタート画面が表示される
2. 「📂 PDFを開く」または PDF を画面にドラッグ＆ドロップ
3. 左サイドバーにサムネイル一覧が表示される
4. サムネイルをクリックすると中央にページが表示される
5. ツールバーの「左回転」「右回転」「削除」を試す → Ctrl+Z で取り消し可能
6. メニュー「ツール」→「PDFを結合」で複数PDFを1つにまとめる
7. メニュー「ツール」→「パスワードを設定」でPDFを暗号化保存

---

## 6. トラブルシューティング

### 「PDFium が見つかりません」のようなエラー

PDFtoImage パッケージは PDFium をネイティブDLLとして含んでいますが、
プラットフォームターゲットが `Any CPU` のままだとロードに失敗することがあります。

解決方法: `src\PdfStudio.Wpf\PdfStudio.Wpf.csproj` に以下を追加:

```xml
<PropertyGroup>
  <PlatformTarget>x64</PlatformTarget>
</PropertyGroup>
```

### ビルドエラー: WPF 関連が解決できない

`Directory.Build.props` の TargetFramework が `net10.0-windows` になっていることを確認。
`UseWPF` が `true` になっているか csproj をチェック。

### 文字化け

すべてのソースファイルは UTF-8 (BOM 付き) で保存することを推奨。
日本語コメントが化ける場合、Visual Studio の
「ファイル」→「保存オプション」で「UTF-8 (シグネチャ付き)」を選択。

---

## 7. 次のステップ

- README.md の「次のフェーズで実装予定」を参照して機能拡張
- 注釈機能や OCR の実装に進む
- 商用化を検討する場合は LICENSE と THIRD-PARTY-NOTICES.md を必ず確認

不明点があれば README.md と各ソースのコメントを参照してください。
