# MKVToolNix-Automatisierung

Ein bewusst einfach gehaltenes C#-Projekt, um wiederkehrende MKVToolNix-Abläufe Schritt für Schritt zu automatisieren.

## Aktueller Stand

Die Anwendung ist jetzt als modulare WPF-App aufgebaut.

Aktuell gibt es zwei getrennte Funktionen:

- `Einzelepisode`: muxe eine einzelne Episode mit automatischer Dateisuche und manueller Korrektur
- `Batch-Verarbeitung`: scanne einen Ordner nach mehreren Episoden und verarbeite sie nacheinander

Die Navigation erfolgt über eine linke Modulliste. Rechts wird jeweils das ausgewählte Modul angezeigt.

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
- Die bestehende Einzelfunktion wurde fachlich getestet; die neue Shell- und Batch-Struktur wurde erfolgreich gebaut.
