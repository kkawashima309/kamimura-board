# インストーラー作成ガイド

このフォルダーを Windows PC で展開後、**`build-installer.bat` をダブルクリック**するだけで、
配布可能な MSI インストーラー (`PdfStudioSetup.msi`) が自動生成されます。

---

## 🎯 完成イメージ

```
build-installer.bat をダブルクリック
        ↓
（自動）.NET 10 SDK 確認
        ↓
（自動）WiX v4 ツールをインストール
        ↓
（自動）NuGet パッケージ復元
        ↓
（自動）リリースビルド (3〜5分)
        ↓
（自動）ファイル一覧から WiX 定義生成
        ↓
（自動）MSI インストーラーをビルド
        ↓
build/PdfStudioSetup.msi 完成 ✨
        ↓
build フォルダーが自動で開く
```

---

## ✅ 事前準備（一度だけ）

### .NET 10 SDK のインストール

これだけ事前に必要です。スクリプトが自動でチェックし、なければインストール先のURLを開きます。

1. https://dotnet.microsoft.com/ja-jp/download/dotnet/10.0
2. 「.NET SDK 8.0.xxx」の **x64 Installer** をダウンロード
3. ダウンロードしたインストーラーをダブルクリック → 既定のままインストール
4. インストール後、PCを再起動するか、コマンドプロンプトを開き直す

> **WiX v4 は不要です** — スクリプトが自動でグローバルツールとして入れます。

---

## 🚀 実行手順

1. zip を任意のフォルダーに展開（例: `C:\Projects\PdfStudio\`）
2. `build-installer.bat` をダブルクリック
3. 黒いコンソールウィンドウが開き、自動処理が進む
4. 「ビルド成功!」と表示されたら完了
5. 自動で開く `build` フォルダーに `PdfStudioSetup.msi` がある

> 初回は **5〜10分** ほどかかります（NuGet パッケージのダウンロード含む）。
> 2回目以降は **2〜3分** で完了します。

---

## 📦 PdfStudioSetup.msi の使い方

### インストールする側

1. `PdfStudioSetup.msi` をダブルクリック
2. 「ユーザー アカウント制御」が出たら「はい」（管理者権限が必要）
3. インストールウィザード:
   - ようこそ画面 → 次へ
   - ライセンス同意画面 → 同意して次へ
   - インストール先選択 → 既定（`C:\Program Files\PdfStudio\`）のまま次へ
   - インストール開始 → 完了
4. スタートメニューに「PdfStudio」が追加される
5. デスクトップにショートカットが追加される
6. PDFファイルを右クリック → 「PdfStudio で開く」が選べるようになる

### アンインストールする場合

「設定」→「アプリ」→「インストールされているアプリ」→ PdfStudio →「アンインストール」

または、コントロールパネル → プログラムと機能 から。

---

## 🔧 配布

`PdfStudioSetup.msi` 1ファイルだけを社内共有や配布すれば OK です。
受け取った人は他のソフトをインストールする必要なく、そのまま使えます
(.NET ランタイムも内包されています)。

サイズは **約 100〜130 MB** です（PDFium + .NET ランタイム同梱のため）。

---

## ❓ トラブルシューティング

### 「.NET 10 SDK がインストールされていません」と出る

→ ダウンロードページが自動で開きます。インストール後、再度バッチを実行してください。

### 「WiX のパスが通っていません」と出る

→ インストールはできているのに PATH に反映されていません。
   コマンドプロンプトを完全に閉じて開き直し、もう一度バッチを実行してください。

### 「ビルドに失敗しました」と出る

→ 上にスクロールしてエラー内容を確認してください。
   よくある原因:
   - ウイルス対策ソフトが `bin/obj` を監視していてロック → 一時無効化または除外設定
   - 権限不足 → エクスプローラーで `bin` `obj` フォルダーを削除してから再実行
   - .NET 10 ではなく古いバージョンがインストールされている → 8.0.x を入れる

### MSI のビルドだけ失敗する

→ `installer/Components.wxs` が正しく生成されているか確認。
   `installer/GenerateComponents.ps1` を直接実行して原因を特定:

   ```
   powershell -ExecutionPolicy Bypass -File installer\GenerateComponents.ps1 ^
       -PublishDir "src\PdfStudio.Wpf\bin\Release\net10.0-windows\win-x64\publish" ^
       -OutputPath "installer\Components.wxs"
   ```

### コードサイニング (デジタル署名) について

このスクリプトは**未署名のMSI**を生成します。配布時に Windows SmartScreen の警告
(「Windows によって PC が保護されました」) が出ますが、機能上の問題はありません。

社外配布する場合はコードサイニング証明書 (年間 $200〜600) の購入と signtool での署名を
推奨します。この方法は次のフェーズで案内予定です。

---

## 📁 生成物の場所

| ファイル/フォルダ | 説明 |
|---|---|
| `build\PdfStudioSetup.msi` | **配布用の MSI インストーラー** |
| `src\PdfStudio.Wpf\bin\Release\net10.0-windows\win-x64\publish\` | アプリ本体（実行可能ファイル群） |
| `installer\Components.wxs` | WiX 用の自動生成ファイル一覧（ビルド毎に再生成） |

---

## 🔄 再ビルド方法

ソースコードを修正した後は、もう一度 `build-installer.bat` をダブルクリックするだけです。

完全にクリーンな状態から再ビルドしたい場合は、以下を削除してから実行:

```
build\
src\PdfStudio.Wpf\bin\
src\PdfStudio.Wpf\obj\
src\PdfStudio.Domain\bin\, obj\
src\PdfStudio.Application\bin\, obj\
src\PdfStudio.Infrastructure\bin\, obj\
```

または、PowerShell で一括削除:

```powershell
Get-ChildItem -Path . -Include bin,obj,build -Recurse -Directory | Remove-Item -Recurse -Force
```
