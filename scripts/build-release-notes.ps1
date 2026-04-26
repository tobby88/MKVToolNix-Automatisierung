[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [string]$Repository = $env:GITHUB_REPOSITORY,

    [string]$TargetCommitish = $env:GITHUB_SHA
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-GeneratedNotes {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryName,

        [Parameter(Mandatory = $true)]
        [string]$TagName,

        [string]$Commitish
    )

    if ([string]::IsNullOrWhiteSpace($RepositoryName) -or [string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
        return ''
    }

    $arguments = @(
        'api',
        "repos/$RepositoryName/releases/generate-notes",
        '-f',
        "tag_name=$TagName"
    )

    if (-not [string]::IsNullOrWhiteSpace($Commitish)) {
        $arguments += @('-f', "target_commitish=$Commitish")
    }

    $json = & gh @arguments
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($json)) {
        return ''
    }

    $generated = $json | ConvertFrom-Json
    return [string]$generated.body
}

function Resolve-CuratedNotesPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReleaseVersion
    )

    $repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot "docs\releases\$ReleaseVersion.md"))
}

$tagName = "v$Version"
$curatedNotesPath = Resolve-CuratedNotesPath -ReleaseVersion $Version
if (Test-Path -LiteralPath $curatedNotesPath) {
    $outputDirectory = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
        New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    }

    Copy-Item -LiteralPath $curatedNotesPath -Destination $OutputPath -Force
    return
}

$generatedNotes = if ([string]::IsNullOrWhiteSpace($Repository)) {
    ''
} else {
    Resolve-GeneratedNotes -RepositoryName $Repository -TagName $tagName -Commitish $TargetCommitish
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("## MKVToolNix-Automatisierung $Version")
$lines.Add('')
$lines.Add('Lokaler Windows-Release zur Automatisierung von MKVToolNix-Muxing für Serienepisoden aus MediathekView-Downloads.')
$lines.Add('')
$lines.Add('### Enthalten')
$lines.Add('')
$lines.Add('- Einzelmodus für eine Folge mit automatischer Erkennung und manueller Korrekturmöglichkeit')
$lines.Add('- Batchmodus zum Scannen und Verarbeiten ganzer Serien-Downloadordner')
$lines.Add('- Erkennung von Hauptvideo, Audiodeskription, Untertiteln und TXT-Metadatenanhängen')
$lines.Add('- Archivabgleich gegen die vorhandene Serienbibliothek')
$lines.Add('- TVDB-Abgleich inklusive manueller Prüfung')
$lines.Add('- portable lokale Einstellungen unter `Data\settings.json`')
$lines.Add('')
$lines.Add('### Voraussetzungen')
$lines.Add('')
$lines.Add('- Windows')
$lines.Add('- .NET 10 Desktop Runtime')
$lines.Add('- MKVToolNix und ffprobe werden beim Start automatisch in `Tools` bereitgestellt, sofern kein manueller Override gesetzt ist')
$lines.Add('')
$lines.Add('### Download')
$lines.Add('')
$lines.Add('Die bereitgestellte `.exe` ist framework-dependent und enthält daher nicht die komplette .NET-Laufzeit. Das hält die Datei deutlich kleiner; dafür muss die passende .NET Desktop Runtime installiert sein.')

if (-not [string]::IsNullOrWhiteSpace($generatedNotes)) {
    $lines.Add('')
    $lines.Add('### Änderungen')
    $lines.Add('')
    $lines.Add($generatedNotes.Trim())
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

Set-Content -LiteralPath $OutputPath -Value $lines -Encoding UTF8
