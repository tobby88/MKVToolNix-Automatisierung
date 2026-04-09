[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [ValidateSet('win-x64', 'win-arm64', 'win-x86')]
    [string]$RuntimeIdentifier = 'win-x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-AbsolutePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,

        [Parameter(Mandatory = $true)]
        [string]$ChildPath
    )

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $ChildPath))
}

function Assert-IsUnderRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath,

        [Parameter(Mandatory = $true)]
        [string]$CandidatePath
    )

    $normalizedRoot = [System.IO.Path]::TrimEndingDirectorySeparator($RootPath)
    $normalizedCandidate = [System.IO.Path]::TrimEndingDirectorySeparator($CandidatePath)

    $rootPrefix = $normalizedRoot + [System.IO.Path]::DirectorySeparatorChar
    $isExactRoot = $normalizedCandidate.Equals($normalizedRoot, [System.StringComparison]::OrdinalIgnoreCase)
    $isNestedPath = $normalizedCandidate.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)

    if (-not ($isExactRoot -or $isNestedPath)) {
        throw "Pfad liegt außerhalb des erwarteten Arbeitsbereichs: $CandidatePath"
    }
}

$repoRoot = Resolve-AbsolutePath -BasePath $PSScriptRoot -ChildPath '..'
$projectPath = Resolve-AbsolutePath -BasePath $repoRoot -ChildPath 'MkvToolnixAutomatisierung.csproj'
$temporaryPublishDirectory = Resolve-AbsolutePath -BasePath $repoRoot -ChildPath "artifacts\publish-temp\$RuntimeIdentifier\$Version"
$releaseDirectory = Resolve-AbsolutePath -BasePath $repoRoot -ChildPath 'artifacts\release'
$releaseFileName = "MkvToolnixAutomatisierung-v$Version-$RuntimeIdentifier.exe"
$releaseFilePath = Join-Path $releaseDirectory $releaseFileName
$assemblyVersion = "$Version.0"

Assert-IsUnderRoot -RootPath $repoRoot -CandidatePath $temporaryPublishDirectory
Assert-IsUnderRoot -RootPath $repoRoot -CandidatePath $releaseDirectory

$shortCommit = (& git -C $repoRoot rev-parse --short HEAD).Trim()
if ([string]::IsNullOrWhiteSpace($shortCommit)) {
    throw 'Die aktuelle Commit-ID konnte nicht bestimmt werden.'
}

$informationalVersion = "$Version+$shortCommit"

if (Test-Path -LiteralPath $temporaryPublishDirectory) {
    Remove-Item -LiteralPath $temporaryPublishDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $temporaryPublishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null

& dotnet publish $projectPath `
    -c Release `
    -r $RuntimeIdentifier `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -p:AssemblyVersion=$assemblyVersion `
    -p:FileVersion=$assemblyVersion `
    -p:InformationalVersion=$informationalVersion `
    --output $temporaryPublishDirectory

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish ist mit Exit-Code $LASTEXITCODE fehlgeschlagen."
}

$publishedExecutablePath = Join-Path $temporaryPublishDirectory 'MkvToolnixAutomatisierung.exe'
if (-not (Test-Path -LiteralPath $publishedExecutablePath)) {
    throw "Die erwartete veröffentlichte Exe wurde nicht gefunden: $publishedExecutablePath"
}

Copy-Item -LiteralPath $publishedExecutablePath -Destination $releaseFilePath -Force

Write-Host "Release-Datei erstellt:"
Write-Host $releaseFilePath
