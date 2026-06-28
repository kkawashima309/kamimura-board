// PdfSharp の PdfDocument と Domain の PdfDocument の衝突を回避するためのエイリアス。
// 本ファイル内では PdfDocument = Domain側、PdfSharpDocument = PdfSharp側 とする。
using PdfSharpDocument = PdfSharp.Pdf.PdfDocument;

using System.IO;
using Microsoft.Extensions.Logging;
using PdfSharp.Pdf.IO;
using PdfStudio.Domain.Entities;
using PdfStudio.Domain.Interfaces;
using PdfStudio.Domain.ValueObjects;

namespace PdfStudio.Infrastructure.Pdf;

/// <summary>
/// PDFsharp 6.x を使用したPDF編集実装。
/// MVP: 保存・結合・分割・回転・ページ削除・並び替え。
/// </summary>
public sealed class PdfSharpEditor : IPdfEditor
{
    private readonly ILogger<PdfSharpEditor> _logger;

    public PdfSharpEditor(ILogger<PdfSharpEditor> logger)
    {
        _logger = logger;
    }

    public Task SaveAsync(
        PdfDocument document,
        string? newPath = null,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var sourcePath = document.FilePath;
            var outputPath = newPath ?? document.FilePath;

            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("元のPDFが見つかりません。", sourcePath);

            // ログ: Domain側の回転状態を保存前に記録
            var rotationSummary = string.Join(", ",
                document.Pages.Select((p, i) => $"[{i}:idx={p.Index},rot={p.Rotation}°]"));
            _logger.LogInformation(
                "保存開始: 出力={Out}, Domainページ数={Count}, 状態={State}",
                outputPath, document.Pages.Count, rotationSummary);

            using var src = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
            using var dst = new PdfSharpDocument();

            // Domain側のページ順・回転をPDFに反映
            int skippedCount = 0;
            int addedCount = 0;
            foreach (var pageInfo in document.Pages)
            {
                if (pageInfo.Index < 0 || pageInfo.Index >= src.PageCount)
                {
                    _logger.LogWarning(
                        "保存中: ページIndex {Index} が範囲外(元PDFは{Count}ページ)、スキップ",
                        pageInfo.Index, src.PageCount);
                    skippedCount++;
                    continue;
                }

                // 元PDFのページを追加
                var addedPage = dst.AddPage(src.Pages[pageInfo.Index]);

                // 回転の反映 — 重要: dst.Pages[末尾] にも改めて設定して確実性を上げる
                var originalRotate = src.Pages[pageInfo.Index].Rotate;
                var finalRotate = ((originalRotate + pageInfo.Rotation) % 360 + 360) % 360;

                addedPage.Rotate = finalRotate;
                // 念のため dst 側からも設定 (戻り値の addedPage が古い参照の場合の保険)
                if (dst.PageCount > 0)
                {
                    dst.Pages[dst.PageCount - 1].Rotate = finalRotate;
                }
                addedCount++;
            }

            if (skippedCount > 0)
            {
                _logger.LogWarning(
                    "保存: {Skipped}ページがスキップされました。元PDF={SrcCount}、Domain={DomCount}",
                    skippedCount, src.PageCount, document.Pages.Count);
            }

            if (dst.PageCount == 0)
            {
                throw new InvalidOperationException(
                    "保存処理中にページが全て失われました。Domain側のページ情報を確認してください。");
            }

            // メタデータのコピー
            dst.Info.Title = document.Metadata.Title ?? src.Info.Title;
            dst.Info.Author = document.Metadata.Author ?? src.Info.Author;
            dst.Info.Subject = document.Metadata.Subject ?? src.Info.Subject;

            // 書き込み（同一パスの場合は一時ファイル経由で安全に上書き）
            bool sameAsSource = string.Equals(
                Path.GetFullPath(outputPath),
                Path.GetFullPath(sourcePath),
                StringComparison.OrdinalIgnoreCase);

            if (sameAsSource)
            {
                var tempPath = outputPath + ".tmp";
                dst.Save(tempPath);
                src.Close();
                File.Delete(outputPath);
                File.Move(tempPath, outputPath);
            }
            else
            {
                // 別パス保存時: srcを先に閉じてから保存（同期問題を回避）
                dst.Save(outputPath);
            }

            // 保存後の Rotate 値を検証(別ファイルとして開いて確認)
            try
            {
                using var verifyDoc = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
                var verifyRotations = string.Join(", ",
                    Enumerable.Range(0, verifyDoc.PageCount)
                        .Select(i => $"[{i}:{verifyDoc.Pages[i].Rotate}°]"));
                _logger.LogInformation(
                    "保存後の検証: ファイル={Path}, 合計{Count}ページ, 回転={Rot}",
                    outputPath, verifyDoc.PageCount, verifyRotations);
            }
            catch (Exception verifyEx)
            {
                _logger.LogWarning(verifyEx, "保存後の検証読み込みに失敗(致命的ではない)");
            }

            document.FilePath = outputPath;
            document.IsModified = false;

            _logger.LogInformation(
                "PDFを保存しました: {Path} ({Pages}ページ追加、{Skipped}スキップ)",
                outputPath, addedCount, skippedCount);
        }, ct);
    }

    public Task<string> MergeAsync(
        IEnumerable<string> filePaths,
        string outputPath,
        CancellationToken ct = default)
    {
        return Task.Run<string>(() =>
        {
            using var dst = new PdfSharpDocument();

            foreach (var path in filePaths)
            {
                ct.ThrowIfCancellationRequested();
                using var src = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                for (int i = 0; i < src.PageCount; i++)
                {
                    dst.AddPage(src.Pages[i]);
                }
            }

            dst.Save(outputPath);
            _logger.LogInformation(
                "PDFを結合しました: {Output} ({Count}ファイル)",
                outputPath, filePaths.Count());

            return outputPath;
        }, ct);
    }

    public Task<IReadOnlyList<string>> SplitAsync(
        string filePath,
        SplitMode mode,
        string outputDirectory,
        IEnumerable<PageRange>? ranges = null,
        CancellationToken ct = default)
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            Directory.CreateDirectory(outputDirectory);
            var baseName = Path.GetFileNameWithoutExtension(filePath);
            var results = new List<string>();

            using var src = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);

            switch (mode)
            {
                case SplitMode.EachPage:
                    for (int i = 0; i < src.PageCount; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        using var page = new PdfSharpDocument();
                        page.AddPage(src.Pages[i]);
                        var outPath = Path.Combine(
                            outputDirectory,
                            $"{baseName}_page{i + 1:D3}.pdf");
                        page.Save(outPath);
                        results.Add(outPath);
                    }
                    break;

                case SplitMode.OddEven:
                    using (var odd = new PdfSharpDocument())
                    using (var even = new PdfSharpDocument())
                    {
                        for (int i = 0; i < src.PageCount; i++)
                        {
                            ct.ThrowIfCancellationRequested();
                            // ページ番号は1始まり
                            if ((i + 1) % 2 == 1) odd.AddPage(src.Pages[i]);
                            else even.AddPage(src.Pages[i]);
                        }

                        if (odd.PageCount > 0)
                        {
                            var p = Path.Combine(outputDirectory, $"{baseName}_odd.pdf");
                            odd.Save(p);
                            results.Add(p);
                        }
                        if (even.PageCount > 0)
                        {
                            var p = Path.Combine(outputDirectory, $"{baseName}_even.pdf");
                            even.Save(p);
                            results.Add(p);
                        }
                    }
                    break;

                case SplitMode.ByRange:
                    if (ranges is null)
                        throw new ArgumentException(
                            "ByRangeモードではrangesの指定が必要です。",
                            nameof(ranges));

                    int idx = 1;
                    foreach (var range in ranges)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!range.IsValid) continue;

                        using var part = new PdfSharpDocument();
                        for (int i = range.StartPage; i <= range.EndPage && i < src.PageCount; i++)
                        {
                            part.AddPage(src.Pages[i]);
                        }
                        if (part.PageCount > 0)
                        {
                            var p = Path.Combine(
                                outputDirectory,
                                $"{baseName}_part{idx:D2}.pdf");
                            part.Save(p);
                            results.Add(p);
                            idx++;
                        }
                    }
                    break;
            }

            _logger.LogInformation(
                "PDFを分割しました: {Mode} → {Count}ファイル",
                mode, results.Count);

            return results;
        }, ct);
    }

    public Task RotatePageAsync(
        PdfDocument document,
        int pageIndex,
        int degrees,
        CancellationToken ct = default)
    {
        // Domain層への反映のみ（実ファイルへの反映は SaveAsync 時）
        if (pageIndex >= 0 && pageIndex < document.Pages.Count)
        {
            var page = document.Pages[pageIndex];
            page.Rotation = NormalizeRotation(page.Rotation + degrees);
            document.IsModified = true;
        }
        return Task.CompletedTask;
    }

    public Task DeletePageAsync(
        PdfDocument document,
        int pageIndex,
        CancellationToken ct = default)
    {
        if (pageIndex >= 0 && pageIndex < document.Pages.Count)
        {
            document.Pages.RemoveAt(pageIndex);
            for (int i = 0; i < document.Pages.Count; i++)
                document.Pages[i].Index = i;
            document.IsModified = true;
        }
        return Task.CompletedTask;
    }

    public Task ReorderPagesAsync(
        PdfDocument document,
        IList<int> newOrder,
        CancellationToken ct = default)
    {
        if (newOrder.Count != document.Pages.Count)
            throw new ArgumentException("並び順の数とページ数が一致しません。");

        var snapshot = document.Pages.ToList();
        document.Pages.Clear();
        for (int i = 0; i < newOrder.Count; i++)
        {
            var page = snapshot[newOrder[i]];
            page.Index = i;
            document.Pages.Add(page);
        }
        document.IsModified = true;
        return Task.CompletedTask;
    }

    private static int NormalizeRotation(int degrees)
    {
        var r = degrees % 360;
        return r < 0 ? r + 360 : r;
    }

    // ========== v0.2 新機能 ==========

    public Task InsertPagesAsync(
        PdfDocument document,
        int insertAtIndex,
        string sourceFilePath,
        IEnumerable<int>? sourcePageIndices = null,
        CancellationToken ct = default)
    {
        if (insertAtIndex < 0 || insertAtIndex > document.Pages.Count)
            throw new ArgumentOutOfRangeException(nameof(insertAtIndex));
        if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath))
            throw new FileNotFoundException("挿入元PDFが見つかりません。", sourceFilePath);

        // Domain層のページ情報を更新
        // (実ファイルへの反映は SaveAsync 時に行われる)
        using var srcDoc = PdfReader.Open(sourceFilePath, PdfDocumentOpenMode.Import);

        // 挿入するページ番号一覧を確定
        var indices = sourcePageIndices?.ToList() ?? Enumerable.Range(0, srcDoc.PageCount).ToList();

        // 各ページのメタ情報からDomain側のPdfPageを作成して挿入
        for (int i = 0; i < indices.Count; i++)
        {
            var srcPage = srcDoc.Pages[indices[i]];
            var newPage = new PdfPage
            {
                Index = insertAtIndex + i,
                Width = srcPage.Width.Point,
                Height = srcPage.Height.Point,
                Rotation = srcPage.Rotate,
            };
            document.Pages.Insert(insertAtIndex + i, newPage);
        }

        // 後続ページのインデックスを修正
        for (int i = 0; i < document.Pages.Count; i++)
            document.Pages[i].Index = i;

        // 挿入元情報をドキュメントに記録(SaveAsync時に参照される想定で、
        // 現状の単純実装ではIsModified=trueにしてフラグだけ立てる)
        document.IsModified = true;

        // 元データへの実反映はSaveAsync時に物理保存される。
        // ただし現在のSaveAsync実装は「並び替えと回転」のみ反映するため、
        // 挿入は物理保存ロジックを別途持つ必要がある。
        // → このため、即時に物理ファイルにも反映する補助実装を行う。
        return PhysicallyInsertPagesAsync(document, insertAtIndex, sourceFilePath, indices, ct);
    }

    /// <summary>
    /// 物理ファイルにページ挿入を反映する内部実装。
    /// 元PDFを開き、別PDFから指定ページをImportし、上書き保存する。
    /// </summary>
    private Task PhysicallyInsertPagesAsync(
        PdfDocument document,
        int insertAtIndex,
        string sourceFilePath,
        IList<int> indices,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(document.FilePath))
            {
                _logger.LogInformation("ドキュメントが未保存のため、物理挿入は保存時に行います。");
                return;
            }

            // Modifyモードではなく Importモード で開く(書き込みロックを避けるため)
            // 新規ドキュメントを組み立てて、一時ファイルに保存後、置き換える
            string tempPath = Path.Combine(
                Path.GetDirectoryName(document.FilePath) ?? Path.GetTempPath(),
                Path.GetFileNameWithoutExtension(document.FilePath) + "_tmp" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".pdf");

            try
            {
                using (var destDoc = PdfReader.Open(document.FilePath, PdfDocumentOpenMode.Import))
                using (var srcDoc = PdfReader.Open(sourceFilePath, PdfDocumentOpenMode.Import))
                using (var newDoc = new PdfSharpDocument())
                {
                    // 元のメタデータを保持
                    newDoc.Info.Title = destDoc.Info.Title;
                    newDoc.Info.Author = destDoc.Info.Author;
                    newDoc.Info.Subject = destDoc.Info.Subject;

                    // 挿入位置までのページ
                    for (int i = 0; i < insertAtIndex && i < destDoc.PageCount; i++)
                        newDoc.AddPage(destDoc.Pages[i]);
                    // 挿入するページ
                    foreach (var idx in indices)
                        newDoc.AddPage(srcDoc.Pages[idx]);
                    // 残りのページ
                    for (int i = insertAtIndex; i < destDoc.PageCount; i++)
                        newDoc.AddPage(destDoc.Pages[i]);

                    newDoc.Save(tempPath);
                }
                // ここでusing終了 → 元ファイルへのアクセスが解放される

                // 元ファイルを置き換え
                File.Delete(document.FilePath);
                File.Move(tempPath, document.FilePath);

                _logger.LogInformation(
                    "ページを挿入しました: 位置={Pos}, 件数={Count}, ファイル={File}",
                    insertAtIndex, indices.Count, document.FilePath);
            }
            catch
            {
                // 失敗時は一時ファイルを削除
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
                throw;
            }
        }, ct);
    }

    public Task ExtractPagesAsync(
        string sourceFilePath,
        IEnumerable<int> pageIndices,
        string outputPath,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath))
                throw new FileNotFoundException("元PDFが見つかりません。", sourceFilePath);

            var indexList = pageIndices?.ToList() ?? throw new ArgumentNullException(nameof(pageIndices));
            if (indexList.Count == 0)
                throw new ArgumentException("抽出するページが指定されていません。", nameof(pageIndices));

            using var srcDoc = PdfReader.Open(sourceFilePath, PdfDocumentOpenMode.Import);
            using var newDoc = new PdfSharpDocument();

            // メタデータをコピー
            newDoc.Info.Title = srcDoc.Info.Title;
            newDoc.Info.Author = srcDoc.Info.Author;
            newDoc.Info.Subject = srcDoc.Info.Subject;

            foreach (var idx in indexList)
            {
                if (idx < 0 || idx >= srcDoc.PageCount)
                    throw new ArgumentOutOfRangeException(
                        nameof(pageIndices),
                        $"ページ番号 {idx} は範囲外です(0〜{srcDoc.PageCount - 1})。");
                newDoc.AddPage(srcDoc.Pages[idx]);
            }

            // 出力ディレクトリを保証
            var outDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);

            newDoc.Save(outputPath);
            _logger.LogInformation(
                "ページを抽出しました: 元={Src}, 件数={Count}, 出力={Out}",
                sourceFilePath, indexList.Count, outputPath);
        }, ct);
    }

    public Task InsertBlankPageAsync(
        PdfDocument document,
        int insertAtIndex,
        BlankPageSize size,
        BlankPageOrientation orientation,
        CancellationToken ct = default)
    {
        if (insertAtIndex < 0 || insertAtIndex > document.Pages.Count)
            throw new ArgumentOutOfRangeException(nameof(insertAtIndex));

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            // ページサイズをポイント単位で取得(1 inch = 72 point, 1 mm = 2.8346 point)
            var (widthPt, heightPt) = GetPageSizeInPoints(size, orientation, document);

            // Domain層に反映
            var newPage = new PdfPage
            {
                Index = insertAtIndex,
                Width = widthPt,
                Height = heightPt,
                Rotation = 0,
            };
            document.Pages.Insert(insertAtIndex, newPage);
            for (int i = 0; i < document.Pages.Count; i++)
                document.Pages[i].Index = i;
            document.IsModified = true;

            // 物理ファイルへの反映(ファイルが保存済みの場合)
            if (!string.IsNullOrEmpty(document.FilePath) && File.Exists(document.FilePath))
            {
                string tempPath = Path.Combine(
                    Path.GetDirectoryName(document.FilePath) ?? Path.GetTempPath(),
                    Path.GetFileNameWithoutExtension(document.FilePath) + "_tmp" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".pdf");

                try
                {
                    using (var destDoc = PdfReader.Open(document.FilePath, PdfDocumentOpenMode.Import))
                    using (var newDoc = new PdfSharpDocument())
                    {
                        // メタデータをコピー
                        newDoc.Info.Title = destDoc.Info.Title;
                        newDoc.Info.Author = destDoc.Info.Author;
                        newDoc.Info.Subject = destDoc.Info.Subject;

                        // 挿入位置までのページ
                        for (int i = 0; i < insertAtIndex && i < destDoc.PageCount; i++)
                            newDoc.AddPage(destDoc.Pages[i]);

                        // 白紙ページを追加
                        var blankPage = newDoc.AddPage();
                        blankPage.Width = PdfSharp.Drawing.XUnit.FromPoint(widthPt);
                        blankPage.Height = PdfSharp.Drawing.XUnit.FromPoint(heightPt);

                        // 残りのページ
                        for (int i = insertAtIndex; i < destDoc.PageCount; i++)
                            newDoc.AddPage(destDoc.Pages[i]);

                        newDoc.Save(tempPath);
                    }
                    // using終了 → 元ファイルアクセス解放

                    File.Delete(document.FilePath);
                    File.Move(tempPath, document.FilePath);

                    _logger.LogInformation(
                        "白紙ページを挿入しました: 位置={Pos}, サイズ={Size} {Orient}",
                        insertAtIndex, size, orientation);
                }
                catch
                {
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                    throw;
                }
            }
        }, ct);
    }

    private static (double width, double height) GetPageSizeInPoints(
        BlankPageSize size,
        BlankPageOrientation orientation,
        PdfDocument document)
    {
        // 各用紙サイズをポイント単位で定義(縦向き基準)
        // 1mm = 72/25.4 ≒ 2.8346 point
        const double mmToPt = 72.0 / 25.4;
        double w, h;
        switch (size)
        {
            case BlankPageSize.A3:
                w = 297.0 * mmToPt;
                h = 420.0 * mmToPt;
                break;
            case BlankPageSize.A4:
                w = 210.0 * mmToPt;
                h = 297.0 * mmToPt;
                break;
            case BlankPageSize.A5:
                w = 148.0 * mmToPt;
                h = 210.0 * mmToPt;
                break;
            case BlankPageSize.B5:
                w = 176.0 * mmToPt;
                h = 250.0 * mmToPt;
                break;
            case BlankPageSize.Letter:
                w = 8.5 * 72.0;
                h = 11.0 * 72.0;
                break;
            case BlankPageSize.Legal:
                w = 8.5 * 72.0;
                h = 14.0 * 72.0;
                break;
            case BlankPageSize.MatchFirstPage:
                if (document.Pages.Count > 0)
                {
                    w = document.Pages[0].Width;
                    h = document.Pages[0].Height;
                }
                else
                {
                    // フォールバック: A4縦
                    w = 210.0 * mmToPt;
                    h = 297.0 * mmToPt;
                }
                break;
            default:
                w = 210.0 * mmToPt;
                h = 297.0 * mmToPt;
                break;
        }

        // 横向きならW/Hを入れ替え
        if (orientation == BlankPageOrientation.Landscape && size != BlankPageSize.MatchFirstPage)
            (w, h) = (h, w);

        return (w, h);
    }

    public Task UpdatePropertiesAsync(
        PdfDocument document,
        PdfDocumentProperties properties,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(document.FilePath) || !File.Exists(document.FilePath))
            {
                _logger.LogWarning("ドキュメントが未保存のため、プロパティ更新を保留します。");
                document.IsModified = true;
                return;
            }

            using var pdfDoc = PdfReader.Open(document.FilePath, PdfDocumentOpenMode.Modify);
            pdfDoc.Info.Title = properties.Title ?? string.Empty;
            pdfDoc.Info.Author = properties.Author ?? string.Empty;
            pdfDoc.Info.Subject = properties.Subject ?? string.Empty;
            pdfDoc.Info.Keywords = properties.Keywords ?? string.Empty;
            if (!string.IsNullOrEmpty(properties.Creator))
                pdfDoc.Info.Creator = properties.Creator;

            pdfDoc.Save(document.FilePath);

            // Domain側のMetadataにも反映
            document.Metadata.Title = properties.Title ?? string.Empty;
            document.Metadata.Author = properties.Author ?? string.Empty;
            document.Metadata.Subject = properties.Subject ?? string.Empty;

            _logger.LogInformation("PDFプロパティを更新しました: {File}", document.FilePath);
        }, ct);
    }

    public Task<PdfDocumentProperties> GetPropertiesAsync(
        string filePath,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("PDFが見つかりません。", filePath);

            using var pdfDoc = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
            return new PdfDocumentProperties
            {
                Title = pdfDoc.Info.Title ?? string.Empty,
                Author = pdfDoc.Info.Author ?? string.Empty,
                Subject = pdfDoc.Info.Subject ?? string.Empty,
                Keywords = pdfDoc.Info.Keywords ?? string.Empty,
                Creator = pdfDoc.Info.Creator ?? string.Empty,
                Producer = pdfDoc.Info.Producer ?? string.Empty,
                CreationDate = pdfDoc.Info.CreationDate == DateTime.MinValue
                    ? null
                    : pdfDoc.Info.CreationDate,
                ModificationDate = pdfDoc.Info.ModificationDate == DateTime.MinValue
                    ? null
                    : pdfDoc.Info.ModificationDate,
            };
        }, ct);
    }

    // ========== v0.4 新機能 ==========

    /// <summary>
    /// 全ページにウォーターマークを追加する。
    /// </summary>
    public Task AddWatermarkAsync(
        string sourceFilePath,
        string outputPath,
        WatermarkOptions options,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException("元PDFが見つかりません。", sourceFilePath);

            using var pdfDoc = PdfDocumentOpener.OpenForEdit(sourceFilePath, out _);
            var color = ParseColor(options.ColorHex);
            var brush = new PdfSharp.Drawing.XSolidBrush(
                PdfSharp.Drawing.XColor.FromArgb(
                    (int)(options.Opacity * 255), color.R, color.G, color.B));
            var font = FontHelper.Create(options.FontSize, bold: true);

            foreach (var page in pdfDoc.Pages)
            {
                ct.ThrowIfCancellationRequested();
                using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(
                    page, PdfSharp.Drawing.XGraphicsPdfPageOptions.Append);

                // ページ中央を中心に回転して描画
                var w = page.Width.Point;
                var h = page.Height.Point;
                gfx.TranslateTransform(w / 2, h / 2);
                gfx.RotateTransform(options.RotationDegrees);
                var size = gfx.MeasureString(options.Text, font);
                gfx.DrawString(options.Text, font, brush,
                    -size.Width / 2, size.Height / 4);
            }

            pdfDoc.Save(outputPath);
            _logger.LogInformation(
                "ウォーターマーク追加: {Text} → {Out}", options.Text, outputPath);
        }, ct);
    }

    /// <summary>
    /// 全ページにページ番号を追加する。
    /// </summary>
    public Task AddPageNumbersAsync(
        string sourceFilePath,
        string outputPath,
        PageNumberOptions options,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException("元PDFが見つかりません。", sourceFilePath);

            using var pdfDoc = PdfDocumentOpener.OpenForEdit(sourceFilePath, out _);
            var brush = PdfSharp.Drawing.XBrushes.Black;
            var font = FontHelper.Create(options.FontSize);

            int totalPages = pdfDoc.PageCount;
            for (int i = 0; i < pdfDoc.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (i < options.FirstPageIndex) continue;

                var page = pdfDoc.Pages[i];
                using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(
                    page, PdfSharp.Drawing.XGraphicsPdfPageOptions.Append);

                int displayPageNum = options.StartingNumber + (i - options.FirstPageIndex);
                string text = options.Format
                    .Replace("{page}", displayPageNum.ToString())
                    .Replace("{total}", totalPages.ToString());

                var size = gfx.MeasureString(text, font);
                var w = page.Width.Point;
                var h = page.Height.Point;
                var (x, y) = GetPositionCoords(
                    options.Position, w, h, size.Width, size.Height, options.Margin);
                gfx.DrawString(text, font, brush, x, y);
            }

            pdfDoc.Save(outputPath);
            _logger.LogInformation(
                "ページ番号追加: {Out} ({Count}ページ)", outputPath, totalPages);
        }, ct);
    }

    /// <summary>
    /// ヘッダー/フッターを全ページに追加する。
    /// </summary>
    public Task AddHeaderFooterAsync(
        string sourceFilePath,
        string outputPath,
        HeaderFooterOptions options,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException("元PDFが見つかりません。", sourceFilePath);

            using var pdfDoc = PdfDocumentOpener.OpenForEdit(sourceFilePath, out _);
            var brush = PdfSharp.Drawing.XBrushes.Black;
            var font = FontHelper.Create(options.FontSize);

            var fileName = Path.GetFileNameWithoutExtension(sourceFilePath);
            var date = DateTime.Now.ToString("yyyy/MM/dd");

            string Substitute(string s) =>
                s.Replace("{date}", date)
                 .Replace("{filename}", fileName);

            for (int i = 0; i < pdfDoc.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                var page = pdfDoc.Pages[i];
                using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(
                    page, PdfSharp.Drawing.XGraphicsPdfPageOptions.Append);

                var w = page.Width.Point;
                var h = page.Height.Point;

                // ヘッダー
                DrawHeaderFooterLine(gfx, font, brush,
                    Substitute(options.HeaderLeft),
                    Substitute(options.HeaderCenter),
                    Substitute(options.HeaderRight),
                    w, options.Margin, isHeader: true);

                // フッター
                DrawHeaderFooterLine(gfx, font, brush,
                    Substitute(options.FooterLeft),
                    Substitute(options.FooterCenter),
                    Substitute(options.FooterRight),
                    w, h - options.Margin - options.FontSize, isHeader: false);
            }

            pdfDoc.Save(outputPath);
            _logger.LogInformation("ヘッダー/フッター追加: {Out}", outputPath);
        }, ct);
    }

    private static void DrawHeaderFooterLine(
        PdfSharp.Drawing.XGraphics gfx,
        PdfSharp.Drawing.XFont font,
        PdfSharp.Drawing.XBrush brush,
        string left, string center, string right,
        double pageWidth, double yPosition, bool isHeader)
    {
        double margin = 24;
        if (!string.IsNullOrEmpty(left))
            gfx.DrawString(left, font, brush, margin, yPosition);
        if (!string.IsNullOrEmpty(center))
        {
            var size = gfx.MeasureString(center, font);
            gfx.DrawString(center, font, brush, (pageWidth - size.Width) / 2, yPosition);
        }
        if (!string.IsNullOrEmpty(right))
        {
            var size = gfx.MeasureString(right, font);
            gfx.DrawString(right, font, brush, pageWidth - margin - size.Width, yPosition);
        }
    }

    private static (double x, double y) GetPositionCoords(
        PageNumberPosition pos, double pageW, double pageH,
        double textW, double textH, double margin)
    {
        return pos switch
        {
            PageNumberPosition.TopLeft => (margin, margin + textH),
            PageNumberPosition.TopCenter => ((pageW - textW) / 2, margin + textH),
            PageNumberPosition.TopRight => (pageW - margin - textW, margin + textH),
            PageNumberPosition.BottomLeft => (margin, pageH - margin),
            PageNumberPosition.BottomCenter => ((pageW - textW) / 2, pageH - margin),
            PageNumberPosition.BottomRight => (pageW - margin - textW, pageH - margin),
            _ => ((pageW - textW) / 2, pageH - margin),
        };
    }

    private static (byte R, byte G, byte B) ParseColor(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return (0x88, 0x88, 0x88);
        var h = hex.TrimStart('#');
        if (h.Length != 6) return (0x88, 0x88, 0x88);
        try
        {
            byte r = Convert.ToByte(h.Substring(0, 2), 16);
            byte g = Convert.ToByte(h.Substring(2, 2), 16);
            byte b = Convert.ToByte(h.Substring(4, 2), 16);
            return (r, g, b);
        }
        catch
        {
            return (0x88, 0x88, 0x88);
        }
    }

    /// <summary>
    /// PDFページを画像ファイルとしてエクスポートする。
    /// </summary>
    public Task ExportPageAsImageAsync(
        string sourceFilePath,
        int pageIndex,
        string outputPath,
        ImageExportFormat format,
        int dpi = 150,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException("元PDFが見つかりません。", sourceFilePath);

            var bytes = File.ReadAllBytes(sourceFilePath);

            // PDFtoImage(PDFium)経由でラスタライズ
            using var bitmap = PDFtoImage.Conversion.ToImage(
                bytes,
                page: pageIndex,
                options: new PDFtoImage.RenderOptions(Dpi: dpi));

            // 出力ディレクトリを保証
            var outDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);

            var skFormat = format == ImageExportFormat.Jpeg
                ? SkiaSharp.SKEncodedImageFormat.Jpeg
                : SkiaSharp.SKEncodedImageFormat.Png;
            int quality = format == ImageExportFormat.Jpeg ? 85 : 100;

            using var data = bitmap.Encode(skFormat, quality);
            File.WriteAllBytes(outputPath, data.ToArray());

            _logger.LogInformation(
                "画像エクスポート: ページ{Page} → {Out} ({Format}, {Dpi}DPI)",
                pageIndex, outputPath, format, dpi);
        }, ct);
    }
}
