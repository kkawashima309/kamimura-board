namespace PdfStudio.Wpf.ViewModels;

/// <summary>
/// ページ表示のズームモード。
/// </summary>
public enum ZoomMode
{
    /// <summary>ユーザー指定の任意倍率</summary>
    Custom,
    /// <summary>ウィンドウにフィット(全体表示)</summary>
    FitPage,
    /// <summary>幅にフィット</summary>
    FitWidth,
    /// <summary>実寸(100%)</summary>
    Actual,
}
