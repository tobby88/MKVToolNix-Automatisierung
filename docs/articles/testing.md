# Tests

## Schichten

Das Projekt verwendet jetzt drei Testebenen:

- Unit-Tests in `MkvToolnixAutomatisierung.Tests`
  - fokussieren einzelne Services, Parser und ViewModel-Regeln
  - sichern auch kleine, zentralisierte Heuristiken wie Textnormalisierung und Encoding-Reparatur gezielt ab
  - prüfen außerdem den Emby-HTTP-Client, das strukturierte neue-Dateien-Reportformat und NFO-Provider-ID-Updates ohne Live-Emby-Server
- Integrationstests in `MkvToolnixAutomatisierung.IntegrationTests`
  - prüfen mehrere Services zusammen über echte Temp-Dateien und einen kontrollierten Fake-`mkvmerge`
  - decken auch Batch-Scans mit vorbereitetem `BatchScanDirectoryContext` über mehrere Einzeldateien hinweg ab
  - sichern zusätzlich Regressionen ab, bei denen sich Dateierkennung, Archivintegration oder Fake-`mkvmerge` zwischen zwei Schritten unterschiedlich verhalten würden
  - enthalten gezielte Planungsregressionen für Mehrfach-Audio, damit frische Quellen mit mehreren normalen Audiospuren nicht wieder auf die erste Spur reduziert werden
- Architektur-/Bootstrap-Tests in den Unit-Tests
  - prüfen Composition-Root, DI-Registrierung und gezielte Startup-Fehlerpfade
  - sichern auch ab, dass der Root-ServiceProvider bei fehlgeschlagener Startup-Auflösung wieder disposed wird
- manuelle GUI-Prüfung
  - bleibt für Dialoge, WPF-Bindings und visuelle Usability weiterhin sinnvoll

## Kritische Provider- und Emby-Regressionen

Einige Tests sind bewusst auf die zuletzt fehleranfälligen Provider- und Emby-Pfade zugeschnitten:

- TVDB- und IMDb-Dialoge übersetzen Netzwerk-, Timeout- und Dienstfehler in sichtbare Statusmeldungen, statt technische Exceptions bis zur UI durchzureichen.
- TVDB- und IMDb-Pagination ist mit Seitenlimits und Loop-Erkennung abgesichert, damit ein defekter Provider keine endlose Suche auslöst.
- Der Emby-Abgleich prüft lokale `.nfo`-Dateien weiter, wenn Emby-Item- oder Library-Abfragen temporär fehlschlagen.
- Eine explizite Entscheidung `Keine IMDb-ID` wird auch ohne weitere Provider-ID in die NFO geschrieben.
- Reports können als erledigt markiert werden, auch wenn kein Emby-Refresh nötig ist, weil die lokale NFO bereits aktuell war.
- Der Scan-Status unterscheidet zwischen gezieltem Serienbibliotheksscan und sichtbar gemeldetem globalem Fallback.

## FakeMkvMerge

Die Integrationstests verwenden `TestTools/FakeMkvMerge`. Das Hilfsprogramm simuliert:

- `mkvmerge --identify` über sidecar-Dateien `*.mkvmerge.json`
- `mkvextract attachments` über dieselben Probe-Sidecars, sodass eingebettete TXT-Anhänge im Testpfad wie im Produktivpfad extrahiert werden
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
