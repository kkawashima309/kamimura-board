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

        // PDFsharp のフォント解決を起動時に構成する。
        // (未構成だとスタンプ・付箋・ウォーターマーク等の文字描画が例外で失敗する)
        PdfStudio.Infrastructure.Pdf.PdfFontSetup.EnsureConfigured();

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
