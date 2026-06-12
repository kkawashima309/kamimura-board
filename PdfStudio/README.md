# PdfStudio MVP v0.1

Windows 向け PDF 編集デスクトップアプリ。
個人利用から始め、将来の商用化も見据えた設計です。

---

## 🚀 すぐにインストーラーを作りたい方へ

**`build-installer.bat` をダブルクリック** してください。

詳細は [INSTALLER-GUIDE.md](INSTALLER-GUIDE.md) を参照。

事前に [.NET 10 SDK](https://dotnet.microsoft.com/ja-jp/download/dotnet/10.0) のインストールだけ必要です（自動チェックされます）。

---

## ✨ MVP で実装済みの機能

- ✅ PDFを開く / 表示（PDFium 経由）
- ✅ 複数タブ
- ✅ サムネイル一覧
- ✅ ページ削除・左右回転・ドラッグ＆ドロップで並び替え
- ✅ 複数 PDF の結合
- ✅ PDF を1ページずつ分割
- ✅ パスワード保護（AES 128bit）
- ✅ Undo / Redo（Ctrl+Z / Ctrl+Y）
- ✅ 最近使ったファイル
- ✅ ドラッグ＆ドロップでファイルを開く
- ✅ キーボードショートカット
- ✅ 高 DPI 対応
- ✅ 構造化ログ
- ✅ 未保存時の警告

## 🎮 キーボードショートカット

| キー | 機能 |
|---|---|
| Ctrl+O | PDFを開く |
| Ctrl+S | 上書き保存 |
| Ctrl+Shift+S | 名前を付けて保存 |
| Ctrl+W | タブを閉じる |
| Ctrl+Z | 元に戻す |
| Ctrl+Y | やり直し |
| Delete | 選択中のページを削除 |

---

## 📁 ファイル構成

```
PdfStudio/
├── build-installer.bat          ← ダブルクリックで全自動ビルド
├── INSTALLER-GUIDE.md           ← まず読むファイル
├── README.md                    ← このファイル
├── QUICKSTART.md                ← 開発者向け開発手順
├── PdfStudio.sln                ← Visual Studio 用ソリューション
├── LICENSE                      ← MIT
├── THIRD-PARTY-NOTICES.md       ← 依存OSSライセンス
├── assets/
│   ├── PdfStudio.ico            ← アプリアイコン
│   └── PdfStudio.svg            ← アイコン元データ
├── installer/
│   ├── Product.wxs              ← WiX MSI 定義
│   ├── Components.wxs           ← (自動生成)
│   ├── License.rtf              ← インストール時表示用
│   └── GenerateComponents.ps1   ← Components.wxs 自動生成
├── src/
│   ├── PdfStudio.Domain/        ← エンティティ・インターフェース
│   ├── PdfStudio.Application/   ← UseCase・Undo/Redo
│   ├── PdfStudio.Infrastructure/← PDFium・PDFsharp 実装
│   └── PdfStudio.Wpf/           ← WPF UI (MVVM)
└── build/                       ← (自動生成) PdfStudioSetup.msi
```

---

## 📦 依存ライブラリ（すべて商用利用可能）

| ライブラリ | ライセンス | 用途 |
|---|---|---|
| PDFium / PDFtoImage | BSD-3 / MIT | PDF 表示 |
| PDFsharp 6 | MIT | PDF 編集・暗号化 |
| SkiaSharp | MIT | 画像処理 |
| CommunityToolkit.Mvvm | MIT | MVVM 基盤 |
| Serilog | Apache 2.0 | ロギング |

詳細は [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) 参照。

---

## ⚠️ 商用化に向けた注意

本MVPは「商用ライセンス費用ゼロ円」で構築。将来の注意点：

- **iText 7 は絶対に追加しない**（AGPL）
- **Syncfusion / QuestPDF Community** は規模制限あり
- 配布前に各ライブラリのバージョンとライセンスを再確認

---

## 🗺️ 次のフェーズで実装予定

- [ ] 注釈機能（ハイライト・テキストボックス・矢印・スタンプ）
- [ ] OCR（Tesseract.NET、日本語対応）
- [ ] 画像 ⇔ PDF 変換
- [ ] PDF → Word / Excel
- [ ] 手書き署名
- [ ] フォーム入力
- [ ] ダークモード切替
- [ ] 自動保存・クラッシュ復旧
- [ ] コードサイニング（配布時 SmartScreen 警告対策）
