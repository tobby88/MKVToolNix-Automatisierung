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
    public static void Register(AppServiceRegistry services)
    {
        services.AddSingleton<MkvToolNixLocator>(provider => new MkvToolNixLocator(provider.GetRequired<AppToolPathStore>()));
        services.AddSingleton<IMkvToolNixLocator>(provider => provider.GetRequired<MkvToolNixLocator>());
        services.AddSingleton<FfprobeLocator>(provider => new FfprobeLocator(provider.GetRequired<AppToolPathStore>()));
        services.AddSingleton<IFfprobeLocator>(provider => provider.GetRequired<FfprobeLocator>());
        services.AddSingleton<MkvMergeProbeService>(_ => new MkvMergeProbeService());
        services.AddSingleton<ToolingServices>(provider => new ToolingServices(
            provider.GetRequired<MkvToolNixLocator>(),
            provider.GetRequired<FfprobeLocator>(),
            provider.GetRequired<MkvMergeProbeService>()));
    }
}
