using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PdfStudio.Application;
using PdfStudio.Infrastructure;
using PdfStudio.Wpf.Services;
using PdfStudio.Wpf.ViewModels;
using PdfStudio.Wpf.Views;
using Serilog;

namespace PdfStudio.Wpf;

public partial class App : System.Windows.Application
{
    public static IHost? Host { get; private set; }

    public static T GetService<T>() where T : class
    {
        if (Host?.Services.GetService(typeof(T)) is not T service)
            throw new InvalidOperationException(
                $"{typeof(T).FullName} がDIコンテナに登録されていません。");
        return service;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        // 例外ハンドラ
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Serilog 設定
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PdfStudio", "Logs");
        Directory.CreateDirectory(logDir);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logDir, "pdfstudio-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .WriteTo.Console()
            .CreateLogger();

        // PDFsharp 6.x のフォント解決機構を登録する。
        // これがないと XFont 生成が全て失敗し、スタンプ・ウォーターマーク等が
        // 「使用可能なフォントが見つかりません」で必ずエラーになる。
        try
        {
            PdfStudio.Infrastructure.Pdf.WindowsFontResolver.Register();
            Log.Information(
                "FontResolver登録完了: 既定フォント={Face}, 利用可={Has}, 詳細={Diag}",
                PdfStudio.Infrastructure.Pdf.WindowsFontResolver.DefaultFaceName,
                PdfStudio.Infrastructure.Pdf.WindowsFontResolver.HasUsableFont,
                PdfStudio.Infrastructure.Pdf.WindowsFontResolver.DiagnosticInfo);
        }
        catch (Exception fontEx)
        {
            Log.Error(fontEx, "FontResolver登録に失敗");
        }

        Host = Microsoft.Extensions.Hosting.Host
            .CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((ctx, services) =>
            {
                services.AddApplication();
                services.AddInfrastructure();

                // Presentation
                services.AddSingleton<IDialogService, DialogService>();
                services.AddSingleton<PrintService>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await Host.StartAsync();

        var mainWindow = Host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (Host != null)
        {
            await Host.StopAsync(TimeSpan.FromSeconds(5));
            Host.Dispose();
        }
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(
        object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "未処理のUI例外");
        MessageBox.Show(
            $"予期しないエラーが発生しました:\n{e.Exception.Message}",
            "PdfStudio",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(
        object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Log.Error(ex, "AppDomain未処理例外");
    }

    private void OnUnobservedTaskException(
        object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "未観測のTask例外");
        e.SetObserved();
    }
}
