# Entwicklung und Veröffentlichung

Die README ist der Einstieg für Nutzer. Dieser Artikel bündelt die Arbeitsabläufe für
lokale Builds, Dokumentation, Screenshots und Veröffentlichungen. Architektur und
fachliche Regeln stehen in den verlinkten Artikeln, nicht in der Nutzerübersicht.

## Lokal bauen und starten

Voraussetzung ist Windows mit dem .NET 10 SDK. Die folgenden Befehle laufen im
Repository-Hauptverzeichnis:

```powershell
dotnet build .\MkvToolnixAutomatisierung.csproj -c Debug
dotnet run --project .\MkvToolnixAutomatisierung.csproj -c Debug --no-build
```

Der Debug-Ausgabeordner ist `bin/Debug/net10.0-windows`. Auch dieser Build verwendet
portable `Data`-, `Logs`- und `Tools`-Ordner neben der EXE. Darin können echte
Einstellungen, Zugangsdaten und heruntergeladene Werkzeuge liegen; nicht pauschal als
Build-Artefakte löschen. Vor dem erneuten Build die laufende App schließen.

App-Builds, Tests, DocFX und Screenshot-Generierung seriell ausführen, weil sie teilweise
dieselben Ausgabeordner verwenden. Die zentralen Analyzer behandeln Warnungen als
Fehler. Testbefehle, isolierte Realmedientests und verbleibende manuelle Prüfungen stehen
in der [Testdokumentation](testing.md).

## Code und Dokumentation

- [Architektur](architecture.md): Composition Root, Module, Services und Datenfluss.
- [Metadaten und Provider](metadata-providers.md): TVDB, IMDb, NFOs und Emby-Abgleich.
- [Regeln und Grenzen](behavior-contracts.md): Heuristiken und bewusste Einschränkungen.
- [Portable Daten](portable-storage.md): Dateipfade, Protokolle und Wiederherstellung.

XML-Dokumentationskommentare im C#-Code liefern die API-Referenz. Konzeptionelle Artikel
liegen unter `docs/articles`; `docs/articles/toc.yml` bindet sie in die Navigation ein.
README und Lizenz werden in den Build-/Publish-Ausgabeordner kopiert. Die README ist
zusätzlich eingebettet, damit Single-File-Releases sie bei Bedarf neben der EXE anlegen
können. Eine README-Änderung wird deshalb erst mit einem neuen Build eingebettet.

## DocFX erzeugen

```powershell
.\scripts\build-docs.ps1
```

Das Skript stellt das im Toolmanifest festgelegte DocFX wieder her, entfernt die
generierten Ordner `docs/api` und `docs/_site` und baut die Dokumentation neu. Für eine
lokale Vorschau mit Webserver:

```powershell
.\scripts\build-docs.ps1 -Serve
```

Die CI behandelt DocFX-Warnungen als Fehler. Dieselbe Prüfung lässt sich nach dem
Toolrestore auch ohne vorherige Bereinigung ausführen:

```powershell
dotnet tool restore
dotnet tool run docfx .\docs\docfx.json --warningsAsErrors
```

## Screenshots pflegen

```powershell
.\scripts\generate-readme-screenshots.ps1
```

Der Generator rendert die vollständige App mit Demodaten nach `docs/images/readme`.
Die Bilder vor einem Commit visuell prüfen. Die normale CI führt den Generator als
Smoke-Test aus, fordert aber keine pixelgleichen Ergebnisse zwischen Windows-Runnern
und lokalem Desktop.

Der Workflow `.github/workflows/readme-screenshots.yml` läuft wöchentlich oder manuell
und aktualisiert bei Bildänderungen einen eigenen PR. Das optionale Repository-Secret
`SCREENSHOT_PR_TOKEN` erlaubt, dass dessen Pushes wiederum CI auslösen. Ohne dieses
Secret wird `GITHUB_TOKEN` verwendet. Verhindern die Repository-Berechtigungen das
Erstellen eines PRs, bleibt der Screenshot-Branch erhalten und der Run meldet den
manuellen PR-Link.

## CI und Releases

`.github/workflows/ci-docs.yml` baut die App in Release, führt Unit- und Integrationstests
sowie den Screenshot-Smoke-Test aus und erzeugt DocFX mit Warnungen als Fehlern. Bei
Pushes auf `master` wird die Dokumentation über GitHub Pages veröffentlicht. Dependabot
pflegt Vorschläge für NuGet- und GitHub-Actions-Updates.

Reguläre Releases werden manuell über `.github/workflows/release.yml` mit einer Version
im Format `Major.Minor.Patch` gestartet. Vorher Änderungen unter `docs/releases` und
betroffene Nutzerdokumentation/Screenshots aktualisieren. Der Workflow baut und testet,
erzeugt Release-Notes, setzt das Versions-Tag und veröffentlicht die EXE.

Für einen rein lokalen Publish ohne Tag oder GitHub-Veröffentlichung:

```powershell
$version = Read-Host 'Release-Version (Major.Minor.Patch)'
.\scripts\publish-release.ps1 -Version $version
```

Die EXE liegt danach unter `artifacts/release`. Der reguläre Release ist eine
framework-dependent Single-File-Anwendung für `win-x64`, benötigt also die .NET 10
Desktop Runtime auf dem Zielrechner. Verwaltete Medienwerkzeuge und der optionale
IMDb-Index werden nicht in die EXE eingebettet.

`.github/workflows/nightly.yml` veröffentlicht denselben Buildtyp als rollendes
Prerelease `nightly`: nachts nur bei neuen Commits, bei manuellem Start auch ohne
Änderungen. Das Prerelease wird neu erstellt, damit GitHub dessen Datum aktualisiert;
der Assetname und damit der direkte Downloadlink bleiben gleich.
