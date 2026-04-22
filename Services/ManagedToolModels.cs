namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Kennzeichnet die beiden von der App automatisch verwaltbaren Werkzeuge.
/// </summary>
internal enum ManagedToolKind
{
    /// <summary>
    /// MKVToolNix inklusive <c>mkvmerge.exe</c> und <c>mkvpropedit.exe</c>.
    /// </summary>
    MkvToolNix,

    /// <summary>
    /// Optionales <c>ffprobe.exe</c> für zuverlässigere Laufzeitmessungen.
    /// </summary>
    Ffprobe
}

/// <summary>
/// Beschreibt ein von einer offiziellen Downloadquelle aufgelöstes Toolpaket.
/// </summary>
/// <param name="Kind">Werkzeug, zu dem das Paket gehört.</param>
/// <param name="VersionToken">Vergleichbarer Schlüssel für Install-/Updateentscheidungen.</param>
/// <param name="DisplayVersion">Benutzerlesbare Versionsdarstellung für Status und Tooltips.</param>
/// <param name="DownloadUri">Direkter Downloadlink zum Archiv.</param>
/// <param name="ArchiveFileName">Dateiname des herunterzuladenden Archivs.</param>
/// <param name="ExpectedSha256">Optional erwartete SHA-256-Prüfsumme.</param>
internal sealed record ManagedToolPackage(
    ManagedToolKind Kind,
    string VersionToken,
    string DisplayVersion,
    Uri DownloadUri,
    string ArchiveFileName,
    string? ExpectedSha256 = null);

/// <summary>
/// Ergebnis des Start-Upgrades für automatisch verwaltete Werkzeuge.
/// </summary>
/// <param name="Warnings">Benutzerrelevante Warnungen, falls ein Werkzeug nicht automatisch bereitgestellt werden konnte.</param>
internal sealed record ManagedToolStartupResult(IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// Kennzeichnet, ob eine Warnung an die UI weitergereicht werden sollte.
    /// </summary>
    public bool HasWarning => Warnings.Count > 0;

    /// <summary>
    /// Verdichtete Mehrzeilenmeldung für den Startdialog.
    /// </summary>
    public string? WarningMessage => HasWarning
        ? string.Join(Environment.NewLine + Environment.NewLine, Warnings)
        : null;
}

/// <summary>
/// Laufender Status der Werkzeugprüfung beim App-Start.
/// </summary>
/// <param name="StatusText">Kurzer Hauptstatus für den sichtbaren Startdialog.</param>
/// <param name="DetailText">Optionaler Detailtext, etwa Werkzeugname oder Bytefortschritt.</param>
/// <param name="ProgressPercent">Optionaler Prozentwert für determinate Schritte.</param>
/// <param name="IsIndeterminate">Kennzeichnet Schritte ohne belastbaren Prozentfortschritt.</param>
internal sealed record ManagedToolStartupProgress(
    string StatusText,
    string? DetailText = null,
    double? ProgressPercent = null,
    bool IsIndeterminate = true);

/// <summary>
/// Liefert die aktuelle Download-Metadaten eines automatisch verwalteten Werkzeugs.
/// </summary>
internal interface IManagedToolPackageSource
{
    /// <summary>
    /// Kennzeichnet das Werkzeug, für das diese Quelle Metadaten auflöst.
    /// </summary>
    ManagedToolKind Kind { get; }

    /// <summary>
    /// Ermittelt das aktuell bereitgestellte Paket aus der jeweiligen Primärquelle.
    /// </summary>
    /// <param name="cancellationToken">Abbruchsignal für Netzwerkzugriffe.</param>
    /// <returns>Aufgelöste Paketmetadaten inklusive Download-URL und Prüfsumme.</returns>
    Task<ManagedToolPackage> GetLatestPackageAsync(CancellationToken cancellationToken = default);
}
