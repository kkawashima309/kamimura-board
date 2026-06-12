using Microsoft.Extensions.DependencyInjection;
using PdfStudio.Application.Common;
using PdfStudio.Application.Services;
using PdfStudio.Domain.Interfaces;

namespace PdfStudio.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<UndoRedoStack>();
        services.AddSingleton<ILicenseService, NullLicenseService>();
        return services;
    }
}
