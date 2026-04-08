# MKVToolNix-Automatisierung

[![CI and Docs](https://github.com/tobby88/MKVToolNix-Automatisierung/workflows/CI%20and%20Docs/badge.svg)](https://github.com/tobby88/MKVToolNix-Automatisierung/actions/workflows/ci-docs.yml)

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

## Die zwei Arbeitsmodi

- `Einzelepisode`: für einen einzelnen Fall mit Vorschau, manueller Korrektur und anschließendem Mux
- `Batch`: für einen kompletten Ordner mit Scan, Pflichtchecks, Ausführung, Cleanup und Protokoll

## Voraussetzungen

- `mkvmerge.exe` aus MKVToolNix ist für das eigentliche Muxing erforderlich.
- `ffprobe.exe` ist optional. Wenn `ffprobe` fehlt, nutzt die App für Laufzeiten den Windows-Fallback.
- Ein TVDB-API-Key ist optional. Er wird nur benötigt, wenn Serien- und Episodendaten über TVDB geprüft oder verbessert werden sollen.

## Portable Modus

Die App ist bewusst portabel gedacht und nicht für eine klassische Installation vorgesehen.

- Es gibt keinen Installer.
- Einstellungen werden lokal unter `.\Data\settings.json` neben der Anwendung gespeichert.
- Verwendete Unterordner für portable Laufzeitdaten sind `.\Data` und `.\Logs`.
- Der Anwendungsordner muss beschreibbar sein.
- Die App sollte deshalb nicht aus `C:\Program Files` gestartet werden.

## Erststart

1. App starten.
2. Links unten prüfen, ob `mkvmerge: bereit` angezeigt wird. Falls nicht, den MKVToolNix-Ordner auswählen.
3. Optional `ffprobe` auswählen, wenn Laufzeiten möglichst zuverlässig über `ffprobe` ermittelt werden sollen.
4. Bei Bedarf links unten die Standard-Serienbibliothek anpassen.
5. Bei Bedarf den TVDB-Dialog öffnen und API-Key sowie optional eine PIN speichern.
6. Danach mit `Einzelepisode` oder `Batch` arbeiten.

## Typischer Workflow: Einzelepisode

1. `Hauptvideo wählen`.
2. Automatische Erkennung für Quelle, Begleitdateien und Metadaten prüfen.
3. Falls angezeigt, `Quelle prüfen / freigeben` und/oder `TVDB prüfen`.
4. Bei Bedarf im Bereich `Korrekturen und Ausgabe` manuell nachbessern.
5. `Vorschau erzeugen`, um den geplanten `mkvmerge`-Aufruf zu kontrollieren.
6. `Muxen`, um die MKV tatsächlich zu erstellen.

## Typischer Workflow: Batch

1. Quellordner wählen.
2. Scan abwarten und gefundene Episoden prüfen.
3. Bei Bedarf Einträge auswählen oder abwählen.
4. Offene Pflichtprüfungen mit `Pflichtchecks starten` oder einzeln im Detailbereich erledigen.
5. `Batch starten`.
6. Danach Protokoll, neue Bibliotheksdateien und den optionalen `done`-Ordner prüfen.

Nach jedem Batch-Lauf:

- bleibt das Batch-Protokoll in der GUI sichtbar
- wird das vollständige Protokoll zusätzlich unter `.\Logs` gespeichert
- wird dort auch eine Liste neu erzeugter Ausgabedateien gespeichert, damit sie anschließend geprüft oder bei Bedarf in Emby katalogisiert werden können

## Unterstützte Dateien

Im aktuellen Serien-Modul werden verwendet:

- Hauptvideo: `.mp4`
- optionale Audiodeskription: `.mp4`
- optionale Untertitel: `.srt`, `.ass`, `.vtt`
- optionale TXT-Begleitdatei: `.txt`

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

- `mkvmerge.exe` wird automatisch im neuesten Ordner `%USERPROFILE%\Downloads\mkvtoolnix-64-bit-*\mkvtoolnix` gesucht.
- Der Startordner für Videoquellen bevorzugt `Downloads\MediathekView-latest-win\Downloads`, fällt aber automatisch auf `Dokumente` zurück, wenn der Ordner nicht existiert.
- Die Standard-Serienbibliothek ist links unten konfigurierbar und wird lokal in `.\Data\settings.json` gespeichert.
- Portable Daten und Logs bleiben im Anwendungsordner.

## Starten

```powershell
dotnet build
dotnet run
```

im Projektordner:

`<dein-projektordner>\mkvtoolnix-Automatisierung`

## Manuelle Releases

Für gelegentliche Releases gibt es einen bewusst manuell ausgelösten GitHub-Workflow unter `.github/workflows/release.yml`.

Der Workflow:

- wird nicht bei jedem Push ausgelöst
- baut die App in `Release`
- führt Unit- und Integrationstests seriell aus
- veröffentlicht anschließend eine selbst enthaltene Single-File-Exe für `win-x64`
- erstellt dazu ein Git-Tag `v<Version>` und eine GitHub-Release-Seite

Die Release-Datei enthält die Anwendung selbst samt .NET-Laufzeit in einer einzigen `.exe`.  
Die externen Werkzeuge `mkvmerge.exe` und optional `ffprobe.exe` bleiben bewusst separate Tools und werden nicht in die Release-Datei eingebettet.

### Versionsnummern

Die Release-Version wird beim manuellen Start des Workflows eingegeben und folgt bewusst einfachem SemVer:

- `Major`: nur erhöhen, wenn du bewusst einen harten Bruch einführst
- `Minor`: für neue Funktionen und größere fachliche Erweiterungen
- `Patch`: für Bugfixes, kleine Verbesserungen und Doku-/Pflege-Releases

Typische Beispiele:

- `1.0.0` für einen ersten echten Release-Stand
- `1.1.0` für neue fachliche Fähigkeiten ohne harten Bruch
- `1.1.1` für reine Korrekturen auf demselben Stand

Die Action erwartet die Eingabe ohne `v`, also zum Beispiel `1.4.0`.  
Das Git-Tag und der Release-Name werden dann automatisch als `v1.4.0` erzeugt.

### Lokaler Release-Build

Wenn du denselben Release-Typ lokal bauen willst:

```powershell
.\scripts\publish-release.ps1 -Version 1.4.0
```

Das Skript erzeugt eine selbst enthaltene Single-File-Exe unter `.\artifacts\release\`.

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

## Projektaufbau

- `MainWindow.xaml`: Shell mit Modulnavigation und Tool-Status
- `ViewModels/MainWindowViewModel.cs`: Shell-ViewModel
- `Composition/`: Composition-Root und fachlich getrennte DI-Registrierungsmodule
- `Views/`: WPF-Views für die einzelnen Module
- `ViewModels/Modules/`: ViewModels der einzelnen Module
- `Services/`: technische Dienste wie Dialoge, Toolsuche und Prozessausführung
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
