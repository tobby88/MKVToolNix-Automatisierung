# MKVToolNix-Automatisierung

[![CI and Docs](https://github.com/tobby88/MKVToolNix-Automatisierung/actions/workflows/ci-docs.yml/badge.svg)](https://github.com/tobby88/MKVToolNix-Automatisierung/actions/workflows/ci-docs.yml)
[![Nightly EXE](https://img.shields.io/badge/nightly-win--x64%20exe-1f6feb)](https://github.com/tobby88/MKVToolNix-Automatisierung/releases/download/nightly/MkvToolnixAutomatisierung-nightly-win-x64.exe)
[![Latest release](https://img.shields.io/github/v/release/tobby88/MKVToolNix-Automatisierung)](https://github.com/tobby88/MKVToolNix-Automatisierung/releases/latest)
[![License](https://img.shields.io/badge/license-CC%20BY--NC--SA%204.0-lightgrey.svg)](LICENSE.md)

## Wichtiger Hinweis

Dieses Projekt wurde vollständig KI-gestützt erstellt und weiterentwickelt.  
Verantwortlich für Konzeption, Code-Erstellung, Überarbeitungen und große Teile der Dokumentation ist die KI, nicht ein klassisch manuell entwickeltes Teamprojekt.

## Worum es geht

Diese Anwendung automatisiert wiederkehrende Muxing-Abläufe für Serienepisoden aus Mediathek-Downloads.  
Sie ist dafür gedacht, frische Download-Dateien nicht jedes Mal manuell in MKVToolNix zusammenzuklicken, sondern die fachlichen Entscheidungen möglichst weit vorab zu treffen und dann reproduzierbar auszuführen.

Dabei geht es nicht nur um ein simples "Datei A plus Untertitel B muxen", sondern um den typischen Serien-Alltag:

- eine einzelne Episode schnell prüfen und muxen
- einen ganzen Download-Ordner gesammelt verarbeiten
- vorhandene Dateien in der Serienbibliothek erkennen und sinnvoll weiterverwenden
- neue, bessere oder zusätzliche Spuren ergänzen, ohne gute vorhandene Inhalte blind wegzuwerfen
- Audiodeskription, Untertitel und TXT-Begleitdateien konsistent mitziehen
- Tracknamen vereinheitlichen, damit die Bibliothek über längere Zeit sauber bleibt

Die App ist bewusst auf einen konkreten persönlichen Workflow zugeschnitten. Sie will nicht jede denkbare MKV-Konstellation generisch erschlagen, sondern Serienepisoden aus deutsch geprägten Mediathek-Quellen zuverlässig und mit möglichst wenig manuellem Nacharbeiten verarbeiten.

## Module

- `Download`: zum Starten der installierten oder portablen MediathekView-Variante als erstem Workflow-Schritt
- `Einsortieren`: für lose MediathekView-Dateien, die anhand erkannter Serienordner in Unterordner verschoben werden sollen
- `Muxen`: gemeinsamer Arbeitsbereich für Einzel- und Batch-Mux mit derselben Erkennungs-, Planungs- und Archivvergleichslogik
- `Emby-Abgleich`: für neu erzeugte MKV-Dateien, deren NFO-Provider-IDs mit Emby abgeglichen werden sollen
- `Archivpflege`: für bestehende Archiv-MKVs, deren Header oder Dateinamen nachträglich vereinheitlicht werden sollen

## Screenshots

### Download

![Download](docs/images/readme/download.png)

### Einsortieren

![Einsortieren](docs/images/readme/download-sort.png)

### Muxen: Einzel-Mux

![Muxen: Einzel-Mux](docs/images/readme/mux-single.png)

### Muxen: Batch-Mux

![Muxen: Batch-Mux](docs/images/readme/mux-batch.png)

### Emby-Abgleich

![Emby-Abgleich](docs/images/readme/emby-sync.png)

### Archivpflege

![Archivpflege](docs/images/readme/archive-maintenance.png)

## Voraussetzungen

- Die veröffentlichte `.exe` benötigt die `.NET 10 Desktop Runtime`; für Builds aus dem Quellcode wird das `.NET 10 SDK` benötigt.
- MediathekView bleibt das externe Download-Werkzeug. Die App kann eine installierte Version oder eine portable Variante im Downloadordner starten; optional kann sie die portable Windows-ZIP-Version auch selbst unter `.\Tools` herunterladen und aktuell halten.
- MKVToolNix und `ffprobe.exe` werden beim Start automatisch unter `.\Tools` bereitgestellt und aktualisiert, solange kein manueller Override in den Einstellungen gesetzt ist.
- Wenn `ffprobe` nicht bereitgestellt werden kann, nutzt die App für Laufzeiten den Windows-Fallback.
- Ein TVDB-API-Key ist optional. Er wird nur benötigt, wenn Serien- und Episodendaten über TVDB geprüft oder verbessert werden sollen.
- Ein Emby-API-Key ist optional. Er wird nur für den nachgelagerten `Emby-Abgleich` benötigt.

## Portable Modus

Die App ist bewusst portabel gedacht und nicht für eine klassische Installation vorgesehen.

- Es gibt keinen Installer.
- Einstellungen werden lokal unter `.\Data\settings.json` neben der Anwendung gespeichert.
- Verwendete Unterordner für portable Laufzeitdaten sind `.\Data`, `.\Logs` und `.\Tools`; `.\Logs` enthält Mux-Artefakte und die gespeicherten Modulprotokolle.
- Bei Single-File-Releases legt die App eine fehlende `README.md` beim Start neben der `.exe` an.
- Der Anwendungsordner muss beschreibbar sein.
- Die App sollte deshalb nicht aus `C:\Program Files` gestartet werden.

## Erststart

1. App starten.
2. Über `Einstellungen` die selten geänderten Dinge zentral hinterlegen:
   - Standard-Archivpfad
   - optional MediathekView-Pfad oder automatische MediathekView-Verwaltung
   - bei Bedarf manuelle Overrides für MKVToolNix oder `ffprobe`
   - optional TVDB-API-Key und PIN
   - optional IMDb-Abgleichmodus für den Emby-Dialog
   - optional Emby-Server und API-Key
3. Im Hauptfenster darunter kurz prüfen, ob `Archiv`, `MKVToolNix` und die Laufzeitermittlung als bereit angezeigt werden.
4. Danach dem Workflow von oben nach unten folgen: `Download`, `Einsortieren`, `Muxen`, `Emby-Abgleich` und optional `Archivpflege`.

## Typischer Workflow: Download

1. Im Modul `Download` `MediathekView starten` ausführen.
2. Falls die App nicht gefunden wird, in `Einstellungen` den Pfad zur installierten oder portablen `MediathekView.exe` bzw. `MediathekView_Portable.exe` setzen oder die automatische portable MediathekView-Verwaltung aktivieren.
3. Sendungen wie gewohnt in MediathekView herunterladen.
4. Danach im Modul `Einsortieren` mit den erzeugten Download-Dateien weiterarbeiten.

## Typischer Workflow: Muxen

Das Modul `Muxen` bündelt zwei Arbeitsweisen, die fachlich möglichst gleich laufen sollen:

- `Einzel-Mux` ist für eine gezielt ausgewählte Episode gedacht, wenn man bewusst Datei für Datei prüfen oder nacharbeiten möchte.
- `Batch-Mux` verarbeitet einen ganzen Quellordner, zeigt alle erkannten Episoden tabellarisch an und arbeitet danach die ausgewählten Einträge nacheinander ab.

Beide Tabs verwenden dieselbe zentrale Mux-Planung. Das betrifft insbesondere lokale Dateierkennung, TVDB-Abgleich, Archivtreffer, Spurenauswahl, AD-/Untertitel-Logik, TXT-Anhänge, Header-Normalisierung und die Ausgabe der Emby-Metadatenreports. Unterschiede sollen nur dort bestehen, wo sie durch die Bedienung nötig sind: Einzel-Mux arbeitet direkt an einer Episode, Batch-Mux verwaltet mehrere Einträge mit Auswahl, Sortierung und Sammelaktionen.

Die Vorschau zeigt nicht nur den `mkvmerge`-Aufruf, sondern fasst auch zusammen, was mit vorhandenen Archivspuren, neuen Quellen und direkten Header-Anpassungen passieren soll. Wenn eine bestehende Archiv-MKV bereits alle benötigten Inhalte enthält, kann die App statt eines kompletten Remux auch nur relevante Matroska-Headerdaten direkt aktualisieren.

### Einzel-Mux-Tab

1. `Hauptvideo wählen`.
2. Automatische Erkennung für Quelle, Begleitdateien und Metadaten prüfen.
3. Falls angezeigt, `Quelle prüfen / freigeben` und/oder `TVDB prüfen`.
4. Bei Bedarf im Bereich `Korrekturen und Ausgabe` manuell nachbessern, etwa Sprache, Originalsprache, AD, Untertitel, Anhänge oder Ausgabepfad.
5. Mit den `Öffnen`-Aktionen bei Bedarf Hauptvideo, AD, Untertitel, Anhänge oder vorhandene Archivdateien vorab in der Standardanwendung prüfen.
6. `Vorschau erzeugen`, um den geplanten Mux- oder Header-Edit-Vorgang zu kontrollieren.
7. `Muxen`, um die MKV zu erstellen oder die vorhandene MKV direkt zu aktualisieren.

### Batch-Mux-Tab

1. Quellordner wählen.
2. Scan abwarten und gefundene Episoden prüfen.
3. Bei Bedarf Einträge auswählen, abwählen, sortieren oder im Detailbereich korrigieren.
4. Offene Pflichtprüfungen mit `Pflichtchecks starten` oder einzeln im Detailbereich erledigen.
5. Mit den `Öffnen`-Aktionen bei Bedarf alle zugehörigen Videos, AD-Dateien, Untertitel, Anhänge oder Archivdateien eines Eintrags prüfen.
6. `Batch starten`.
7. Danach Protokoll, neue Bibliotheksdateien und den optionalen `done`-Ordner prüfen.

Nach jedem Mux-Lauf:

- bleibt das Protokoll in der GUI sichtbar
- wird das vollständige Protokoll zusätzlich unter `.\Logs` gespeichert
- wird eine TXT-Liste neu erzeugter Ausgabedateien gespeichert, damit sie anschließend schnell geprüft werden können
- wird zusätzlich ein strukturierter JSON-Metadatenreport `Neu erzeugte Ausgabedateien - ...metadata.json` geschrieben, den das Tool für den Emby-Abgleich importieren kann
- öffnet die App den Report mit neu erzeugten Dateien automatisch, wenn neue Ausgabedateien entstanden sind
- räumt die App erfolgreich verarbeitete Quelldateien auf und entfernt im Einzel-Mux auch leere Quellordner

Zusätzlich beim Batch-Lauf:

- bleibt das Batch-Protokoll im Batch-Tab sichtbar
- können fertig verarbeitete Quellen in einen `done`-Ordner verschoben werden

## Typischer Workflow: Archivpflege

Die `Archivpflege` ist der nachgelagerte Kontrollschritt für bereits vorhandene MKV-Dateien im Serienarchiv. Sie verwendet dieselben Header-Regeln wie der Mux-Archivvergleich, führt aber keinen automatischen Voll-Remux aus.

1. Serienarchiv oder einen Serienunterordner wählen; danach startet der Scan automatisch.
2. `Scannen` wiederholt die rekursive Prüfung aller `.mkv`-Dateien bei Bedarf.
3. In der Tabelle kontrollieren, ob ein Eintrag nur direkte Header-/Dateinamenänderungen braucht oder ob ein manueller Remux-Hinweis vorliegt.
4. Im Detailbereich die konkreten Änderungen prüfen und bei Bedarf den Bereich `Manuelle Korrektur` aufklappen, um Ziel-Dateiname, MKV-Titel oder einzelne Track-Zielwerte anzupassen.
5. Nur freigegebene Zeilen auswählen und `Ausgewählte Änderungen anwenden`.

Direkt schreibbar sind derzeit MKV-Titel, Tracknamen, Sprachwerte, Standard-/Forced-/Original-/Accessibility-Flags sowie sichere Dateinamen-Normalisierungen inklusive gleichnamiger Emby-Begleitdateien. Automatisch erkannte Sollwerte sind vor dem Schreiben manuell überschreibbar; die App schreibt weiterhin nur ausgewählte Zeilen. Wenn eine gleichnamige `.nfo` eine TVDB-Episoden-ID enthält und für die Serie bereits ein TVDB-Mapping gespeichert ist, wird der TVDB-Titel als Sollwert genutzt; sonst bleibt der lokale Dateiname der Fallback. Fehlende AD- oder Untertitelspuren werden bewusst nicht als Problem gemeldet: das Archivpflege-Modul bewertet den vorhandenen Bestand und fordert keine Inhalte an, die nie gemuxt wurden. Doppelte AD-Spuren oder doppelte Untertitel-Slots werden dagegen als Remux-Hinweis markiert, weil diese Fälle nicht sauber per Header-Edit aufzulösen sind.

## Typischer Workflow: Einsortieren

1. MediathekView-Downloadordner wählen oder den vorgeschlagenen Standardordner nutzen.
2. `Neu scannen`, um lose Dateien in der Wurzel zu gruppieren.
3. Zielordner und Hinweise prüfen.
4. Bei Bedarf Zielordner manuell korrigieren oder einzelne Einträge abwählen.
5. `Auswahl einsortieren`, um die Dateien in die Serienunterordner zu verschieben.

## Typischer Workflow: Emby-Abgleich

1. Emby-Zugangsdaten zentral über `Einstellungen` hinterlegen.
2. Einen oder mehrere nach einem Batch- oder Einzel-Lauf erzeugte Metadatenreports `Neu erzeugte Ausgabedateien - ...metadata.json` über `Reports wählen` laden.
3. Nach `Reports wählen` prüft das Tool automatisch lokale `.nfo`-Dateien und, falls konfiguriert, auch bereits sichtbare Emby-Einträge.
4. Wenn Emby neue Dateien noch nicht kennt, `Emby scannen` ausführen und den Serverfortschritt abwarten. Der Scan wird bevorzugt auf die zur Archivwurzel passende Serienbibliothek begrenzt. Falls Emby die Bibliothek nicht eindeutig zuordnen kann, zeigt die App den globalen Fallback ausdrücklich an, statt ihn als bibliotheksscharfen Scan aussehen zu lassen. Danach prüft das Tool die betroffenen Einträge erneut automatisch.
5. Offene Provider-ID-Prüfungen mit `Pflichtchecks starten` abarbeiten. TVDB wird nur bei widersprüchlichen Quellen aktiv geprüft; IMDb wird für jede NFO-fähige Episode bewusst bestätigt, korrigiert oder als nicht vorhanden markiert.
6. Einzelne Zeilen können weiterhin direkt über die `TVDB`- und `IMDb`-Buttons nachbearbeitet werden. Die ID-Zellen sind zusätzlich editierbar, wenn eine ID direkt bekannt ist.
7. `NFO speichern + Emby aktualisieren`, um geänderte TVDB-/IMDb-IDs in die `.nfo` zurückzuschreiben und nur betroffene Emby-Einträge gezielt zu refreshen.

Die erste Emby-Ausbaustufe erzeugt bewusst keine neue NFO aus dem Nichts. Emby soll die Episoden-NFO zunächst selbst anlegen; das Tool ergänzt danach nur die Provider-IDs. Wenn Emby temporär nicht erreichbar ist oder eine Datei noch nicht als Item liefert, prüft die App vorhandene lokale `.nfo`-Dateien trotzdem weiter, damit ein Serverproblem nicht jede lokale Kontrolle blockiert. Dateien in Emby-Asset-Ordnern wie `trailers` oder `backdrops` bekommen normalerweise keine Episoden-NFO; solche Einträge werden erkannt und beim Provider-ID-Sync übersprungen.

Nach einem erfolgreichen Emby-Abgleich markiert die App erledigte Reporteinträge in der JSON. Sobald alle relevanten Einträge eines Reports abgearbeitet sind, wird der Report in einen `done`-Unterordner verschoben.

Für IMDb nutzt der Dialog je nach Einstellung bevorzugt `imdbapi.dev`, ausschließlich `imdbapi.dev` oder ausschließlich die Browserhilfe. Im Automatikmodus fällt der Dialog nur dann auf die Browserhilfe zurück, wenn der freie API-Dienst insgesamt nicht erreichbar ist. Die Entscheidung `Keine IMDb-ID` wird auch dann in die lokale NFO übernommen, wenn keine weitere Provider-ID vorhanden ist. Netzwerk- oder Dienstfehler bei TVDB und IMDb werden im Dialog als verständliche Statusmeldung angezeigt; Endlos-Pagination oder wiederholte Provider-Tokens werden intern begrenzt.

## Unterstützte Dateien

Im aktuellen Serien-Modul werden verwendet:

- Hauptvideo: `.mp4`
- optionale Audiodeskription: `.mp4`
- optionale Untertitel: `.srt`, `.ass`, `.vtt`
- optionale TXT-Begleitdatei: `.txt`
- vorhandene Archivdateien zum Vergleich und zur Wiederverwendung: `.mkv`
- vorhandene Archivdateien zur nachgelagerten Pflege: `.mkv` plus gleichnamige `.nfo`-/Bild-Begleitdateien bei sicheren Umbenennungen

`.ttml` wird nicht gemuxt, aber als Begleitdatei für Cleanup und Aufräumen berücksichtigt.

## Fachliche Regeln

Dieser Abschnitt beschreibt bewusst die wichtigsten fachlichen Entscheidungen der App. Er ist nicht als exakte technische Spezifikation gedacht, sondern als gut lesbare Zusammenfassung dessen, was das Tool normalerweise tut und warum.

### Videoauswahl

- Es werden nur Quellen derselben Episode gemeinsam betrachtet.
- Bei unterschiedlichen Laufzeiten bleibt nur die fachlich passende Laufzeitgruppe übrig. Kleinere Abweichungen werden toleriert, klar unpassende Dateien fliegen heraus.
- Frische Videospuren werden pro Sprach-/Codec-Slot ausgewählt. Das bedeutet: Für `Deutsch + H.264`, `Deutsch + H.265`, `Plattdeutsch + H.264` oder `English + H.264` bleibt jeweils nur die beste Quelle übrig.
- Innerhalb eines Slots gewinnt zuerst die höhere Auflösung, dann die größere Datei und danach die Sender-Priorität.
- Die Ausgabereihenfolge der Videospuren ist sprachlich bewusst fest: `Deutsch`, `Plattdüütsch`, `English`.
- Innerhalb derselben Sprache steht `H.264` vor `H.265`.
- Wenn zu einer Sprache sowohl `H.264` als auch `H.265` vorhanden sind, können beide erhalten bleiben. `H.265` ersetzt also nicht pauschal `H.264`.
- Im Archivabgleich kann eine vorhandene Videospur desselben Slots durch eine neue ersetzt werden, wenn die neue fachlich besser ist, insbesondere bei höherer Auflösung.

### Archivabgleich und Sonderfolgen

- Wenn das geplante Ziel bereits im Archiv existiert, liest die App die vorhandene MKV ein und entscheidet, ob Inhalte wiederverwendet, ersetzt, ergänzt oder nur Headerdaten korrigiert werden müssen.
- Vorhandene Archivspuren werden nicht blind verworfen. Besonders normale Audiospuren und bereits vorhandene AD-/Untertitelspuren werden weiterverwendet, wenn sie den fachlichen Slot bereits abdecken.
- Bei nicht eindeutig TVDB-zuordenbaren Sonder- oder Bonusfolgen sucht die App zusätzlich in typischen Sonderordnern der Serie, etwa `Specials`, `Season 0`, `Trailers` und `Backdrops`.
- Wenn dort eine passende Archivdatei gefunden wird, kann sie als Ziel und Metadatenquelle dienen. Das spart manuelle Nacharbeit bei Bonusmaterial ohne sauberen TVDB- oder IMDb-Eintrag.
- Hinweise wie Mehrfachfolge, Archivtreffer oder ungewöhnliche Quellen müssen vor dem Muxen bewusst geprüft werden, wenn sie als Pflichtprüfung angezeigt werden.

### Audio und Audiodeskription

- Normale Audiospuren aus frischen Quellen bleiben erhalten und werden nicht mehr auf die erste Tonspur reduziert.
- Audiodeskriptionsspuren werden getrennt behandelt und sollen nicht als normale Tonspur im Set landen.
- Als AD gelten Spuren mit passendem Accessibility-Flag oder mit klaren Hinweisen wie `sehbehinder...` oder `audiodeskrip...` im Namen.
- Falls die Heuristik bei einer frischen Quelldatei ausnahmsweise jede Audiospur als AD einordnen würde, bleibt die Auswahl konservativ und lässt die Audiospur lieber stehen, statt die Quelle stumm zu planen.
- Beim Ersetzen einer vorhandenen Archiv-Hauptquelle bleiben vorhandene normale Archiv-Audiospuren für Sprachen erhalten, die in den frischen ausgewählten Quellen nicht mehr abgedeckt sind.
- Eine separate AD-Datei wird weiterhin als eigener Sonderfall behandelt.

### Untertitel

- Unterstützt werden externe `.ass`, `.srt` und `.vtt`.
- Externe Untertitel werden derzeit konservativ als `hörgeschädigte` behandelt, solange nichts Sicheres erkannt wird.
- Bereits eingebettete Untertitel aus der Zieldatei werden weiterverwendet, wenn sie denselben fachlichen Slot bereits belegen.
- Für die Wiederverwendung zählt dabei bewusst nur `Typ + Sprache`, nicht jede Feinheit der Accessibility-Markierung.
- Externe Untertitel werden nur dann zusätzlich aufgenommen, wenn dieser Slot in der Zieldatei noch nicht vorhanden ist.
- Nicht unterstützte Untertitelcodecs werden nicht stillschweigend als vollwertig weitergemuxte Standard-Untertitel behandelt.

### TXT-Begleitdateien und eingebettete TXT-Anhänge

- Zu jeder tatsächlich verwendeten frischen Videodatei wird die passende benachbarte `.txt` mitgenommen.
- Ungenutzte frische Hauptquellen ziehen ihre TXT nicht mehr versehentlich mit.
- Manuell ausgewählte TXT-Anhänge bleiben davon unabhängig erhalten.
- Bereits in der Ziel-MKV eingebettete TXT-Anhänge werden konservativ behandelt und möglichst nicht unnötig verworfen.
- Für eingebettete TXT-Anhänge nutzt die App eine Heuristik aus Dateiname und Inhalt, insbesondere aus `Titel` und `URL`.
- Daraus können Sprache, Auflösung und teils auch Codec abgeleitet werden, zum Beispiel `Plattdüütsch`, `FHD`, `HD`, `H.264` oder `H.265`.
- Ein eingebetteter TXT-Anhang wird nur dann automatisch entfernt, wenn seine Zuordnung zu einer ersetzten alten Videospur wirklich eindeutig ist.
- Wenn die Zuordnung nicht sicher ist, bleibt der TXT-Anhang erhalten.
- Zusätzlich bleibt der alte explizit sichere Fallback aktiv: `genau eine vorhandene Videospur + genau eine TXT`, wenn diese Videospur ersetzt wird.

### Direkte Header-Anpassungen

- Wenn am Ziel bereits alle benötigten Inhalte vorhanden sind, kann die App statt eines Remux nur die Matroska-Header aktualisieren.
- Verglichen und bei Bedarf angepasst werden Tracknamen, Sprachen, Standard-Flags, Originalsprache, Forced-Flags, Accessibility-Flags und der MKV-Titel.
- Die Vorschau zeigt nur relevante Änderungen an, damit sichtbar bleibt, was vorher falsch war und was geändert wird.
- Normale Hauptspuren, also nicht AD und nicht hörgeschädigte Untertitel, sollen als Standard geeignet markiert sein. Spezialspuren werden bewusst getrennt behandelt.
- Die Archivpflege nutzt dieselbe Header-Regelbasis nachträglich für vorhandene Archivdateien. Sie ergänzt keine fehlenden Spuren, meldet aber doppelte AD- oder Untertitel-Slots als Remux-Fall.

### Sender-Priorität und manuelle Prüfung

- Die Sender-Priorität ist nur ein Tie-Breaker, nicht das Hauptkriterium.
- Bevorzugt werden aktuell vor allem `ZDF`, danach `ARD` / `Das Erste`, dann `RBB` und `Arte`.
- `SRF` wird nicht pauschal verworfen, aber bewusst zurückhaltender behandelt und in der Regel zur manuellen Prüfung markiert.

### Tracknamen

Die App setzt Tracknamen bewusst einheitlich, damit die Bibliothek langfristig lesbar bleibt.

Typische Formate sind:

- Video: `Deutsch - FHD - H.264`
- Audio: `Deutsch - AAC`
- Audiodeskription: `Deutsch (sehbehinderte) - AAC`
- Untertitel: `Deutsch (hörgeschädigte) - SRT`

Sprachbezeichnungen werden in ihrer eigenen Sprache geschrieben:

- `Deutsch`
- `Plattdüütsch`
- `English`

## Hinweise für die Nutzung

- MKVToolNix und `ffprobe` werden standardmäßig automatisch im portablen `.\Tools`-Ordner verwaltet; MediathekView kann dort optional ebenfalls automatisch verwaltet werden.
- Manuelle Toolpfad-Overrides in den Einstellungen haben Vorrang vor den automatisch verwalteten Tools.
- Der Startordner für Videoquellen bevorzugt `Downloads\MediathekView\Downloads`, fällt aber automatisch auf `Dokumente` zurück, wenn der Ordner nicht existiert.
- Die Standard-Serienbibliothek, Toolpfade und API-Schlüssel werden zentral im Einstellungsdialog gepflegt und lokal in `.\Data\settings.json` gespeichert.
- Portable Daten und Logs bleiben im Anwendungsordner.

## Starten

```powershell
dotnet build
dotnet run
```

im Projektordner:

`<dein-projektordner>\mkvtoolnix-Automatisierung`

## Entwicklerdokumentation

Das Projekt ist zusätzlich mit XML-Dokumentationskommentaren und einer DocFX-Konfiguration versehen.

Lokal erzeugen:

```powershell
dotnet tool restore
.\scripts\build-docs.ps1
```

Lokale Vorschau im Browser:

```powershell
.\scripts\build-docs.ps1 -Serve
```

Das Skript bereinigt vorher alte generierte Artefakte unter `.\docs\api` und `.\docs\_site`, damit lokal keine veralteten DocFX-Seiten liegen bleiben.  
Die erzeugte Seite landet unter `.\docs\_site`.  
Auf GitHub ist außerdem ein Workflow unter `.github/workflows/ci-docs.yml` vorbereitet, der Build, Unit-Tests, Integrationstests und den DocFX-Site-Build automatisiert ausführt und die Dokumentation bei Pushes auf `master` optional nach GitHub Pages deployen kann.

Zusätzlich hält `.github/dependabot.yml` Versionsupdates für GitHub Actions und NuGet-Pakete automatisch im Blick.

README-Screenshots neu erzeugen:

```powershell
.\scripts\generate-readme-screenshots.ps1
```

Die PNGs landen danach unter `.\docs\images\readme\`.
Der CI-Workflow rendert die Screenshots zusätzlich als Smoke-Test, damit der Generator nicht unbemerkt bricht. Da Windows-Runner und lokaler Desktop PNGs leicht unterschiedlich rendern können, blockiert die normale CI aber nicht auf Bild-Diffs. Für gelegentliche automatische Aktualisierungen gibt es stattdessen `.github/workflows/readme-screenshots.yml`; dieser Workflow läuft wöchentlich oder manuell und öffnet bei geänderten Bildern einen PR. Falls die Repository-Einstellung GitHub Actions das Erstellen von PRs verbietet, pusht der Workflow den Branch trotzdem und gibt eine Notice mit dem manuellen PR-Link aus.

### Releases

Gelegentliche Releases laufen manuell über `.github/workflows/release.yml`. Der Workflow baut in `Release`, führt Tests seriell aus, erzeugt Release-Notes, setzt danach das Git-Tag und veröffentlicht eine framework-dependent Single-File-Exe für `win-x64` auf GitHub.

Lokal kann derselbe Release-Typ mit `.\scripts\publish-release.ps1 -Version 1.4.0` gebaut werden. Die erzeugte `.exe` liegt danach unter `.\artifacts\release\` und benötigt auf dem Zielsystem die passende `.NET Desktop Runtime 10`; MKVToolNix und `ffprobe` werden beim Start in `.\Tools` verwaltet, MediathekView optional bei aktivierter Einstellung.

Zusätzlich kann `.github/workflows/nightly.yml` einen rollenden Vorabstand `nightly` erzeugen. Der Nightly-Build läuft geplant einmal pro Nacht oder manuell per `workflow_dispatch`, verwendet denselben framework-dependent Single-File-Build wie ein Release und erstellt das GitHub-Prerelease nur dann automatisch neu, wenn seit dem letzten Nightly neue Commits auf `master` dazugekommen sind.

Praktische Links:

- direkte Nightly-Exe: [MkvToolnixAutomatisierung-nightly-win-x64.exe](https://github.com/tobby88/MKVToolNix-Automatisierung/releases/download/nightly/MkvToolnixAutomatisierung-nightly-win-x64.exe)
- Nightly-Prerelease-Seite: [releases/tag/nightly](https://github.com/tobby88/MKVToolNix-Automatisierung/releases/tag/nightly)
- Nightly-Workflow-Historie: [actions/workflows/nightly.yml](https://github.com/tobby88/MKVToolNix-Automatisierung/actions/workflows/nightly.yml)

## Projektaufbau

- `MainWindow.xaml`: Shell mit Modulnavigation und Tool-Status
- `ViewModels/MainWindowViewModel.cs`: Shell-ViewModel
- `Composition/`: Composition-Root und fachlich getrennte DI-Registrierungsmodule
- `Views/`: WPF-Views für die einzelnen Module
- `ViewModels/Modules/`: ViewModels der einzelnen Module
- `Services/`: technische Dienste wie Dialoge, Toolsuche und Prozessausführung
- `Services/Emby/`: Emby-API-Zugriff, NFO-Provider-ID-Abgleich und Emby-Settings
- `Services/AppModuleServices.cs`: kleinere Service-Bundles für Einzelmodus, Batch und Shell statt eines globalen Sammelobjekts
- `Modules/SeriesEpisodeMux/`: Fachlogik für Erkennung, Planung, Archivabgleich und Muxing

Die App verwendet `Microsoft.Extensions.DependencyInjection`, bleibt aber bewusst bei einem klaren Composition Root. `IServiceProvider` wird nicht durch die Fachlogik gereicht; aufgelöst wird nur zentral beim App-Start.

## Weitergabe und Lizenz

Dieses Repository steht unter `CC BY-NC-SA 4.0`, siehe [LICENSE.md](LICENSE.md).

Praktisch bedeutet das:

- Nutzung und Weitergabe sind erlaubt
- kommerzielle Nutzung ist nicht erlaubt
- geänderte und weitergegebene Fassungen müssen wieder unter derselben Lizenz stehen
- der ursprüngliche Autor muss genannt bleiben

Wichtig:

- Creative Commons empfiehlt diese Lizenzfamilie selbst nicht für Software. Sie wurde hier trotzdem bewusst gewählt, weil sie die gewünschten Bedingungen für dieses Repository am besten abbildet.
- Dieses Projekt ist wegen der `NC`-Klausel nicht als klassische Open-Source-Lizenz im OSI-Sinne zu verstehen.
