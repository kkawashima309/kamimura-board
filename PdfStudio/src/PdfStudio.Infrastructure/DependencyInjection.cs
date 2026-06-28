using Microsoft.Extensions.DependencyInjection;
using PdfStudio.Application.Services;
using PdfStudio.Domain.Interfaces;
using PdfStudio.Infrastructure.Pdf;
using PdfStudio.Infrastructure.Persistence;

namespace PdfStudio.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IPdfRenderer, PdfiumRenderer>();
        services.AddSingleton<IPdfEditor, PdfSharpEditor>();
        services.AddSingleton<IPdfSecurityService, PdfSharpSecurityService>();
        services.AddSingleton<IPdfSearchService, PdfPigSearchService>();
        services.AddSingleton<IPdfAnnotationService, PdfSharpAnnotationService>();
        services.AddSingleton<IPdfOcrService, TesseractOcrService>();
        services.AddSingleton<BatchService>();
        services.AddSingleton<IRecentFilesService, RecentFilesRepository>();
        return services;
    }
}
