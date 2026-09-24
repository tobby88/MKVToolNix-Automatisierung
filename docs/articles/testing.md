# Tests

## Schichten

Das Projekt kombiniert automatisierte Tests mit manueller GUI-Prüfung:

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

- TVDB-Dialog und TVDB-basierter IMDb-Abgleich übersetzen Netzwerk-, Timeout- und Dienstfehler in sichtbare Statusmeldungen, statt technische Exceptions bis zur UI durchzureichen.
- TVDB-Pagination ist mit Seitenlimits und Loop-Erkennung abgesichert, damit ein defekter Provider keine endlose Suche auslöst.
- Der Emby-Abgleich prüft lokale `.nfo`-Dateien weiter, wenn Emby-Item- oder Library-Abfragen temporär fehlschlagen.
- IMDb-Remote-IDs aus TVDB werden nur ohne widersprechende Report-, NFO- oder Emby-ID automatisch bestätigt beziehungsweise ergänzt; Konflikte bleiben im manuellen Browserdialog offen.
- Der IMDb-Offlineindex wird aus kleinen echten GZip-TSV-Testdaten aufgebaut und anschließend per SQLite durchsucht. Tests decken abgelehnte und abgebrochene Downloads, atomaren Neuaufbau, schemaerzwungene Neuaufbauten, kompakt gespeicherte Episodenfelder, ungeordnete Episodenzuordnungen, die verwendeten Teilindizes, das alte Alias-Schema, exakte und mehrdeutige Kandidaten sowie den TVDB-zu-Offlineindex-Fallback im Emby-Pflichtcheck ab.
- Die gemeinsame Titelnormalisierung wird für ASCII-, Unicode-, Satzzeichen- und Sonderfolgenvarianten gegen das bisherige Verhalten geprüft. Ein relativer Allokationstest schützt den schnellen IMDb-Importpfad vor einer unbemerkten Rückkehr zu mehreren Regex-, LINQ- und String-Zwischenschritten pro Titel.
- Eine explizite Entscheidung `Keine IMDb-ID` wird auch ohne weitere Provider-ID in die NFO geschrieben.
- Reports können als erledigt markiert werden, auch wenn kein Emby-Refresh nötig ist, weil die lokale NFO bereits aktuell war.
- Getrennte TVDB-/IMDb-Absagen, ihre Rücknahme und der Wiederimport werden einschließlich echter JSON-/NFO-Dateien geprüft. Tests sichern den Wechsel zwischen `partial` und `done`, den Erhalt nicht ausgewählter Einträge, Dateinamenskollisionen und das Zurücknehmen veralteter Abschlussmarker ab. Ein WPF-Test prüft die sofortige Übernahme der Checkboxen und ihre eigene Leertastenbedienung.
- Der Scan verlangt eine eindeutig zugeordnete Serienbibliothek. Explizite Serverpfade und Bibliotheks-IDs sind getestet; ein globaler Fallback ist ausgeschlossen.

## FakeMkvMerge

Die Integrationstests verwenden `TestTools/FakeMkvMerge`. Das Hilfsprogramm simuliert:

- `mkvmerge --identify` über sidecar-Dateien `*.mkvmerge.json`
- `mkvextract attachments` über dieselben Probe-Sidecars, sodass eingebettete TXT-Anhänge im Testpfad wie im Produktivpfad extrahiert werden
- echte Mux-Läufe über `*.mkvmerge.run.json`

Dadurch lassen sich Planung, Prozesssteuerung, Fortschrittsparsing und Cleanup reproduzierbar testen, ohne auf externe Binärdateien oder Live-Mediendateien angewiesen zu sein.

Der Integrationstest-Build stößt den Build dieses Hilfsprogramms automatisch mit derselben Konfiguration an. Die Tests bleiben damit auch ohne direkte Projekt-Referenz auf das Tool reproduzierbar.

## Datenerhalt und Abbruch

Regressionen prüfen die Veröffentlichung temporärer Mux-Ausgaben bei Exit-Code 0/1 sowie den Erhalt vorhandener Ziele bei Fehlern, Abbruch, leeren Ausgaben und fehlerhaften Ausgabe-Callbacks. Weitere Dateisystemtests decken Rename-Rollback mit Sidecars, geschützte Cleanup-Quellen, gesperrte Sortierziele und den Erhalt portabler MediathekView-Daten ab. Alle schreibenden Tests verwenden isolierte Testordner, keine echten Medienarchive.

WPF-Tests prüfen Dispatcher-Zustände, Auswahl/Fokus, Busy-Sperren und die Freigabe tatsächlich angezeigter Pläne. HTTP-Fakes und kleine echte SQLite-/GZip-Datensätze prüfen Providerantworten und Cancellation ohne externe Accounts. Das ersetzt keine vollständige Live-Emby-Prüfung und keinen Leistungsbenchmark mit dem gesamten IMDb-Datenbestand.

Zusätzliche Regressionen sichern exklusive NFO-/Report-Schreibkonflikte, vollständige
Sortierpaket-Rollbacks, reale Hardlinks/Junctions, Extraktions-/Speicherlimits und
Wiederherstellungs-Snapshots ab. 16 gleichzeitig gestartete Threads prüfen die drei
TVDB-Lazy-Request-Factories. Ein echter WPF-Dispatcher plus verzögerter Provider und
Fake-Prozess prüfen die verschachtelte Alternativquellen-Erkennung im Batch.
10.000 Emby-Zeilen und 400 gleichnamige IMDb-Serien prüfen die Skalierungs-/Limitverträge,
nicht eine zugesagte Laufzeit auf jedem Rechner.

## Isolierter Realmedientest

`RealMediaWorkflowTests` erzeugt zwei Sekunden synthetisches Video mit Audio, Untertitel
und TXT-Anhang. Er nutzt den gemeinsamen Mux-Workflow, prüft Dauer/Tracks mit echten
Werkzeugen, führt Header-/NFO-Änderungen aus und verschiebt MKV, NFO und Thumbnail in
eine andere Staffel. Alle Dateien und Settings liegen in eigenen temporären Ordnern.
Ohne explizite Werkzeugpfade wird der Test sichtbar übersprungen, nicht als erfolgreich
ausgegeben. Es gibt keinen automatischen Download dieser Testwerkzeuge.

```powershell
$env:MKV_TEST_TOOLNIX = 'C:\Tools\mkvtoolnix'
$env:MKV_TEST_FFMPEG = 'C:\Tools\ffmpeg\bin\ffmpeg.exe'
$env:MKV_TEST_FFPROBE = 'C:\Tools\ffmpeg\bin\ffprobe.exe'
dotnet test .\MkvToolnixAutomatisierung.IntegrationTests --filter FullyQualifiedName~RealMediaWorkflowTests
```

Bei Plattformtests außerhalb einer eingeschränkten Entwicklungs-Sandbox laufen lassen:
Windows-Vorfahrenprüfungen müssen Metadaten des Benutzerpfads lesen können. Live-Emby,
Mobilfunk/SMB-Ausfälle, Stromausfall, native Mehrmonitor-DPI und Screenreader bleiben
separate Praxisabnahmen. Tests verändern weder das produktive Archiv noch die echte Zwischenablage.

## Lokal ausführen

Build, Unit-Tests, Integrationstests, DocFX und App-Build sollten in diesem Projekt seriell laufen. Parallele Build-/Testläufe können insbesondere wegen gemeinsamer Artefaktpfade und des Fake-Tool-Builds unnötige Kollisionen erzeugen.

Der normale Build nutzt die zentralen .NET-Analyzer in `Directory.Build.props` und behandelt Warnungen als Fehler. Neue Warnungen sollten deshalb wie echte Regressionen behandelt und nicht nur lokal ignoriert werden.

```powershell
dotnet test .\MkvToolnixAutomatisierung.Tests\MkvToolnixAutomatisierung.Tests.csproj
dotnet test .\MkvToolnixAutomatisierung.IntegrationTests\MkvToolnixAutomatisierung.IntegrationTests.csproj
```
