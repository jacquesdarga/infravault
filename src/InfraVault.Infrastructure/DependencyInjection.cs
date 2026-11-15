using InfraVault.Application.Interfaces;
using InfraVault.Application.Services;
using InfraVault.Infrastructure.Backup;
using InfraVault.Infrastructure.Cryptography;
using InfraVault.Infrastructure.Persistence;
using InfraVault.Infrastructure.Reporting;
using InfraVault.Infrastructure.Repositories;
using InfraVault.Infrastructure.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace InfraVault.Infrastructure;

public static class DependencyInjection
{
    /// <param name="dataDirectory">Dossier de données ; null = %LOCALAPPDATA%\InfraVault.</param>
    public static IServiceCollection AddInfraVaultInfrastructure(this IServiceCollection services, string? dataDirectory = null)
    {
        var paths = new AppPaths(dataDirectory);
        services.AddSingleton(paths);
        services.AddSingleton<IAppPaths>(paths);

        services.AddSingleton<VaultDbFactory>();
        services.AddSingleton<IVaultDbFactory>(sp => sp.GetRequiredService<VaultDbFactory>());
        services.AddSingleton<SqliteSearchIndex>();
        services.AddSingleton<ISearchIndex>(sp => sp.GetRequiredService<SqliteSearchIndex>());
        services.AddSingleton<IDatabaseFiles, DatabaseFiles>();
        services.AddSingleton<IFileStorage, FileStorageService>();

        services.AddSingleton<IEncryptionService, AesGcmEncryptionService>();
        services.AddSingleton<IKeyDerivationService>(_ => new Argon2KeyDerivationService());
        services.AddSingleton<IPlatformSecurityService, WindowsSecurityService>();
        services.AddSingleton<IArchitectureReportWriter, DocxArchitectureReportWriter>();
        return services;
    }
}
