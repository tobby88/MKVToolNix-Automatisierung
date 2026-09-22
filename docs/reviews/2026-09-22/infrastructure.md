# Review: Infrastruktur, Persistenz und Ausführung

Ausgangspunkt: `323634f`. Dieser Bereich wurde im Hauptlauf geprüft und mit den sechs parallel bearbeiteten Fachbereichen integriert. Keine echten Medien, Emby-NFOs, Benutzereinstellungen oder installierten IMDb-Datenbanken wurden verändert.

## Behobene Befunde

| ID | Priorität | Befund und Änderung | Regression |
| --- | --- | --- | --- |
| I01 | P1 | Direkte Mux-Ausgabe konnte ein vorhandenes Archivziel bei Abbruch zerstören. `MuxOutputTransaction` hält es bis zur erfolgreichen Veröffentlichung intakt. Die Zwischenausgabe endet ausschließlich auf `.tmp`, damit Emby sie nicht als zusätzliche Mediendatei einliest. | `MuxOutputTransactionTests`, `MuxExecutionServiceIntegrationTests`, bestehende Workflow-Integration |
| I02 | P2 | Unbehandelte Ausnahmen aus Ausgabe-Callbacks konnten Prozess-Eventthreads verlassen. Der Dienst serialisiert beide Streams, beendet bei Callbackfehlern den Prozess und reicht die ursprüngliche Ausnahme über den Task zurück, ohne die Ausgabe zu veröffentlichen. | `ExecuteAsync_OutputCallbackFailureStopsProcessAndPreservesArchive` |
| I03 | P2 | Datei-Copy konnte trotz zwischenzeitlicher Cancellation noch veröffentlichen. Token wird vor Beginn und unmittelbar vor dem finalen Move erneut geprüft. | `FileCopyServiceTests` einschließlich Abbruch im letzten Fortschrittscallback |
| I04 | P2 | Ein fehlgeschlagener Log-Append setzte trotzdem den Fortschreibcursor weiter. Er wird erst nach erfolgreichem Schreiben aktualisiert; eine extern entfernte Logdatei setzt die Cursor zurück. | `ModuleLogServiceTests` mit Dateisperre und Wiederanlage |
| I05 | P2 | Parallele Einzel-/Batch-Abschlüsse konnten denselben Reportnamen wählen. Namenswahl und sämtliche Artefaktwrites stehen nun unter derselben Servicesperre. | `BatchRunLogServiceTests`, 16 konkurrierende Saves |
| I06 | P2 | JSON `null` verhinderte den Settings-Backup-Fallback; explizit null gesetzte Toolobjekte oder Archiv-Suppressionen verursachten Fehler beim Klonen. Null-Dokumente gelten als defekt, optionale Tools bleiben bei Normalisierung deaktiviert. | `AppSettingsFileLocatorTests`, `AppSettingsStoreTests` |
| I07 | P2 | Ein eindeutiger Archiv-Titelmatch konnte bekannte widersprechende Staffel-/Folgenummern übergehen. Bei bekanntem Code ist nun ein eindeutiger Code-Match erforderlich. | `EpisodeOutputPathServiceTests` mit widersprechender Staffel/Folge |
| I08 | P2 | Plan-Cache prüfte Dateizustände bei festen Quelllisten synchron auf dem Dispatcher und übersprang Cancellation. Alle Schlüsselberechnungen laufen nun auf einem Worker und respektieren das Token. | `EpisodePlanCacheTests` |
| I09 | P2 | TXT-BOMs verdeckten das erste Label; leere Werte konnten die nächste Zeile verschlucken. BOM-bewusstes Lesen und zeilenbegrenzte Labels korrigieren beides. | `CompanionTextMetadataReaderTests`, UTF-8/UTF-16 sowie CRLF/LF |
| I10 | P2 | Encoding-Reparatur konnte legitime Zeichen wie in `Mâcon` durch Ersatzzeichen beschädigen. Nur verlustfreie Rückübersetzungen werden als Kandidat zugelassen. | `MojibakeRepairTests` |
| I11 | P3 | Ungenutzter synchroner Composition-Wrapper blockierte auf einem Async-Task. Er wurde entfernt, sein einziger Test verwendet den produktiven Async-Einstieg. | `AppCompositionRootTests` |
| I12 | P2 | Release-Notes-Workflow hatte zu wenig Git-Historie für den Push-Diff und kontrollierte CLI-Fehler nicht. Vollständige Historie, geprüfte Exitcodes und Behandlung noch nicht veröffentlichter Releases verhindern stille Fehlläufe. | Statische PowerShell-/Workflow-Prüfung, kein Remote-Run |
| I13 | P2/P3 | Screenshot-PR-Updates nutzten trotz konfiguriertem PAT teils das Standard-Push-Token; Release-Kommandos ignorierten Fehler und interpolierten Eingaben in Skriptcode. Checkout-Token, explizite Fehlerbehandlung, Umgebungsvariablen und Tag-Verifikation sind jetzt konsistent. Pages-Konfiguration läuft nicht bei PRs. | PowerShell-AST-Prüfung; Screenshot-Generator wird lokal ausgeführt |
| I14 | P2 | Globale Einstellungen konnten laufende Workflows verändern; nach Speichern mit anschließendem Dialogabbruch wurden Module nicht aktualisiert. Die Shell beobachtet den gemeinsamen Interaktionszustand, sperrt Modulwechsel/Einstellungen während der Arbeit und liest nach jedem Settingsdialog neu. | `MainWindowViewModelTests`, Composition-Vertrag für alle Module; ergänzende Mux-Tab-Tests |

## Geprüfter Kontext

Gelesen wurden insbesondere Composition und Modulverdrahtung, `MuxWorkflowCoordinator`, `SingleMuxExecutionCoordinator`, `MuxExecutionService`, `FileCopyService`, Ergebnis-/Dateizustandsmodelle, Outputpfade, Plan-Cache, Settings-Stores, portable Datenablage, Sitzungs-/Crashlogs, Dialogdienst, Text-/Dateinamenhelfer, MainWindow und Startup-Layout. Ferner CI-, Nightly-, Release-, Screenshot- und Dokumentationsskripte sowie das Screenshot-Hilfsprojekt. Unveränderte Dateien wurden nicht künstlich umgeschrieben. Die Fachbereichsberichte dokumentieren die angrenzenden Prüfungen und ihre eigenen Grenzen.

## Grenzen

- Die temporäre Ausgabe braucht zusätzlichen freien Speicher am Ausgabeziel. Same-volume-Veröffentlichung vermeidet eine weitere vollständige Kopie. Ein harter Prozessabsturz kann einen `.mux-*`-Ordner hinterlassen; normale Fehler und Cancellation räumen eigene temporäre Dateien auf.
- Der Zielzustandsvergleich nutzt Größe und Zeitstempel. Er ist keine prozessübergreifende Sperre und schließt externe TOCTOU-Rennen, Hardlink-/Junction-Aliase oder absichtlich identische Dateistatistik nicht aus.
- Direkte Header-Edits bleiben in-place; NFO-, Header- und Rename-Schritte sind keine gemeinsame Transaktion. Kein Power-loss-/Netzlaufwerk-Ausfalltest oder echter Medienmux wurde durchgeführt.
- Die Shell-Sperre schützt den normalen UI-Ablauf, nicht zwei separat gestartete App-Instanzen oder fremde Programme.
- CI-Workflows wurden lokal geprüft, nicht durch einen Push auf GitHub ausgeführt. Die bestehenden Coverage-Kompatibilitätskopien wurden nicht ohne nachgewiesene Alternative entfernt.

Die zentral tatsächlich ausgeführten Test-, Screenshot-, DocFX- und Build-Ergebnisse stehen im [Gesamtbericht](README.md).
