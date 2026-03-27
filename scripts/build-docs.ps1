param(
    [switch]$Serve
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Push-Location $root
try {
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
