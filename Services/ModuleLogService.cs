using System.Globalization;
using System.Text;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Schreibt sichtbare Modul-Protokolle in den portablen Log-Ordner, ohne sie mit Mux-spezifischen
/// Ausgabedatei-Reports zu vermischen.
/// </summary>
internal interface IModuleLogService
{
    /// <summary>
    /// Persistiert das aktuelle sichtbare Modul-Protokoll als eigene Textdatei.
    /// </summary>
    /// <param name="moduleLabel">Benutzerlesbarer Modulname, z. B. <c>Einsortieren</c>.</param>
    /// <param name="operationLabel">Benutzerlesbarer Vorgang, z. B. <c>Scan</c> oder <c>Sync</c>.</param>
    /// <param name="context">Optionaler Kontext wie Quellordner, Reportpfad oder Archivwurzel.</param>
    /// <param name="logText">Vollständiger UI-Protokolltext.</param>
    /// <returns>Pfad zur geschriebenen Logdatei.</returns>
    ModuleLogSaveResult SaveModuleLog(
        string moduleLabel,
        string operationLabel,
        string? context,
        string logText);
}

/// <summary>
/// Dateibasierte Implementierung für allgemeine Modul-Protokolle.
/// </summary>
internal sealed class ModuleLogService : IModuleLogService
{
    private static readonly UTF8Encoding Utf8Encoding = new(encoderShouldEmitUTF8Identifier: false);

    public ModuleLogSaveResult SaveModuleLog(
        string moduleLabel,
        string operationLabel,
        string? context,
        string logText)
    {
        PortableAppStorage.EnsureLogsDirectoryForSave();

        var createdAt = DateTimeOffset.Now;
        var safeModuleLabel = NormalizeLabel(moduleLabel, fallback: "Modul");
        var safeOperationLabel = NormalizeLabel(operationLabel, fallback: "Protokoll");
        var fileStamp = createdAt.LocalDateTime.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
        var filePath = GetUniqueLogPath($"{safeModuleLabel} - {safeOperationLabel} - {fileStamp}.log.txt");

        File.WriteAllText(
            filePath,
            BuildLogText(createdAt, safeModuleLabel, safeOperationLabel, context, logText),
            Utf8Encoding);

        return new ModuleLogSaveResult(filePath);
    }

    private static string BuildLogText(
        DateTimeOffset createdAt,
        string moduleLabel,
        string operationLabel,
        string? context,
        string logText)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"MKVToolNix-Automatisierung - {moduleLabel}-Protokoll");
        builder.AppendLine($"Erstellt am: {createdAt:dd.MM.yyyy HH:mm:ss}");
        builder.AppendLine($"Vorgang: {operationLabel}");
        if (!string.IsNullOrWhiteSpace(context))
        {
            builder.AppendLine("Kontext:");
            foreach (var line in NormalizeLogText(context).Split(Environment.NewLine))
            {
                builder.AppendLine($"  {line}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Protokoll:");
        var normalizedLogText = NormalizeLogText(logText);
        builder.AppendLine(string.IsNullOrWhiteSpace(normalizedLogText) ? "(leer)" : normalizedLogText.TrimEnd());
        return builder.ToString();
    }

    private static string NormalizeLogText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine,
            text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .Select(MojibakeRepair.NormalizeLikelyMojibake));
    }

    private static string NormalizeLabel(string value, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            normalized = normalized.Replace(invalidCharacter, '_');
        }

        return normalized;
    }

    private static string GetUniqueLogPath(string fileName)
    {
        var path = Path.Combine(PortableAppStorage.LogsDirectory, fileName);
        if (!File.Exists(path))
        {
            return path;
        }

        var stem = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(fileName));
        for (var index = 2; index < 10_000; index++)
        {
            var candidate = Path.Combine(PortableAppStorage.LogsDirectory, $"{stem} ({index}).log.txt");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(PortableAppStorage.LogsDirectory, $"{stem} - {Guid.NewGuid():N}.log.txt");
    }
}

/// <summary>
/// Ergebnis eines geschriebenen allgemeinen Modul-Protokolls.
/// </summary>
/// <param name="LogPath">Pfad zur geschriebenen Logdatei.</param>
internal sealed record ModuleLogSaveResult(string LogPath);
