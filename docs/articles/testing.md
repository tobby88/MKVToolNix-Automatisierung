# Tests

## Schichten

Das Projekt verwendet jetzt drei Testebenen:

- Unit-Tests in `MkvToolnixAutomatisierung.Tests`
  - fokussieren einzelne Services, Parser und ViewModel-Regeln
  - sichern auch kleine, zentralisierte Heuristiken wie Textnormalisierung und Encoding-Reparatur gezielt ab
- Integrationstests in `MkvToolnixAutomatisierung.IntegrationTests`
  - prüfen mehrere Services zusammen über echte Temp-Dateien und einen kontrollierten Fake-`mkvmerge`
  - decken auch Batch-Scans mit vorbereitetem `BatchScanDirectoryContext` über mehrere Einzeldateien hinweg ab
  - sichern zusätzlich Regressionen ab, bei denen sich Dateierkennung, Archivintegration oder Fake-`mkvmerge` zwischen zwei Schritten unterschiedlich verhalten würden
- manuelle GUI-Prüfung
  - bleibt für Dialoge, WPF-Bindings und visuelle Usability weiterhin sinnvoll

## FakeMkvMerge

Die Integrationstests verwenden `TestTools/FakeMkvMerge`. Das Hilfsprogramm simuliert:

- `mkvmerge --identify` über sidecar-Dateien `*.mkvmerge.json`
- echte Mux-Läufe über `*.mkvmerge.run.json`

Dadurch lassen sich Planung, Prozesssteuerung, Fortschrittsparsing und Cleanup reproduzierbar testen, ohne auf externe Binärdateien oder Live-Mediendateien angewiesen zu sein.

Der Integrationstest-Build stößt den Build dieses Hilfsprogramms automatisch mit derselben Konfiguration an. Die Tests bleiben damit auch ohne direkte Projekt-Referenz auf das Tool reproduzierbar.

## Lokal ausführen

Build, Unit-Tests, Integrationstests, DocFX und App-Build sollten in diesem Projekt seriell laufen. Parallele Build-/Testläufe können insbesondere wegen gemeinsamer Artefaktpfade und des Fake-Tool-Builds unnötige Kollisionen erzeugen.

Der normale Build nutzt die zentralen .NET-Analyzer in `Directory.Build.props` und behandelt Warnungen als Fehler. Neue Warnungen sollten deshalb wie echte Regressionen behandelt und nicht nur lokal ignoriert werden.

```powershell
dotnet test .\MkvToolnixAutomatisierung.Tests\MkvToolnixAutomatisierung.Tests.csproj
dotnet test .\MkvToolnixAutomatisierung.IntegrationTests\MkvToolnixAutomatisierung.IntegrationTests.csproj
```
