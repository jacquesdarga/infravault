using InfraVault.Application.Interfaces;
using InfraVault.Application.Services;
using InfraVault.Application.UseCases;
using InfraVault.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InfraVault.Application;

public static class DependencyInjection
{
    /// <summary>Enregistre les cas d'utilisation. Tous les services sont des singletons sans état métier (hors session).</summary>
    public static IServiceCollection AddInfraVaultApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddSingleton<IVaultSession, VaultSession>();
        services.AddSingleton<AuditService>();
        services.AddSingleton<AuthenticationService>();
        services.AddSingleton<VaultService>();
        services.AddSingleton<InfrastructureService>();
        services.AddSingleton<CredentialService>();
        services.AddSingleton<DocumentationService>();
        services.AddSingleton<SearchService>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<TopologyService>();
        services.AddSingleton<DemoDataSeeder>();
        return services;
    }
}
