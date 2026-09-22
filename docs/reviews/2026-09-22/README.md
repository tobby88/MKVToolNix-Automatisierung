# Gesamtreview 2026-09-22

Ausgangspunkt: `323634f` (inklusive Untertitelersatz beim Hauptquellenwechsel).
Ziel: belegbare Fehler, Inkonsistenzen, Performance- und Wartbarkeitsprobleme
einschließlich kleiner Befunde prüfen und direkt mit Regressionstests beheben.
Keine funktional unbegründeten Großumbauten oder Paketupdates.

**Für die weitere Auswahl:** [Konsolidierte offene Findings, Hinweise und Testlücken](open-findings.md).
Diese Restliste berücksichtigt die späteren Korrekturen und ersetzt ältere Offen-/Übergabevermerke
in den einzelnen Bereichsberichten. Der Stand bis `9ff3a8e` wurde inzwischen gepusht.

## Arbeitsliste

- [x] Mux-Kern: Erkennung, Archivvergleich, Spurenauswahl, Argumente und Medien-Probes.
- [x] Mux-Oberfläche: Einzel/Batch, Zustände, Tastaturbedienung, gemeinsame WPF-Helfer.
- [x] Emby: Scan, Pfadzuordnung, NFOs, Pflichtchecks und Reportabschluss.
- [x] Metadaten: TVDB, IMDb-Offlineindex, Matching und Suchdialoge.
- [x] Werkzeuge und Start: Downloads, Updates, Entpacken, Einstellungen und Startdialog.
- [x] Archivpflege und Einsortieren: Korrekturen, Sidecars, Auswahl und Aufräumen.
- [x] Infrastruktur: Workflow-Koordination, Persistenz, Logs, Pfade und Composition.
- [x] Build/CI/Dokumentation: Workflows, Skripte, Screenshot-Werkzeug und Beschreibungen.
- [x] Befunde integriert, thematische Commits erstellt, abschließenden Diff geprüft.
- [x] Vollständige Unit-/WPF- und Integrationstests sowie regulären Debug-Build geprüft.

Die Bereichsberichte in diesem Ordner enthalten Befunde, Änderungen, Tests und
verbleibende Grenzen. Builds und Tests laufen seriell, damit WPF-generierte Dateien
und Testwerkzeuge nicht gleichzeitig in denselben Ausgabepfaden gebaut werden.

## Bereichsberichte

| Bereich | Bericht |
| --- | --- |
| Planung, Spuren, Header, Probes | [Mux-Kern](mux-core.md) |
| Einzel-/Batch-Abläufe, Auswahl, Abbruch | [Mux-Oberfläche](mux-ui.md) |
| Server, NFO, Provider-Prüfung, Reportabschluss | [Emby](emby.md) |
| Matching, TVDB, IMDb-Import und Suchdialoge | [Metadaten](metadata.md) |
| Installation, Updates, Start, Einstellungen | [Werkzeuge](tooling.md) |
| Archivpflege, Sidecars, Einsortieren, Cleanup | [Archiv und Einsortieren](archive-sort.md) |
| Persistenz, Dateiveröffentlichung, Shell, CI | [Infrastruktur](infrastructure.md) |

Die Bereichsberichte enthalten auch Zwischenstände der Übergaben. Für den tatsächlich
integrierten und getesteten Gesamtstand ist die Verifikation am Ende dieser Seite maßgeblich.

## Wichtige Verhaltensänderungen

- Vollständige Mux-Ausgaben werden zunächst als `.tmp` am Ziel erstellt. Erst der erfolgreiche
  Abschluss veröffentlicht die MKV. Emby erhält dadurch keine zusätzliche temporäre Mediendatei.
  Der zusätzliche Speicherbedarf und Crash-Reste sind in der README beschrieben.
- Batch-Konflikte zwischen Ausgaben und Quellen werden blockiert. Gemeinsam verwendete
  Quellen bleiben konservativ liegen. Bei Abbruch bleiben erfolgreiche Teilresultate in Reports erhalten.
- Ein aktiver Vorgang sperrt Modul-/Mux-Tabwechsel und globale Einstellungen. Der aktive
  Abbruchknopf bleibt erreichbar. Das verhindert konkurrierende GUI-Abläufe, keine fremden Prozesse.
- Bei Headerkorrekturen bleibt der tatsächliche Sprachwert der Datei getrennt vom aus dem
  Tracknamen abgeleiteten Sprachwert. Fremdsprachige vorhandene AD-Spuren werden nicht entfernt,
  nur weil eine deutsche AD ersetzt wird. Untertitelersatz bleibt an den Hauptquellenwechsel gebunden.
- Der IMDb-Index verwendet Schema 4. Ein Neuaufbau wird nur mit Zustimmung angeboten;
  ein vorhandener Index bleibt bei Ablehnung oder fehlgeschlagenem Import bestehen. Es wurde
  kein echter großer Datensatz heruntergeladen und kein Vollimport-Benchmark durchgeführt.
- MediathekView-Versionen mit möglicherweise enthaltenen Benutzerdaten werden nicht mehr
  pauschal gelöscht. Das erhöht gegebenenfalls den Speicherbedarf, schützt aber Einstellungen/Downloads.
- NFO- und Report-Zusatzdaten bleiben beim Roundtrip erhalten. Eine erfolgreiche Emby-Refresh-
  Anforderung wird nicht mit dem beobachteten Serverabschluss gleichgesetzt.

## Bewusst Verbleibende Grenzen

- Externe parallele NFO-/Report-Schreibvorgänge sind nicht prozessübergreifend gesperrt.
  Read-Modify-Replace kann bei einem genau gleichzeitigen fremden Write weiterhin dessen
  Änderung verdrängen. Auch Header-, NFO- und Rename-Schritte sind keine gemeinsame Transaktion.
- Serien-Fuzzy-Suche lädt weiter einen begrenzten Präfix-Kandidatenraum; Tippfehler am
  Wortanfang können passende Serien ausschließen. Kein ungemessener Vollscan-/FTS-Umbau.
- Windows-umgeleitete Downloads-Ordner, einige synchrone Dateiexistenzprüfungen in UI-Statuspfaden,
  sehr große Log-/Listenmengen und sämtliche High-DPI-/Screenreader-Zustände sind nicht vollständig
  modernisiert bzw. lastgetestet. Konkrete Restpunkte stehen in den Bereichsberichten.
- Es gab keinen Live-Test mit Emby/TVDB, echten Archivmedien, Netzwerkshare-Ausfällen oder
  mehreren gleichzeitig gestarteten App-Instanzen. Die Integrationstests verwenden isolierte
  Dateien, synthetische Archive und kontrollierte Testprozesse.
- Keine Paketupdates ohne fachlichen Anlass, keine neu erfundene Hintergrund- oder Plugin-
  Infrastruktur. CI-Skripte sind lokal geprüft; Remote-Workflows benötigen einen späteren Push.

## Ausgangslage

- Sauberer Arbeitsbaum; ein noch nicht gepushter Commit.
- Letzter geprüfter Stand: 823 Unit-/WPF-Tests und 109 Integrationstests erfolgreich.
- Keine Benutzerkonfigurationen, echten Archivdateien oder lokalen Datenbanken verändern.

## Verifikation

| Prüfung | Beobachtetes Ergebnis |
| --- | --- |
| `dotnet test MkvToolnixAutomatisierung.Tests --no-restore --verbosity minimal` | 1167 bestanden, 0 fehlgeschlagen, 0 übersprungen |
| `dotnet test MkvToolnixAutomatisierung.IntegrationTests --no-restore --verbosity minimal` | 133 bestanden, 0 fehlgeschlagen, 0 übersprungen |
| `dotnet build MkvToolnixAutomatisierung.csproj -c Debug --no-restore` | Erfolgreich, 0 Warnungen, 0 Fehler; reguläre EXE und DLL vorhanden |
| `dotnet tool run docfx docs/docfx.json --warningsAsErrors` | Erfolgreich, 0 Warnungen, 0 Fehler |
| `dotnet run --project tools/ReadmeScreenshotGenerator/ReadmeScreenshotGenerator.csproj -c Release --no-restore` | Erfolgreich; geänderte Download-/Emby-/Batch-Abbildungen visuell geprüft |
| PowerShell-Skripte und Workflow-Skriptblöcke | AST-Prüfung ohne Syntaxfehler; native Exitcodes und Token-/Eingabeverwendung statisch geprüft |
| `git diff --check` | Keine Whitespacefehler |

Damit sind 1300 automatisierte Tests grün, gegenüber dem Ausgangsstand 344 zusätzliche
Unit-/WPF- und 24 zusätzliche Integrationstestfälle. Die Zahlen sind Testfälle einschließlich
Theory-Datensätzen, keine Behauptung über vollständige Pfad- oder Zeilenabdeckung.

Alle Änderungen sind in thematischen englischsprachigen Commits festgehalten. Der vorherige
Commit `323634f` bleibt unverändert enthalten. Kein Push, Release, echter Medienmux oder
Schreibzugriff auf das Benutzerarchiv wurde für diesen Review ausgeführt.
