# Portable Daten und Logs

## Verzeichnisstruktur

Die Anwendung verwendet bewusst portable Laufzeitordner relativ zur EXE:

- `.\Data`
  - Einstellungen, Backups und Korruptions-Snapshots
- `.\Logs`
  - Mux-Logs, Listen neu erzeugter Ausgabedateien und fortlaufende Sitzungslogs der allgemeinen Module
- `.\Tools`
  - automatisch bereitgestellte MKVToolNix- und ffprobe-Versionen
  - optional automatisch bereitgestellte portable MediathekView-Versionen

Ein persistenter Dateisystem-`Cache` ist absichtlich nicht mehr Teil des Projekts. Kurzlebige Performance-Caches bleiben ausschließlich im Speicher.

## Wichtige Konsequenzen

- `Data/settings.json` kann TVDB-Zugangsdaten und lokale Serienzuordnungen enthalten und gehört nicht in ein öffentliches Repository.
- `Data/IMDb/imdb-episodes.sqlite` ist der optionale, jederzeit neu aufbaubare IMDb-Episodenindex. Stand Juli 2026 benötigt er ungefähr 2,7 GiB dauerhaft; während eines Updates sollten wegen des atomaren Austauschs und der rund 750 MiB Roharchive 6 bis 7 GiB frei sein. Temporär geladene GZip-Rohdaten liegen nur während eines ausdrücklich bestätigten Updates in einem Staging-Unterordner.
- `Logs` kann lokale Dateipfade enthalten und sollte bei Releases oder Uploads ebenfalls bewusst behandelt werden.
- `Tools` kann große heruntergeladene Toolversionen enthalten und wird jederzeit aus den Einstellungen bzw. vom Start-Check neu aufgebaut.
- `.gitignore` schließt diese lokalen Laufzeitverzeichnisse deshalb standardmäßig aus.
