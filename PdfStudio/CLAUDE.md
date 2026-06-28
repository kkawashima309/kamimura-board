# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```powershell
dotnet restore
dotnet build PdfStudio.sln
dotnet run --project src\PdfStudio.Wpf
```

Release single-file build (self-contained, win-x64 only — this app does not support other RIDs):
```powershell
dotnet publish src\PdfStudio.Wpf -c Release -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true
```
Output: `src\PdfStudio.Wpf\bin\Release\net10.0-windows\win-x64\publish\PdfStudio.exe`

Full MSI installer (WiX): double-click `build-installer.bat`, or see `INSTALLER-GUIDE.md`.

**No automated test suite exists in this solution** (no `*.Tests` project is referenced by `PdfStudio.sln`). Do not assume `dotnet test` works — verify manually by running the app per `QUICKSTART.md` section 5.

`PdfStudio.Wpf` must build/run as `x64` (`PlatformTarget` is hardcoded in its `.csproj`) because PDFium (via PDFtoImage) ships native libraries that fail to load under `AnyCPU`.

## Architecture

Four-project layered solution under `src/`, referencing strictly downward (`Wpf → Infrastructure → Application → Domain`):

- **PdfStudio.Domain** — no dependencies. `Entities/` (`PdfDocument`, `PdfPage`, `PdfMetadata`), `Interfaces/` (`IPdfEditor`, `IPdfRenderer`, `IPdfOcrService`, `IPdfSearchService`, `IPdfAnnotationService`, `IPdfSecurityService`, `ILicenseService`), `ValueObjects/` (immutable records: `PdfDocumentProperties`, `PdfPermissions`, `WatermarkOptions`, etc.).
- **PdfStudio.Application** — depends on Domain only. `UseCases/Pages/` holds undoable command classes (`DeletePageCommand`, `RotatePageCommand`, `MovePageCommand`) implementing `IUndoableCommand` (`Description`, `ExecuteAsync()`, `UndoAsync()`). `Common/UndoRedoStack.cs` is the history manager (two stacks, 100-entry cap, redo cleared on new execute). `Common/Result.cs` provides a `Result`/`Result<T>` railway type. `DependencyInjection.cs` exposes `AddApplication()`.
- **PdfStudio.Infrastructure** — implements the Domain interfaces under `Pdf/`:
  | Interface | Implementation | Library |
  |---|---|---|
  | `IPdfRenderer` | `PdfiumRenderer.cs` | PDFtoImage (PDFium) |
  | `IPdfEditor` | `PdfSharpEditor.cs` | PDFsharp 6 |
  | `IPdfSecurityService` | `PdfSharpSecurityService.cs` | PDFsharp 6 (AES-128 V4) |
  | `IPdfSearchService` | `PdfPigSearchService.cs` | PdfPig |
  | `IPdfAnnotationService` | `PdfSharpAnnotationService.cs` | PDFsharp 6 |
  | `IPdfOcrService` | `TesseractOcrService.cs` | TesseractOCR 5.5.2 |

  Also: `BatchService.cs` (batch watermark/page-number/header-footer), `FontHelper.cs` / `WindowsFontResolver.cs` (Japanese font embedding — PDFsharp can't read `.ttc`, so Noto Sans JP `.otf`/`.ttf` is bundled instead), `OcrImagePreprocessor.cs` (grayscale → contrast → 3x3 median filter → sharpen → binarize before Tesseract), and `Persistence/RecentFilesRepository.cs` (`IRecentFilesService`, JSON MRU list, max 15, under `%APPDATA%/PdfStudio/`). `DependencyInjection.cs` exposes `AddInfrastructure()`; **everything is registered as a singleton**.

  **PDFium is not thread-safe.** `PdfiumRenderer` and `TesseractOcrService` each hold a static lock object around every `PDFtoImage.Conversion.*` call. Any new code calling PDFium/PDFtoImage APIs must follow the same `lock` pattern.

- **PdfStudio.Wpf** — MVVM via CommunityToolkit.Mvvm, hosted by `Microsoft.Extensions.Hosting`. `App.xaml.cs` builds the Generic Host, calls `AddApplication()` + `AddInfrastructure()`, then registers presentation-layer singletons (`IDialogService`, `PrintService`, `MainViewModel`, `MainWindow`) and configures Serilog (rolling file at `%APPDATA%/PdfStudio/Logs/pdfstudio-.log`, 14-day retention, + console sink). Also registers the PDFsharp `WindowsFontResolver` globally for stamps/watermarks.
  - `Views/` (`MainWindow.xaml` is the tabbed shell; `Views/Dialogs/` for modals) — no `View` suffix; a view's `DataContext` is assigned to its ViewModel in code-behind/XAML rather than by naming convention.
  - `ViewModels/MainViewModel.cs` is the central orchestrator: constructor-injects every Domain interface plus `UndoRedoStack` and `IDialogService`, and exposes 40+ `[RelayCommand]` methods (open/save, page ops, merge/split, security, annotations, OCR, search, navigation, printing).
  - `ViewModels/PdfDocumentViewModel.cs` — one per open tab (`MainViewModel.OpenDocuments`), holds `Pages`, zoom state, and the currently rendered page image.
  - `ViewModels/PageViewModel.cs` — single page (thumbnail/number).
  - `Converters/Converters.cs` (Bool/Null/Count → Visibility), drag-and-drop and keyboard shortcuts wired in `MainWindow.xaml(.cs)`.

### Licensing constraint (from README.md)
This MVP targets zero commercial-license cost. **Never add iText 7 (AGPL)**. Syncfusion / QuestPDF Community have usage-scale limits — check before depending on them for new PDF features.

## OCR現状メモ(2026-06-24)

- OCR修正(`EngineMode.LstmOnly`固定 / `jpn+eng`文字列指定 / `OcrImagePreprocessor`への3x3メディアンフィルター追加)は実装・ビルド済み。
- **`tools\tessdata`を`tessdata_best`版(jpn/eng)に差し替え済み**。実機再テスト(`merged.pdf`)で品番・図番・日本語・丸囲み数字(①②③)すべて大幅改善し実用レベルに到達。**この構成で確定**。
- 退避した標準版(`tessdata`版)は`tools\tessdata\backup_standard\`に保管。精度が再度問題化するまで**当面戻さない方針**。
- `PdfStudio.Wpf.csproj`のpublishコピー設定で`tools\tessdata\backup_standard\**`は除外済み(ビルド出力肥大化防止)。
- **残課題**: `PSM=SingleBlock`が表組み帳票に不適合の可能性。別PSM(`SparseText`等)を要検討(優先度は下がった)。
- **別件**: `TesseractOcrService.cs`の`engine.Process(img, "PdfStudio OCR")`の第2引数が実ファイルパスでないため、毎ページLeptonica警告(`fopenReadStream`/`findFileFormat`)が出る。出力PDF自体は無害(画像埋め込み・検索とも正常)だが要修正。
- **次の要望(着手中)**: 検索ヒット箇所へのカーソル移動/ハイライト機能(`IPdfSearchService`/`PdfPigSearchService`周り)。
