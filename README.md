# MKVToolNix-Automatisierung

[![CI and Docs](https://github.com/tobby88/MKVToolNix-Automatisierung/workflows/CI%20and%20Docs/badge.svg)](https://github.com/tobby88/MKVToolNix-Automatisierung/actions/workflows/ci-docs.yml)

## Wichtiger Hinweis

Dieses Projekt wurde vollständig KI-gestützt erstellt und weiterentwickelt.  
Verantwortlich für Konzeption, Code-Erstellung, Überarbeitungen und große Teile der Dokumentation ist die KI, nicht ein klassisch manuell entwickeltes Teamprojekt.

Eine bewusst schlanke WPF-App, um wiederkehrende MKVToolNix-Abläufe für einzelne Episoden oder ganze Ordner Schritt für Schritt zu automatisieren.

## Lizenz

Dieses Repository steht unter `CC BY-NC-SA 4.0`, siehe [LICENSE.md](LICENSE.md).

Praktisch bedeutet das:

- Nutzung und Weitergabe sind erlaubt
- kommerzielle Nutzung ist nicht erlaubt
- Wenn geänderte Fassungen weitergegeben oder veröffentlicht werden, müssen sie wieder unter derselben Lizenz stehen
- der ursprüngliche Autor muss genannt bleiben

Wichtig:

Creative Commons empfiehlt diese Lizenzfamilie selbst nicht für Software. Sie wurde hier trotzdem bewusst gewählt, weil sie die gewünschten Bedingungen für dieses Repository am besten abbildet.

## Überblick

Die Anwendung besteht aktuell aus zwei Modulen:

- `Einzelepisode`: eine einzelne Episode erkennen, prüfen und muxen
- `Batch`: einen Ordner scannen und mehrere Episoden gesammelt verarbeiten

Die Navigation erfolgt über die linke Modulleiste. Rechts wird jeweils das ausgewählte Modul angezeigt.

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
- optionaler Anhang: `.txt`

`.ttml` wird nicht gemuxt, aber als Begleitdatei für Cleanup/Archiv-Aufräumen berücksichtigt.

Mehrere normale Audiospuren innerhalb einer frischen Quelldatei bleiben im Mux-Plan erhalten. Eine separate Audiodeskriptionsspur wird weiterhin heuristisch getrennt behandelt und nicht als normale Tonspur mitgeführt.

## Projektaufbau

- `MainWindow.xaml`: Shell mit Modulnavigation und Tool-Status
- `ViewModels/MainWindowViewModel.cs`: Shell-ViewModel
- `Composition/`: Composition-Root und fachlich getrennte DI-Registrierungsmodule
- `Views/`: WPF-Views für die einzelnen Module
- `ViewModels/Modules/`: ViewModels der einzelnen Module
- `Services/`: technische Dienste wie Dialoge, Toolsuche und Prozessausführung
- `Services/AppModuleServices.cs`: kleinere Service-Bundles für Einzelmodus, Batch und Shell statt eines globalen Sammelobjekts
- `Modules/SeriesEpisodeMux/`: Fachlogik für Muxing und Dateierkennung

Die App verwendet `Microsoft.Extensions.DependencyInjection`, bleibt aber bewusst bei einem klaren Composition Root. `IServiceProvider` wird nicht durch die Fachlogik gereicht; aufgelöst wird nur zentral beim App-Start.

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

## Hinweise

- `mkvmerge.exe` wird automatisch im neuesten Ordner `%USERPROFILE%\Downloads\mkvtoolnix-64-bit-*\mkvtoolnix` gesucht.
- Der Startordner für Videoquellen bevorzugt `Downloads\MediathekView-latest-win\Downloads`, fällt aber automatisch auf `Dokumente` zurück, wenn der Ordner nicht existiert.
- Die Standard-Serienbibliothek ist links unten konfigurierbar und wird lokal in `.\Data\settings.json` gespeichert.
- Portable Daten und Logs bleiben im Anwendungsordner.

## Veröffentlichung

Vor einer Veröffentlichung oder Weitergabe sollte beachtet werden:

- `.\Data\settings.json` kann lokale Toolpfade sowie TVDB-Zugangsdaten enthalten und gehört nicht ins Repo
- `.\Logs` enthält Laufprotokolle und lokale Dateipfade und gehört ebenfalls nicht ins Repo
- dieses Projekt ist wegen der `NC`-Klausel nicht als klassische Open-Source-Lizenz im OSI-Sinne zu verstehen
