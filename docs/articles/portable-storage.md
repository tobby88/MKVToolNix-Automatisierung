# Portable Daten und Logs

## Verzeichnisstruktur

Die Anwendung verwendet bewusst portable Laufzeitordner relativ zur EXE:

- `.\Data`
  - Einstellungen, Backups und Korruptions-Snapshots
- `.\Logs`
  - Batch-Logs und Listen neu erzeugter Ausgabedateien
- `.\Tools`
  - automatisch bereitgestellte MKVToolNix- und ffprobe-Versionen

Ein persistenter Dateisystem-`Cache` ist absichtlich nicht mehr Teil des Projekts. Kurzlebige Performance-Caches bleiben ausschließlich im Speicher.

## Wichtige Konsequenzen

- `Data/settings.json` kann TVDB-Zugangsdaten und lokale Serienzuordnungen enthalten und gehört nicht in ein öffentliches Repository.
- `Logs` kann lokale Dateipfade enthalten und sollte bei Releases oder Uploads ebenfalls bewusst behandelt werden.
- `Tools` kann große heruntergeladene Toolversionen enthalten und wird jederzeit aus den Einstellungen bzw. vom Start-Check neu aufgebaut.
- `.gitignore` schließt diese lokalen Laufzeitverzeichnisse deshalb standardmäßig aus.
