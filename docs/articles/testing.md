# Tests

## Schichten

Das Projekt verwendet jetzt drei Testebenen:

- Unit-Tests in `MkvToolnixAutomatisierung.Tests`
  - fokussieren einzelne Services, Parser und ViewModel-Regeln
- Integrationstests in `MkvToolnixAutomatisierung.IntegrationTests`
  - prüfen mehrere Services zusammen über echte Temp-Dateien und einen kontrollierten Fake-`mkvmerge`
- manuelle GUI-Prüfung
  - bleibt für Dialoge, WPF-Bindings und visuelle Usability weiterhin sinnvoll

## FakeMkvMerge

Die Integrationstests verwenden `TestTools/FakeMkvMerge`. Das Hilfsprogramm simuliert:

- `mkvmerge --identify` über sidecar-Dateien `*.mkvmerge.json`
- echte Mux-Läufe über `*.mkvmerge.run.json`

Dadurch lassen sich Planung, Prozesssteuerung, Fortschrittsparsing und Cleanup reproduzierbar testen, ohne auf externe Binärdateien oder Live-Mediendateien angewiesen zu sein.

## Lokal ausführen

```powershell
dotnet test .\MkvToolnixAutomatisierung.Tests\MkvToolnixAutomatisierung.Tests.csproj
dotnet test .\MkvToolnixAutomatisierung.IntegrationTests\MkvToolnixAutomatisierung.IntegrationTests.csproj
```
