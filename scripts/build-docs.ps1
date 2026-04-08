param(
    [switch]$Serve
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$apiOutputPath = Join-Path $root "docs\api"
$siteOutputPath = Join-Path $root "docs\_site"

function Remove-GeneratedDocsOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

Push-Location $root
try {
    # DocFX lässt gelöschte API-Seiten sonst lokal leicht als Altlast zurück.
    # Vor jedem Build wird deshalb der komplette generierte Output neu aufgebaut.
    Remove-GeneratedDocsOutput -Path $apiOutputPath
    Remove-GeneratedDocsOutput -Path $siteOutputPath

    dotnet tool restore

    if ($Serve) {
        dotnet tool run docfx docs/docfx.json --serve
    }
    else {
        dotnet tool run docfx docs/docfx.json
    }
}
finally {
    Pop-Location
}
