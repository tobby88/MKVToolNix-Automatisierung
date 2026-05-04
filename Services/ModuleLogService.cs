using System.Globalization;
using System.Text;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Schreibt sichtbare Modul-Protokolle in ein fortlaufendes Sitzungslog, ohne sie mit Mux-spezifischen
/// Ausgabedatei-Reports zu vermischen.
/// </summary>
internal interface IModuleLogService
{
    /// <summary>
    /// Persistiert neue Zeilen des aktuellen sichtbaren Modul-Protokolls im Sitzungslog.
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
/// Dateibasierte Implementierung für allgemeine Modul-Protokolle mit einer Datei pro App-Sitzung.
/// </summary>
internal sealed class ModuleLogService : IModuleLogService
{
    private static readonly UTF8Encoding Utf8Encoding = new(encoderShouldEmitUTF8Identifier: false);
    private readonly Dictionary<string, string> _lastSavedLogByContext = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private readonly DateTimeOffset _sessionStartedAt = DateTimeOffset.Now;
    private string? _sessionLogPath;

    public ModuleLogSaveResult SaveModuleLog(
        string moduleLabel,
        string operationLabel,
        string? context,
        string logText)
    {
        PortableAppStorage.EnsureLogsDirectoryForSave();
        var safeModuleLabel = NormalizeLabel(moduleLabel, fallback: "Modul");
        var safeOperationLabel = NormalizeLabel(operationLabel, fallback: "Protokoll");
        var normalizedContext = NormalizeLogText(context);
        var normalizedLogText = PersistedLogTextCleaner.Clean(logText);
        var logKey = $"{safeModuleLabel}\0{normalizedContext}";

        lock (_sync)
        {
            _sessionLogPath ??= CreateSessionLogPath(_sessionStartedAt);
            EnsureSessionHeader(_sessionLogPath, _sessionStartedAt);

            var newLogText = GetNewLogText(logKey, normalizedLogText);
            if (!string.IsNullOrWhiteSpace(newLogText))
            {
                File.AppendAllText(
                    _sessionLogPath,
                    BuildOperationSection(DateTimeOffset.Now, safeModuleLabel, safeOperationLabel, normalizedContext, newLogText),
                    Utf8Encoding);
            }

            return new ModuleLogSaveResult(_sessionLogPath);
        }
    }

    private string GetNewLogText(string logKey, string normalizedLogText)
    {
        _lastSavedLogByContext.TryGetValue(logKey, out var previousLogText);
        _lastSavedLogByContext[logKey] = normalizedLogText;

        if (string.IsNullOrWhiteSpace(normalizedLogText))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(previousLogText))
        {
            return normalizedLogText;
        }

        if (string.Equals(previousLogText, normalizedLogText, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var expectedPrefix = previousLogText + Environment.NewLine;
        return normalizedLogText.StartsWith(expectedPrefix, StringComparison.Ordinal)
            ? normalizedLogText[expectedPrefix.Length..]
            : normalizedLogText;
    }

    private static void EnsureSessionHeader(string filePath, DateTimeOffset sessionStartedAt)
    {
        if (File.Exists(filePath))
        {
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine("MKVToolNix-Automatisierung - Sitzungsprotokoll");
        builder.AppendLine($"Gestartet am: {sessionStartedAt:dd.MM.yyyy HH:mm:ss}");
        builder.AppendLine();
        File.WriteAllText(filePath, builder.ToString(), Utf8Encoding);
    }

    private static string BuildOperationSection(
        DateTimeOffset createdAt,
        string moduleLabel,
        string operationLabel,
        string normalizedContext,
        string normalizedLogText)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine($"Zeit: {createdAt:dd.MM.yyyy HH:mm:ss}");
        builder.AppendLine($"Modul: {moduleLabel}");
        builder.AppendLine($"Vorgang: {operationLabel}");
        if (!string.IsNullOrWhiteSpace(normalizedContext))
        {
            builder.AppendLine("Kontext:");
            foreach (var line in normalizedContext.Split(Environment.NewLine))
            {
                builder.AppendLine($"  {line}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Protokoll:");
        builder.AppendLine(normalizedLogText.TrimEnd());
        builder.AppendLine();
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

    private static string CreateSessionLogPath(DateTimeOffset sessionStartedAt)
    {
        var fileStamp = sessionStartedAt.LocalDateTime.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
        return GetUniqueLogPath($"Sitzung - {fileStamp}.log.txt");
    }
}

/// <summary>
/// Ergebnis eines geschriebenen allgemeinen Modul-Protokolls.
/// </summary>
/// <param name="LogPath">Pfad zur geschriebenen Logdatei.</param>
internal sealed record ModuleLogSaveResult(string LogPath);
