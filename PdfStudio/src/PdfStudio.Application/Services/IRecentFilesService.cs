namespace PdfStudio.Application.Services;

/// <summary>
/// 最近使ったファイルの管理。
/// </summary>
public interface IRecentFilesService
{
    IReadOnlyList<string> GetRecentFiles();
    void Add(string filePath);
    void Remove(string filePath);
    void Clear();
}
