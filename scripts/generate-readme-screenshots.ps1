$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Push-Location $root
try {
    dotnet run --project .\tools\ReadmeScreenshotGenerator\ReadmeScreenshotGenerator.csproj -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "README-Screenshot-Generator wurde mit Exit-Code $LASTEXITCODE beendet."
    }
}
finally {
    Pop-Location
}
