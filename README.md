# MKVToolNix-Automatisierung

Ein bewusst einfach gehaltenes C#-Projekt, um wiederkehrende MKVToolNix-Abläufe Schritt für Schritt zu automatisieren.

## Aktueller Stand

Die Anwendung ist als modulare WPF-App aufgebaut.

Aktuell gibt es zwei getrennte Funktionen:

- `Einzelepisode`: eine einzelne Episode mit automatischer Dateisuche und manueller Korrektur muxen
- `Batch-Verarbeitung`: einen Ordner nach mehreren Episoden scannen und nacheinander verarbeiten

Die Navigation erfolgt über eine linke Modulliste. Rechts wird jeweils das ausgewählte Modul angezeigt.

## Portable Modus

Die App ist bewusst portabel gedacht und nicht für eine klassische Installation vorgesehen.

- Es gibt keinen Installer.
- Einstellungen werden lokal unter `.\Data\settings.json` neben der Anwendung gespeichert.
- Reservierte Unterordner für portable Laufzeitdaten sind `.\Data`, `.\Cache` und `.\Logs`.
- Der Anwendungsordner muss beschreibbar sein.
- Die App sollte deshalb nicht aus `C:\Program Files` gestartet werden.

## Unterstützte Dateien

Im aktuellen Serien-Modul werden verwendet:

- Hauptvideo: `.mp4`
- optionale Audiodeskription: `.mp4`
- optionale Untertitel: `.srt`, `.ass`, `.vtt`
- optionaler Anhang: `.txt`

`.ttml` wird weiterhin ignoriert.

## Projektaufbau

- `MainWindow.xaml`: Shell mit Modulnavigation
- `ViewModels/MainWindowViewModel.cs`: Shell-ViewModel
- `Views/`: WPF-Views für die einzelnen Module
- `ViewModels/Modules/`: ViewModels der einzelnen Module
- `Services/`: technische Dienste wie Dialoge, `mkvmerge`-Suche und Prozessausführung
- `Modules/SeriesEpisodeMux/`: Fachlogik für Muxing und Dateierkennung

## Starten

```powershell
dotnet build
dotnet run
```

im Projektordner:

`C:\Users\tobby\Documents\mkvtoolnix-Automatisierung`

## Hinweise

- `mkvmerge.exe` wird automatisch im neuesten Ordner `C:\Users\tobby\Downloads\mkvtoolnix-64-bit-*\mkvtoolnix` gesucht.
- Der Startordner für Videoquellen bevorzugt `Downloads\MediathekView-latest-win\Downloads`, fällt aber automatisch auf `Dokumente` zurück, wenn der Ordner nicht existiert.
