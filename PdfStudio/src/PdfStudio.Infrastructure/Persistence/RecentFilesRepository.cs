using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PdfStudio.Application.Services;

namespace PdfStudio.Infrastructure.Persistence;

/// <summary>
/// 最近使ったファイルをJSONファイルで永続化する実装。
/// 保存先: %APPDATA%/PdfStudio/recent.json
/// </summary>
public sealed class RecentFilesRepository : IRecentFilesService
{
    private const int MaxItems = 15;
    private readonly string _filePath;
    private readonly ILogger<RecentFilesRepository> _logger;
    private List<string> _items = new();

    public RecentFilesRepository(ILogger<RecentFilesRepository> logger)
    {
        _logger = logger;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "PdfStudio");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "recent.json");
        Load();
    }

    public IReadOnlyList<string> GetRecentFiles() =>
        _items.Where(File.Exists).ToList();

    public void Add(string filePath)
    {
        _items.RemoveAll(p => string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase));
        _items.Insert(0, filePath);
        if (_items.Count > MaxItems)
            _items = _items.Take(MaxItems).ToList();
        Save();
    }

    public void Remove(string filePath)
    {
        if (_items.RemoveAll(p =>
                string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            Save();
        }
    }

    public void Clear()
    {
        _items.Clear();
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var json = File.ReadAllText(_filePath);
            _items = JsonSerializer.Deserialize<List<string>>(json) ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "最近使ったファイルの読み込みに失敗しました");
            _items = new();
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(
                _items,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "最近使ったファイルの保存に失敗しました");
        }
    }
}
