using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MkvToolnixAutomatisierung.Services;

/// <summary>
/// Löst die aktuelle portable Windows-Version von MKVToolNix aus der offiziellen Downloadseite auf.
/// </summary>
internal sealed class MkvToolNixPackageSource : IManagedToolPackageSource
{
    private static readonly Uri DownloadsPageUri = new("https://mkvtoolnix.download/downloads.html");
    private static readonly Regex DownloadRegex = new(
        @"(?<url>(?:https://mkvtoolnix\.download/)?windows/releases/[^""'\s>]+/mkvtoolnix-64-bit-(?<version>\d+(?:\.\d+)+)\.7z)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex InlineChecksumRegex = new(
        @"data-checksum=""(?<sha256>[a-fA-F0-9]{64})""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly HttpClient _httpClient;

    public MkvToolNixPackageSource(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public ManagedToolKind Kind => ManagedToolKind.MkvToolNix;

    /// <inheritdoc />
    public async Task<ManagedToolPackage> GetLatestPackageAsync(CancellationToken cancellationToken = default)
    {
        using var downloadsResponse = await _httpClient.GetAsync(DownloadsPageUri, cancellationToken);
        downloadsResponse.EnsureSuccessStatusCode();
        var downloadsHtml = await downloadsResponse.Content.ReadAsStringAsync(cancellationToken);

        var package = ParseLatestPackageFromDownloadsPage(downloadsHtml);
        if (!string.IsNullOrWhiteSpace(package.ExpectedSha256))
        {
            return package;
        }

        using var checksumResponse = await _httpClient.GetAsync(new Uri(package.DownloadUri, "sha256sums.txt"), cancellationToken);
        checksumResponse.EnsureSuccessStatusCode();
        var checksumText = await checksumResponse.Content.ReadAsStringAsync(cancellationToken);

        return ParseLatestPackageFromDownloadsPage(downloadsHtml, checksumText);
    }

    /// <summary>
    /// Parst das aktuelle portable MKVToolNix-Paket aus der offiziellen HTML-Downloadseite.
    /// </summary>
    /// <param name="downloadsHtml">HTML der MKVToolNix-Downloadseite.</param>
    /// <param name="checksumText">Optional bereits geladenes <c>sha256sums.txt</c> aus demselben Release-Verzeichnis.</param>
    /// <returns>Aufgelöstes Paket samt Version, Downloadlink und optionaler Prüfsumme.</returns>
    internal static ManagedToolPackage ParseLatestPackageFromDownloadsPage(string downloadsHtml, string? checksumText = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadsHtml);

        var candidates = DownloadRegex.Matches(downloadsHtml)
            .Select(match =>
            {
                var url = match.Groups["url"].Value;
                return new
                {
                    Url = url,
                    Version = match.Groups["version"].Value,
                    Sha256 = TryReadInlineChecksum(downloadsHtml, match.Index)
                };
            })
            .DistinctBy(candidate => candidate.Url, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("Auf der MKVToolNix-Downloadseite wurde kein portables Windows-Archiv gefunden.");
        }

        var selected = candidates
            .OrderByDescending(candidate => ManagedToolParsing.ParseVersionParts(candidate.Version), ManagedToolParsing.VersionPartsComparer)
            .First();
        var downloadUri = new Uri(DownloadsPageUri, selected.Url);
        var archiveFileName = Path.GetFileName(downloadUri.LocalPath);
        var checksum = !string.IsNullOrWhiteSpace(selected.Sha256)
            ? selected.Sha256
            : ManagedToolParsing.TryReadSha256FromChecksumText(checksumText, archiveFileName);

        return new ManagedToolPackage(
            ManagedToolKind.MkvToolNix,
            selected.Version,
            selected.Version,
            downloadUri,
            archiveFileName,
            checksum);
    }

    private static string? TryReadInlineChecksum(string downloadsHtml, int matchIndex)
    {
        if (matchIndex < 0 || matchIndex >= downloadsHtml.Length)
        {
            return null;
        }

        var rowEnd = downloadsHtml.IndexOf("</tr>", matchIndex, StringComparison.OrdinalIgnoreCase);
        var listEnd = downloadsHtml.IndexOf("</li>", matchIndex, StringComparison.OrdinalIgnoreCase);
        var blockEndCandidates = new[] { rowEnd, listEnd }
            .Where(index => index >= matchIndex)
            .OrderBy(index => index)
            .ToArray();
        var scanLength = blockEndCandidates.Length > 0
            ? Math.Max(0, blockEndCandidates[0] - matchIndex)
            : Math.Min(768, downloadsHtml.Length - matchIndex);
        var snippet = downloadsHtml.Substring(matchIndex, scanLength);
        var checksumMatch = InlineChecksumRegex.Match(snippet);

        return checksumMatch.Success
            ? checksumMatch.Groups["sha256"].Value
            : null;
    }
}

/// <summary>
/// Löst das aktuelle Windows-ffprobe-Paket über die von ffmpeg.org verlinkten BtbN-Builds auf.
/// </summary>
internal sealed class FfprobePackageSource : IManagedToolPackageSource
{
    private static readonly Uri LatestReleaseApiUri = new("https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest");
    private readonly HttpClient _httpClient;

    public FfprobePackageSource(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public ManagedToolKind Kind => ManagedToolKind.Ffprobe;

    /// <inheritdoc />
    public async Task<ManagedToolPackage> GetLatestPackageAsync(CancellationToken cancellationToken = default)
    {
        using var releaseResponse = await _httpClient.GetAsync(LatestReleaseApiUri, cancellationToken);
        releaseResponse.EnsureSuccessStatusCode();
        var releaseJson = await releaseResponse.Content.ReadAsStringAsync(cancellationToken);
        var parsedRelease = ParseLatestRelease(releaseJson);

        string? checksumText = null;
        if (parsedRelease.Package.ExpectedSha256 is null && parsedRelease.ChecksumDownloadUri is not null)
        {
            using var checksumResponse = await _httpClient.GetAsync(parsedRelease.ChecksumDownloadUri, cancellationToken);
            checksumResponse.EnsureSuccessStatusCode();
            checksumText = await checksumResponse.Content.ReadAsStringAsync(cancellationToken);
        }

        return ParseLatestPackageFromReleaseJson(releaseJson, checksumText);
    }

    /// <summary>
    /// Parst das aktuelle Windows-ffprobe-Paket aus dem GitHub-Release-JSON der BtbN-Builds.
    /// </summary>
    /// <param name="releaseJson">JSON des <c>latest</c>-Release-Endpunkts.</param>
    /// <param name="checksumText">Optional bereits geladener Checksum-Inhalt aus demselben Release.</param>
    /// <returns>Aufgelöstes Paket samt Download-URL und wenn möglich SHA-256-Prüfsumme.</returns>
    internal static ManagedToolPackage ParseLatestPackageFromReleaseJson(string releaseJson, string? checksumText = null)
    {
        var parsedRelease = ParseLatestRelease(releaseJson);
        if (parsedRelease.Package.ExpectedSha256 is not null || string.IsNullOrWhiteSpace(checksumText))
        {
            return parsedRelease.Package;
        }

        var checksum = ManagedToolParsing.TryReadSha256FromChecksumText(checksumText, parsedRelease.Package.ArchiveFileName);
        return parsedRelease.Package with { ExpectedSha256 = checksum };
    }

    private static ParsedRelease ParseLatestRelease(string releaseJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseJson);

        using var document = JsonDocument.Parse(releaseJson);
        var root = document.RootElement;
        var releaseName = root.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString()
            : null;
        var fallbackTimestamp = ManagedToolParsing.NormalizeTimestampToken(root.TryGetProperty("published_at", out var publishedAtElement)
            ? publishedAtElement.GetString()
            : null);

        var assetElements = root.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array
            ? assetsElement.EnumerateArray().ToArray()
            : [];
        if (assetElements.Length == 0)
        {
            throw new InvalidOperationException("Das BtbN-Release enthält keine herunterladbaren Assets.");
        }

        var selectedAsset = assetElements
            .Select(asset => new
            {
                Asset = asset,
                Name = asset.TryGetProperty("name", out var assetNameElement) ? assetNameElement.GetString() ?? string.Empty : string.Empty
            })
            .Where(entry => entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .Where(entry => entry.Name.Contains("win64", StringComparison.OrdinalIgnoreCase))
            .Where(entry => entry.Name.Contains("gpl", StringComparison.OrdinalIgnoreCase))
            .Where(entry => entry.Name.Contains("shared", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => GetAssetSelectionRank(entry.Name))
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.Asset)
            .FirstOrDefault();
        if (selectedAsset.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException("Im BtbN-Release wurde kein geeignetes win64 shared ZIP-Asset für ffprobe gefunden.");
        }

        var archiveFileName = selectedAsset.GetProperty("name").GetString() ?? throw new InvalidOperationException("Das ausgewählte ffprobe-Asset hat keinen Dateinamen.");
        var downloadUrl = selectedAsset.GetProperty("browser_download_url").GetString() ?? throw new InvalidOperationException("Das ausgewählte ffprobe-Asset hat keinen Downloadlink.");
        var digest = selectedAsset.TryGetProperty("digest", out var digestElement)
            ? digestElement.GetString()
            : null;
        var expectedSha256 = digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true
            ? digest["sha256:".Length..]
            : null;

        var versionToken = ManagedToolParsing.NormalizeTimestampToken(
            selectedAsset.TryGetProperty("updated_at", out var updatedAtElement)
                ? updatedAtElement.GetString()
                : null)
            ?? fallbackTimestamp
            ?? archiveFileName;
        var displayVersion = !string.IsNullOrWhiteSpace(releaseName)
            ? releaseName!
            : versionToken;

        var checksumAsset = assetElements
            .FirstOrDefault(asset =>
            {
                var name = asset.TryGetProperty("name", out var assetNameElement)
                    ? assetNameElement.GetString()
                    : null;
                return !string.IsNullOrWhiteSpace(name)
                       && (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(name, "checksums.sha256", StringComparison.OrdinalIgnoreCase));
            });
        var checksumDownloadUri = checksumAsset.ValueKind == JsonValueKind.Undefined
            ? null
            : new Uri(checksumAsset.GetProperty("browser_download_url").GetString()!, UriKind.Absolute);

        return new ParsedRelease(
            new ManagedToolPackage(
                ManagedToolKind.Ffprobe,
                versionToken,
                displayVersion,
                new Uri(downloadUrl, UriKind.Absolute),
                archiveFileName,
                expectedSha256),
            checksumDownloadUri);
    }

    private static int GetAssetSelectionRank(string assetName)
    {
        if (string.Equals(assetName, "ffmpeg-master-latest-win64-gpl-shared.zip", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return assetName.Contains("-latest-win64-gpl-shared.zip", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 2;
    }

    private sealed record ParsedRelease(ManagedToolPackage Package, Uri? ChecksumDownloadUri);
}

/// <summary>
/// Löst die aktuelle stabile portable Windows-Version von MediathekView aus dem offiziellen Downloadverzeichnis auf.
/// </summary>
internal sealed class MediathekViewPackageSource : IManagedToolPackageSource
{
    private static readonly Uri StableDownloadsUri = new("https://download.mediathekview.de/stabil/");
    private static readonly Regex WindowsZipRegex = new(
        @"href=""(?<url>/stabil/MediathekView-(?<version>\d+(?:\.\d+)+)-win\.zip)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly HttpClient _httpClient;

    public MediathekViewPackageSource(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public ManagedToolKind Kind => ManagedToolKind.MediathekView;

    /// <inheritdoc />
    public async Task<ManagedToolPackage> GetLatestPackageAsync(CancellationToken cancellationToken = default)
    {
        using var downloadsResponse = await _httpClient.GetAsync(StableDownloadsUri, cancellationToken);
        downloadsResponse.EnsureSuccessStatusCode();
        var downloadsHtml = await downloadsResponse.Content.ReadAsStringAsync(cancellationToken);
        var packageWithoutChecksum = ParseLatestPackageFromDownloadsPage(downloadsHtml);

        using var checksumResponse = await _httpClient.GetAsync(new Uri(packageWithoutChecksum.DownloadUri.ToString() + ".SHA-512"), cancellationToken);
        checksumResponse.EnsureSuccessStatusCode();
        var checksumText = await checksumResponse.Content.ReadAsStringAsync(cancellationToken);

        return ParseLatestPackageFromDownloadsPage(downloadsHtml, checksumText);
    }

    /// <summary>
    /// Parst das aktuelle Windows-ZIP aus dem offiziellen stabilen Downloadverzeichnis.
    /// </summary>
    /// <param name="downloadsHtml">HTML-Listing von <c>download.mediathekview.de/stabil</c>.</param>
    /// <param name="checksumText">Optional geladener Inhalt der zugehörigen <c>.SHA-512</c>-Datei.</param>
    /// <returns>Paketmetadaten für die aktuelle stabile Windows-ZIP-Version.</returns>
    internal static ManagedToolPackage ParseLatestPackageFromDownloadsPage(string downloadsHtml, string? checksumText = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadsHtml);

        var candidates = WindowsZipRegex.Matches(downloadsHtml)
            .Select(match => new
            {
                Url = match.Groups["url"].Value,
                Version = match.Groups["version"].Value
            })
            .DistinctBy(candidate => candidate.Url, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("Im MediathekView-Downloadverzeichnis wurde kein stabiles Windows-ZIP gefunden.");
        }

        var selected = candidates
            .OrderByDescending(candidate => ManagedToolParsing.ParseVersionParts(candidate.Version), ManagedToolParsing.VersionPartsComparer)
            .First();
        var downloadUri = new Uri(StableDownloadsUri, selected.Url);
        var archiveFileName = Path.GetFileName(downloadUri.LocalPath);

        return new ManagedToolPackage(
            ManagedToolKind.MediathekView,
            selected.Version,
            selected.Version,
            downloadUri,
            archiveFileName,
            ExpectedSha512: ManagedToolParsing.TryReadSha512FromChecksumText(checksumText));
    }
}

/// <summary>
/// Bündelt parser- und checksum-spezifische Hilfsfunktionen der Toolquellen.
/// </summary>
internal static class ManagedToolParsing
{
    internal static IComparer<int[]> VersionPartsComparer { get; } = Comparer<int[]>.Create(CompareVersionPartArrays);

    /// <summary>
    /// Vergleicht zwei Versions-Token auf Basis ihrer numerischen Punktsegmente.
    /// </summary>
    internal static int CompareVersionTokens(string? left, string? right)
    {
        return CompareVersionPartArrays(ParseVersionParts(left), ParseVersionParts(right));
    }

    /// <summary>
    /// Zerlegt ein punktgetrenntes Versions-Token in numerische Segmente.
    /// </summary>
    internal static int[] ParseVersionParts(string? versionToken)
    {
        if (string.IsNullOrWhiteSpace(versionToken))
        {
            return [];
        }

        return versionToken
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0)
            .ToArray();
    }

    /// <summary>
    /// Liest die SHA-256-Prüfsumme für einen Dateinamen aus einem textbasierten Checksum-Listing.
    /// </summary>
    internal static string? TryReadSha256FromChecksumText(string? checksumText, string archiveFileName)
    {
        if (string.IsNullOrWhiteSpace(checksumText) || string.IsNullOrWhiteSpace(archiveFileName))
        {
            return null;
        }

        foreach (var rawLine in checksumText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = rawLine.Trim();
            if (!line.EndsWith(archiveFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var firstSeparatorIndex = line.IndexOfAny([' ', '\t']);
            if (firstSeparatorIndex <= 0)
            {
                continue;
            }

            var candidate = line[..firstSeparatorIndex].Trim();
            return IsValidSha256(candidate)
                ? candidate
                : null;
        }

        return null;
    }

    /// <summary>
    /// Liest eine reine SHA-512-Prüfsumme aus einer Checksummendatei.
    /// </summary>
    internal static string? TryReadSha512FromChecksumText(string? checksumText)
    {
        if (string.IsNullOrWhiteSpace(checksumText))
        {
            return null;
        }

        foreach (var token in checksumText.Split(['\r', '\n', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IsValidSha512(token))
            {
                return token;
            }
        }

        return null;
    }

    /// <summary>
    /// Prüft, ob ein Text eine formal gültige hexadezimale SHA-256-Prüfsumme darstellt.
    /// </summary>
    internal static bool IsValidSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
        {
            return false;
        }

        return value.All(Uri.IsHexDigit);
    }

    /// <summary>
    /// Prüft, ob ein Text eine formal gültige hexadezimale SHA-512-Prüfsumme darstellt.
    /// </summary>
    internal static bool IsValidSha512(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 128)
        {
            return false;
        }

        return value.All(Uri.IsHexDigit);
    }

    /// <summary>
    /// Normalisiert ISO-Zeitstempel in dateitaugliche Vergleichstoken.
    /// </summary>
    internal static string? NormalizeTimestampToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value
            .Trim()
            .Replace(":", "-", StringComparison.Ordinal)
            .Replace("/", "-", StringComparison.Ordinal)
            .Replace("\\", "-", StringComparison.Ordinal);
    }

    private static int CompareVersionPartArrays(int[] left, int[] right)
    {
        var maxLength = Math.Max(left.Length, right.Length);
        for (var index = 0; index < maxLength; index++)
        {
            var leftValue = index < left.Length ? left[index] : 0;
            var rightValue = index < right.Length ? right[index] : 0;
            var comparison = leftValue.CompareTo(rightValue);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }
}
