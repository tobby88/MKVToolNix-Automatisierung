using Microsoft.Extensions.DependencyInjection;
using MkvToolnixAutomatisierung.Services;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.RegularExpressions;

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
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MkvToolnixAutomatisierung", GetApplicationVersionForUserAgent()));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        });
        services.AddSingleton<IManagedToolArchiveExtractor, ManagedToolArchiveExtractor>();
        services.AddSingleton<IManagedToolPackageSource, MkvToolNixPackageSource>();
        services.AddSingleton<IManagedToolPackageSource, FfprobePackageSource>();
        services.AddSingleton<IManagedToolPackageSource, MediathekViewPackageSource>();
        services.AddSingleton<ManagedToolInstallerService>();
        services.AddSingleton<IManagedToolInstallerService>(provider => provider.GetRequiredService<ManagedToolInstallerService>());
        services.AddSingleton<MkvToolNixLocator>(provider => new MkvToolNixLocator(provider.GetRequiredService<AppToolPathStore>()));
        services.AddSingleton<IMkvToolNixLocator>(provider => provider.GetRequiredService<MkvToolNixLocator>());
        services.AddSingleton<FfprobeLocator>(provider => new FfprobeLocator(provider.GetRequiredService<AppToolPathStore>()));
        services.AddSingleton<IFfprobeLocator>(provider => provider.GetRequiredService<FfprobeLocator>());
        services.AddSingleton<IMediathekViewLocator>(provider => new MediathekViewLocator(provider.GetRequiredService<AppToolPathStore>()));
        services.AddSingleton<IMediathekViewLauncher>(provider => new MediathekViewLauncher(provider.GetRequiredService<IMediathekViewLocator>()));
        services.AddSingleton<MkvMergeProbeService>(_ => new MkvMergeProbeService());
        services.AddSingleton<ToolingServices>(provider => new ToolingServices(
            provider.GetRequiredService<MkvToolNixLocator>(),
            provider.GetRequiredService<FfprobeLocator>(),
            provider.GetRequiredService<MkvMergeProbeService>()));
    }

    private static string GetApplicationVersionForUserAgent()
    {
        var assembly = typeof(ToolingCompositionModule).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var version = string.IsNullOrWhiteSpace(informationalVersion)
            ? assembly.GetName().Version?.ToString(3)
            : informationalVersion.Split('+')[0];

        version = string.IsNullOrWhiteSpace(version) ? "0.0.0" : version;
        return Regex.Replace(version, @"[^0-9A-Za-z.!#$%&'*+^_`|~-]", "-");
    }
}
