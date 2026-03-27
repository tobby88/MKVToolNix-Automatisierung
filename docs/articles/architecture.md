# Architektur

## Überblick

Das Projekt ist eine portable WPF-Anwendung zur halbautomatischen Aufbereitung episodischer Videoquellen für `mkvmerge`. Die Fachlogik ist bewusst in Services und Modulklassen ausgelagert, damit UI, Dateisystemlogik und Mux-Planung getrennt wartbar bleiben.

## Hauptbausteine

- `Modules/SeriesEpisodeMux`
  - erkennt Episodenquellen, erzeugt Mux-Pläne und kapselt die mkvmerge-spezifische Argumentlogik
- `Services`
  - enthält Archivintegration, Pfadbildung, Dateicopy/Cleanup, Logpersistenz und Settings-Zugriffe
- `Services/Metadata`
  - kapselt TVDB-Zugriff, Caching und lokale Serien-Zuordnungen
- `ViewModels/Modules`
  - stellt Einzel- und Batch-Workflow für die GUI bereit

## Datenfluss

1. Eine Quelle wird ausgewählt oder ein Ordner wird gescannt.
2. `SeriesEpisodeMuxPlanner` erkennt zugehörige Video-, AD-, Untertitel- und TXT-Dateien.
3. `EpisodeMetadataLookupService` kann die lokale Erkennung mit TVDB-Daten anreichern.
4. `SeriesArchiveService` entscheidet, ob direkt neu gemuxt wird, ob eine bestehende Archivdatei wiederverwendet wird oder ob eine Arbeitskopie nötig ist.
5. `SeriesEpisodeMuxPlan` beschreibt den vollständigen mkvmerge-Aufruf.
6. `MuxWorkflowCoordinator` führt Arbeitskopie, Mux und temporäres Aufräumen aus.
7. `BatchRunLogService` schreibt bei Batch-Läufen Log- und Reportdateien in `.\Logs`.

## Warum DocFX

Für C# ist der direkte Gegenpart zum typischen Doxygen-Stil nicht ein anderes Kommentarformat, sondern der Standard aus XML-Dokumentationskommentaren (`/// <summary>`, `/// <param>`, `/// <returns>`). DocFX baut darauf auf und erzeugt daraus API-Seiten plus eine statische Doku-Site.
