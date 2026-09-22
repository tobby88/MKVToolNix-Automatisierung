# Offene Punkte nach dem Gesamtreview

Konsolidierter Stand nach `9ff3a8e`, erstellt am 2026-09-22. Die 28 bis dahin lokalen
Commits wurden auf `GitHub/master` gepusht. Diese Liste ist eine Entscheidungsvorlage,
keine Freigabe für weitere technische Änderungen.

Die Bereichsberichte enthalten ältere Übergabestände. Hier sind Doppelungen entfernt
und nachträgliche Korrekturen berücksichtigt. **Kein bestätigtes P1-Finding aus dem
Review ist noch offen.** P2 bezeichnet relevante Fehler-/Datenrisiken; P3 kleinere
funktionale oder Wartbarkeitsprobleme. Hinweise sind ausdrücklich nicht automatisch
reproduzierte Bugs. Aufwandsschätzungen sind relativ und keine Zeitgarantie.

## Offene Findings

| Nr. | Einordnung | Verbleibender Punkt und Auswirkung | Möglicher nächster Schritt | Aufwand |
| --- | --- | --- | --- | --- |
| O01 | P2, strukturelles Risiko | NFO-/Report-Schreiben kann eine gleichzeitige externe Änderung verdrängen. Atomarer Dateiaustausch schützt vor halben Dateien, nicht davor, veraltete gelesene Werte über einen neueren Stand zu schreiben. Auch der Archiv-NFO-Vorabvergleich schließt das letzte Zeitfenster vor dem Write nicht. | Dateiversions-/Inhaltsvergleich unmittelbar vor Veröffentlichung, Konflikte melden statt still überschreiben; notwendige Sperren anhand des tatsächlichen Emby-Verhaltens festlegen. | Mittel |
| O02 | P2, strukturelles Risiko | Archivpflege ist über Header, Provider-IDs, NFO-Texte und Umbenennung hinweg nicht atomar. Schlägt ein späterer Schritt fehl, können frühere Änderungen bestehen bleiben. Direkte `mkvpropedit`-Änderungen erfolgen weiterhin in-place. | Vorabprüfung ergänzen und eine explizite Wiederaufnahme-/Rollback-Strategie festlegen; vollständige MKV-Backups sind eine Speicher-/Laufzeitentscheidung. | Groß |
| O03 | P2 | Ein Downloadpaket wird nicht als Einheit verschoben. Nach erfolgreichem Video-Move und fehlgeschlagenem Sidecar-Move kann es auf zwei Ordner verteilt bleiben. Die Teilresultate werden jetzt korrekt protokolliert, aber nicht vollständig zurückgerollt. | Paketweises Move-Protokoll und Rücknahme bereits ausgeführter Schritte bei normalen Fehlern. | Mittel |
| O04 | P2 | Beim Einsortieren passieren zulässige Ordnerumbenennungen vor der vollständigen Quellen-/Zielprüfung der ausgewählten Pakete. Ein später abgewiesener Auftrag kann deshalb schon einen Ordner umbenannt haben. | Alle relevanten Vorprüfungen vor die erste Änderung ziehen. | Mittel |
| O05 | P2, Sonderfall | Pfadvergleich ist überwiegend lexikalisch und ignoriert Groß-/Kleinschreibung. Junctions, Symlinks, Hardlinks, unterschiedliche UNC-Aliase oder case-sensitive Unterbäume können dieselbe physische Datei anders benennen bzw. eine Ordnergrenze umgehen. Sichtbare Download-Ziel-Reparse-Points werden bereits abgewiesen, aber nicht alle Vorfahren/Aliase erfasst. | Unterstützte Link-/Aliasfälle definieren, konservativ ablehnen oder physische Pfad-/Dateiidentität prüfen. | Groß |
| O06 | P3 | Manuelle Zieldateinamen prüfen noch nicht alle Windows-Gerätenamen und Pfadlängenlimits. Ein Fehler kann erst beim Move auftreten; fremde Ziele werden dabei nicht überschrieben. | Frühzeitige Validierung mit konkreter Fehlermeldung und Grenzfalltests. | Klein |
| O07 | P3, Erweiterung | Nicht bekannte Sprach-/Regionalsuffixe bei Sidecars sowie weitere Artwork-Namensschemata bleiben bei Umbenennung ggf. zurück. Das ist derzeit bewusst konservativ, um keine fremden Dateien mitzunehmen. | Unterstützte Muster anhand konkreter Beispiele erweitern, keine pauschalen Präfix-Moves. | Klein bis mittel |
| O08 | P3 | Manuelle Trackwerte sind nicht vollständig gegen erlaubte `mkvpropedit`-Properties validiert. Unbekannte Flagtexte können intern als `false` interpretiert werden; die normale Flag-UI bietet bereits nur Ja/Nein an. | Gemeinsame strikte Validierung für UI und programmatische Requests. | Klein bis mittel |
| O09 | P3 | IMDb-Seriensuche kann bei Tippfehlern am Wortanfang oder häufigen Präfixen passende Serien verpassen. Der Kandidatenraum ist auf 256 SQL-Zeilen und anschließend zwölf Serien begrenzt; auch ein höheres `maximumResults` überwindet diese interne Grenze nicht. | Kandidatensuche verbessern und Ergebnislimit konsistent machen, ohne langsamen Komplettscan. | Mittel |
| O10 | P3, Performance-Risiko | Einige UI-Statusprüfungen verwenden weiterhin synchrone Dateizugriffe, etwa Toolpfade in den Einstellungen und Archivexistenz im Mux-Editor. Langsame Netzpfade können die Oberfläche blockieren. | Statusprüfungen entprellen/auslagern und veraltete Antworten verwerfen. | Mittel |
| O11 | P3, Optimierung ohne Lastmessung | Emby-Zusammenfassungen laufen mehrfach über alle Zeilen, Logtext wird wiederholt komplett zusammengesetzt, und erfolglose Bibliothekszuordnungen können erneute Library-Abfragen auslösen. | Mit großen Reports messen; gezielt inkrementelle Zähler, Logpuffer und begrenzte Lookup-Caches einsetzen. | Mittel |
| O12 | P3 | Der Download-Standardpfad basiert auf `USERPROFILE/Downloads`; ein in Windows umgeleiteter Downloads-Ordner wird nicht korrekt aufgelöst. | Windows-Known-Folder-Auflösung mit bisherigem Pfad als Fallback. | Klein |
| O13 | P3, Prüfverdacht | Lange Status-/Fehlertexte können in den TVDB-/IMDb-Dialog-Fußbereichen und im festen Startfenster knapp werden. Ein konkreter Fehler bei allen relevanten DPI-/Mindestgrößen ist noch nicht visuell nachgewiesen. | Kleine Fenster, hohe Skalierung und lange Meldungen reproduzierbar prüfen; danach gezielt Layout korrigieren. | Klein bis mittel |

Quellen: [Archiv/Einsortieren](archive-sort.md), [Emby](emby.md),
[Metadaten](metadata.md), [Werkzeuge](tooling.md), [Mux-Oberfläche](mux-ui.md),
[Infrastruktur](infrastructure.md).

## Weitere Entscheidbare Hinweise

Diese Punkte sind Erweiterungen, Sonderfallabsicherungen oder bewusst verbliebene
Grenzen. Sie haben nicht dieselbe Dringlichkeit wie ein nachgewiesener Alltagsfehler.

| Nr. | Bereich | Was noch offen ist und welche Entscheidung nötig wäre |
| --- | --- | --- |
| O14 | Abbruch/Timeouts | `IMediaDurationProbe` hat keinen CancellationToken; synchrone ffprobe-/Windows-COM-Abfragen können nach einem UI-Abbruch weiterlaufen. Identify hat ohne Aufruferabbruch keinen eigenen Timeout. Cancellation durch die Probe-API reichen und Prozessbudgets festlegen; native COM-/Dateisystemwartezeiten bleiben ein gesondertes Problem. |
| O15 | Emby-Abbruch | Reportimport, Providerprüfung und NFO-Schreiben haben keinen eigenen durchgängigen Abbruchknopf. Nur das Scan-Warten ist abbrechbar. Ein Ausbau müsste Teilergebnisse und Reportfortschritt sauber erhalten. |
| O16 | Emby-Pfadzuordnung | Windows-/Serverpfade werden heuristisch zugeordnet. Ein einzelner falscher Suffix-Treffer ist nicht vollständig ausgeschlossen. Bei mehrdeutiger Bibliothek gibt es weiterhin einen sichtbar angekündigten globalen Scan-Fallback. Explizite Pfad-/Bibliothekszuordnung und kein globaler Scan ohne Zustimmung wären eine mögliche Änderung. |
| O17 | Laufendes MediathekView | Während der Migration kann eine noch laufende alte MediathekView-Instanz ihre Einstellungen weiter verändern. Ihre Installation bleibt erhalten, aber die bereits kopierten Einstellungen sind dann möglicherweise veraltet. Vor Migration auf Schließen bestehen oder einen abgestimmten Snapshot vorsehen. |
| O18 | Wiederherstellung/Bereinigung | Alte MediathekView-Versionen und `.replaced-*`-Kopien bleiben zum Schutz von Benutzerdaten liegen. Ein harter Absturz kann außerdem `.mux-*`-/Index-Staging-Reste hinterlassen. Es gibt keine gemeinsame UI für sichere Wiederherstellung/Bereinigung; automatisches pauschales Löschen wäre falsch. |
| O19 | Tool-Fallback | Die automatische Rückfallsuche ordnet Installationen nach Verzeichniszeitstempel statt semantischer Version. Historische Download-Overrides und ausdrückliche Benutzerwahl sind teilweise nicht unterscheidbar. Herkunft/Version expliziter speichern statt aus Pfaden/Zeitstempeln ableiten. |
| O20 | Gemeinsame Batchquellen | Dateien, die mehrere Pläne nutzen, bleiben absichtlich am Quellort, auch wenn am Ende alle Verbraucher erfolgreich waren. Optional könnte ein abschließender, batchweiter Cleanup diese Fälle sicher erfassen. |
| O21 | Mehrere App-Instanzen | GUI-Sperren und Installer-/Index-Sperren schützen nicht alle Operationen zwischen mehreren gestarteten App-Prozessen. Entscheiden, ob eine zweite Instanz generell verhindert oder pro Ressource koordiniert werden soll. Das ersetzt keinen Schutz vor fremden Emby-Schreibzugriffen aus O01. |
| O22 | IMDb-Index-Robustheit | `File.Exists` ist noch die Verfügbarkeitsprüfung, kein Integritätstest. Ein teilweise unvollständiger, aber formal nutzbarer Import kann die Mindestprüfung bestehen. Datenbankaktivierung und Settings sind nicht gemeinsam atomar; nach erfolgreicher Aktivierung und fehlgeschlagenem Settings-Speichern kann erneut ein Update angeboten werden. Diese drei Fälle ließen sich als getrennte kleine Aufgaben mit Quick-Check, Plausibilitätsvergleich und Statusabgleich behandeln. |
| O23 | Medien-Probe-Caches | Cacheidentität basiert auf Größe/Zeitstempel, nicht Inhalt. Ein gleich großer Austausch mit unverändertem Datum oder eine noch laufende Probe nach Invalidierung kann Sonderfälle erzeugen. Vorbereitete Ordnerkontexte sind Snapshots, keine Live-Watcher. Generationen/Invalidierung gezielt härten; kein zwingender Vollhash aller Videos. |
| O24 | Entpack-Grenzen | Toolarchive haben noch kein Limit für die gesamte entpackte Größe. Schutz vor ungewöhnlich großen/kompromittierten Archiven und vor Speicherplatzmangel könnte ergänzt werden. Kein Versprechen gegen einen lokalen Angreifer, der Pfade während des Entpackens austauscht. |
| O25 | Doppelfolgen-Metadaten | Eine `E01-E02`-Range wird jetzt erhalten. Eine einzelne TVDB-ID in der NFO beschreibt aber nicht immer Titel und Staffel der gesamten Doppelfolge. Die automatische Titel-/Staffelentscheidung benötigt dafür noch eine bewusst festgelegte Regel bzw. einen Reviewhinweis. |
| O26 | Grid-Refresh | Eine offene Edit-Transaktion kann im Batch-CollectionController wiederholte `ContextIdle`-Versuche auslösen. Das aktuelle Batchgrid ist read-only; daher kein nachgewiesener normaler Bedienfehler. Bei einer Bereinigung besser auf ein tatsächliches Edit-Ende reagieren als ungebremst weiterzuplanen. |
| O27 | Erkennungsregeln | Die deutschen Sprach-/HI-Fallbacks, H.264-Präferenz, AD-only-Fallbacks und heuristische Quellengruppierung bleiben bestehen. Qualitäts-/Laufzeitvergleich beweist keine identische Schnittfassung oder Synchronität. Änderung nur mit konkreten gewünschten Regeln/Beispielen; kein genereller Funktionsfehler allein durch diese Grenzen. |
| O28 | Format-/Interaktionsgrenzen | Echte NFO-Änderungen erhalten XML-Inhalte semantisch, nicht ursprüngliche Bytes/BOM/Formatierung; fremde Namespaces/DTD-/Mehrfachwurzel-NFOs werden abgewiesen. IMDb-Browserrückkehr erlaubt bewusst nur eine einmalige Übernahme neuer Zwischenablagewerte. Shared TVDB-Requests können nach Dialogschließen für andere Nutzer weiterlaufen; Cachelimits sind keine harte RAM-/Requestgrenze. Nur erweitern, wenn diese konkreten Verhaltensweisen stören. |

Quellen ergänzend: [Mux-Kern](mux-core.md) und die oben verlinkten Bereichsberichte.

## Verbleibende Testlücken

- **T01: Praxisabnahme.** Echter Einzel-/Batch-Mux, Header-Edit und Archivumbenennung mit
  repräsentativen Medien sowie Emby-Scan/-Refresh/-Live-Watching wurden in diesem Review
  nicht auf dem Benutzerarchiv ausgeführt. Neue automatisierte Tests verwenden Fakes/Tempdaten.
- **T02: Performance.** Kein vollständiger aktueller IMDb-Import-/Peak-RAM-Benchmark und
  kein Lasttest mit sehr großen Archiven, Reports oder langsamem SMB. Die Unit-Tests ersetzen
  weder eine Messung der Importgeschwindigkeit noch eine Lastmessung der UI.
- **T03: Plattform-/Ausfallfälle.** Dateisperren, Junctions/Hardlinks, wenig Speicherplatz,
  Share-Abbruch, Stromausfall/Prozesskill und mehrere App-Instanzen sind nicht systematisch
  Ende-zu-Ende getestet. Einzelne kontrollierte Fehlerfälle sind bereits automatisiert abgedeckt.
- **T04: UI.** Hohe DPI, Mindestfenstergrößen, Screenreader, echte Zwischenablage und alle
  Tastatur-/Layoutzustände benötigen noch eine gezielte Abnahme. Bestehende WPF-Tests und
  visuell geprüfte README-Screenshots bleiben davon unberührt.
- **T05: Gezielt fehlende Regressionen.** Insbesondere NFO-/TVDB-Doppelfolgenauflösung,
  verschachtelte reale alternative Quelldetektion, parallele TVDB-Factory-Races und einzelne
  Settings-/Aktivierungsrennen sind noch nicht jeweils durch eigene deterministische Tests
  abgedeckt. Die Bereichsberichte nennen weitere ausschließlich statisch geprüfte Kleinzweige.
- **T06: Remote-CI.** Der Push ist erfolgt. Die abschließenden GitHub-Workflow-Ergebnisse
  nach diesem Push wurden für diese Restliste noch nicht geprüft. Lokal sind 1167 Unit-/WPF-
  und 133 Integrationstests sowie Build, DocFX und Screenshot-Generator grün.

## Nicht Mehr Offen

- Globale NFO-Sperren über `lockdata=true`: in E23 behoben, im Gesamtlauf getestet.
- Einzel-/Batch-Konkurrenz, Modul-/Settingswechsel während aktiver Vorgänge: für den normalen
  UI-Ablauf behoben. Nur die ausdrücklich separat genannte Mehrprozessgrenze bleibt.
- Batch-Abbruch ohne Teilreports, fehlende Planfehler in der Statistik und irreführender
  Bibliotheksbegriff bei Custom-Zielen: im Mux-UI-Nachtrag behoben.
- Wiederholte identische Emby-Refresh-Anforderungen und falsche Asset-Abschlusszähler:
  E21/E22 behoben. Die älteren Vermerke zu ausstehenden zentralen Tests sind überholt.
- Rohe Tracksprachen, verschwiegene entfernte Normal-Audios und unvollständige Header-Vorschau:
  MC-O1/O2/O3 behoben.
- Direkte finale Mux-Ausgabe, ungefangene Prozess-Callbacks, Root-JSON-null und Settings-
  Benachrichtigungen nach bereits gespeichertem Dialogabbruch: zentral behoben.

## Vorschlag Zur Auswahl

Für den normalen Einsatz zuerst **O01, O04, O06, O09, O10, O12 und O16** betrachten.
O01/O16 sind gerade wegen des laufend überwachenden Emby relevant. O03/O14/O15 sind
sinnvolle weitere Robustheitsarbeiten. O02/O05 sind größere Architektur-/Sonderfallthemen
und sollten nicht unbemerkt in einen vermeintlichen Kleinfix hineinwachsen.
O13 sowie reine Performancevermutungen zuerst reproduzieren bzw. messen.
