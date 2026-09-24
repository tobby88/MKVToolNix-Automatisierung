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
- `Data/IMDb/imdb-episodes.sqlite` ist der optionale, jederzeit neu aufbaubare IMDb-Episodenindex. Stand September 2026 benötigt er ungefähr 1,3 GiB dauerhaft; während eines Updates sollten wegen des atomaren Austauschs und der rund 750 MiB Roharchive 4 bis 5 GiB frei sein. Temporär geladene GZip-Rohdaten liegen nur während eines ausdrücklich bestätigten Updates in einem Staging-Unterordner.
- `Logs` kann lokale Dateipfade enthalten und sollte bei Releases oder Uploads ebenfalls bewusst behandelt werden.
- Der Emby-Abgleich speichert seinen Bearbeitungsstand im jeweiligen Metadatenreport. Nach dem Speichern liegen teilweise bearbeitete Reports in `partial` und vollständig erledigte in `done`, relativ zum ursprünglichen Reportordner. Beide können zum Fortsetzen oder erneuten Bearbeiten importiert werden; bewusste Entscheidungen gegen eine Provider-ID bleiben im Report erhalten.
- `Tools` enthält automatisch verwaltete Programme. MKVToolNix und ffprobe lassen sich aus ihren Paketen neu aufbauen; nach erfolgreicher Installation und Speicherung wird nur die vorher referenzierte Version bereinigt, nicht jeder benachbarte Ordner.
- MediathekView kann zusätzlich Benutzerdaten enthalten. Alte Installationen bleiben deshalb erhalten; Reparaturen derselben Version behalten eine `.replaced-*`-Rückfallkopie. Diese Ordner werden nicht als aktive Toolversion gestartet. Vor manueller Bereinigung immer Einstellungen und eigene Aufnahmeordner prüfen.
- Remuxe nutzen kurzzeitig einen `.mux-*`-Unterordner im Ausgabeordner mit einer `.tmp`-Datei. Erst der erfolgreiche Abschluss macht daraus die endgültige MKV. Eine laufende Emby-Überwachung bekommt keine zusätzliche temporäre Mediendatei angeboten. Nach einem harten Prozessabsturz können solche Reste liegen bleiben; im Wiederherstellungsdialog gezielt prüfen, nicht während eines laufenden Mux entfernen.
- `.gitignore` schließt diese lokalen Laufzeitverzeichnisse deshalb standardmäßig aus.

## Wiederherstellung

`Einstellungen > Wiederherstellung` scannt ausdrücklich gewählte Bereiche nach bekannten
Arbeitsnamen, nicht nach beliebigen vermeintlich unnötigen Dateien. Aktive Toolpfade und
Links werden ausgeschlossen. Ein Entfernen erfordert Bestätigung und nutzt den Papierkorb.

NFO-/JSON-Sicherungen heißen `.DATEI.edit-GUID-SHA256.tmp`; `.writing` bezeichnet eine noch
nicht vollständig veröffentlichte Sicherung. Nur vollständige, hashgeprüfte Sicherungen
werden direkt zur Wiederherstellung angeboten. Der aktuelle Zielinhalt bleibt zusätzlich
unter `.DATEI.before-recovery-GUID.tmp` erhalten. Historische ungeprüfte Sicherungen sind
nur zur manuellen Sichtung vorgesehen.

Archivpflege legt `.archive-change-GUID.json` vor dem ersten schreibenden Schritt an und
aktualisiert den belegten Schritt. Erfolg entfernt den Beleg; Fehler/Abbruch nennt seinen
Pfad. Wiederaufnahme bedeutet erneut scannen und Reständerungen freigeben, kein blindes
Replay. Auch ein beschädigter Beleg ist kein Grund, eine vorhandene MKV zu ersetzen.

MediathekView-Migration wartet bei laufendem MediathekView oder nicht sicher zuordenbaren
Java-Prozessen. Die entpackten Tool-Payloads sind auf 8 GiB und 100.000 Dateien begrenzt;
bei messbarem freien Speicher bleiben mindestens 64 MiB Reserve. Diese Reserve ersetzt
nicht den zusätzlich benötigten Speicher für Downloadarchive oder eigene Aufnahmen.
