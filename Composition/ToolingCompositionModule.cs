using Microsoft.Extensions.DependencyInjection;
using MkvToolnixAutomatisierung.Services;
using System.Net.Http;
using System.Net.Http.Headers;

namespace MkvToolnixAutomatisierung.Composition;

/// <summary>
/// Verdrahtet die Tool-Locatoren und Probe-Services für mkvmerge und ffprobe.
/// </summary>
internal static class ToolingCompositionModule
{
    /// <summary>
    /// Registriert die Werkzeug-Services auf Basis der gespeicherten Toolpfade.
    /// </summary>
    /// <param name="services">DI-Sammlung für Locator- und Probe-Registrierungen.</param>
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<HttpClient>(_ =>
        {
            var client = new HttpClient();
            // Der Start darf bei langsamen oder blockierten Upstream-Diensten nicht minutenlang hängen bleiben.
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MkvToolnixAutomatisierung", "1.2.0"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        });
        services.AddSingleton<IManagedToolArchiveExtractor, ManagedToolArchiveExtractor>();
        services.AddSingleton<IManagedToolPackageSource, MkvToolNixPackageSource>();
        services.AddSingleton<IManagedToolPackageSource, FfprobePackageSource>();
        services.AddSingleton<ManagedToolInstallerService>();
        services.AddSingleton<MkvToolNixLocator>(provider => new MkvToolNixLocator(provider.GetRequiredService<AppToolPathStore>()));
        services.AddSingleton<IMkvToolNixLocator>(provider => provider.GetRequiredService<MkvToolNixLocator>());
        services.AddSingleton<FfprobeLocator>(provider => new FfprobeLocator(provider.GetRequiredService<AppToolPathStore>()));
        services.AddSingleton<IFfprobeLocator>(provider => provider.GetRequiredService<FfprobeLocator>());
        services.AddSingleton<MkvMergeProbeService>(_ => new MkvMergeProbeService());
        services.AddSingleton<ToolingServices>(provider => new ToolingServices(
            provider.GetRequiredService<MkvToolNixLocator>(),
            provider.GetRequiredService<FfprobeLocator>(),
            provider.GetRequiredService<MkvMergeProbeService>()));
    }
}
