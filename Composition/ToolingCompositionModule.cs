using Microsoft.Extensions.DependencyInjection;
using MkvToolnixAutomatisierung.Services;

namespace MkvToolnixAutomatisierung.Composition;

/// <summary>
/// Verdrahtet die Tool-Locatoren und Probe-Services für mkvmerge und ffprobe.
/// </summary>
internal static class ToolingCompositionModule
{
    /// <summary>
    /// Registriert die Werkzeug-Services auf Basis der gespeicherten Toolpfade.
    /// </summary>
    public static void Register(IServiceCollection services)
    {
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
