# Architektur

## Überblick

Das Projekt ist eine portable WPF-Anwendung zur halbautomatischen Aufbereitung episodischer Videoquellen für `mkvmerge`. Die Fachlogik ist bewusst in Services und Modulklassen ausgelagert, damit UI, Dateisystemlogik und Mux-Planung getrennt wartbar bleiben.

## Hauptbausteine

- `Composition`
  - enthält den Composition Root und die DI-Registrierungsmodule
  - trennt Stores, Tooling, Metadaten, Mux-Kern, Workflow und UI-Komposition bewusst voneinander
- `Modules/SeriesEpisodeMux`
  - erkennt Episodenquellen, erzeugt Mux-Pläne und kapselt die mkvmerge-spezifische Argumentlogik
- `Services`
  - enthält Archivintegration, Pfadbildung, Dateicopy/Cleanup, Logpersistenz und Settings-Zugriffe
  - bündelt außerdem gemeinsame technische Heuristiken wie die Text-/Mojibake-Normalisierung, damit Parser und Probe-Service nicht auseinanderlaufen
- `Services/Metadata`
  - kapselt TVDB-Zugriff, Caching und lokale Serien-Zuordnungen
- `ViewModels/Modules`
  - stellt Einzel- und Batch-Workflow für die GUI bereit

## Composition und DI

Der App-Start läuft bewusst über einen klaren Composition Root statt über verteilte Ad-hoc-Auflösungen:

1. `AppCompositionRoot` erstellt eine `ServiceCollection`.
2. `AppCompositionModuleCatalog` registriert die fachlich getrennten Module.
3. Der gebaute `ServiceProvider` wird validiert und nur im Startpfad verwendet.
4. `AppComposition` hält den Root-Provider über die App-Laufzeit am Leben und entsorgt ihn beim Shutdown wieder.

Wichtig dabei:

- Die Fachlogik bekommt ihre Abhängigkeiten weiterhin per Konstruktor.
- `IServiceProvider` wird nicht an ViewModels oder Services weitergereicht.
- Kleinere Service-Bundles in `Services/AppModuleServices.cs` begrenzen, welche Services Einzelmodus, Batch und Shell tatsächlich sehen.

## Datenfluss

1. Eine Quelle wird ausgewählt oder ein Ordner wird gescannt.
2. `SeriesEpisodeMuxPlanner` erkennt zugehörige Video-, AD-, Untertitel- und TXT-Dateien.
   - Für Batch-Ordner kann dafür einmalig ein `DirectoryDetectionContext` vorbereitet und für mehrere Einzelscans wiederverwendet werden.
   - Dieser Kontext dedupliziert teure Kandidaten-Probes pro Datei auch unter parallelen Batch-Scans, damit identische Quellen nicht mehrfach gleichzeitig analysiert werden.
   - TXT-Begleitdateien werden dabei projektweit über denselben Reader und dieselben Encoding-Heuristiken ausgewertet, damit Erkennung, Review und spätere Planerstellung nicht auseinanderlaufen.
   - Für frische Quellen liest die Planerstellung normale Audiospuren aus den vollständigen Container-Metadaten, damit Mehrfach-Audio erhalten bleibt und offensichtliche AD-Spuren nicht versehentlich als normale Tonspuren eingeplant werden.
3. `EpisodeMetadataLookupService` kann die lokale Erkennung mit TVDB-Daten anreichern.
   - Die fachlichen Matching-Heuristiken sind bewusst von Caching und TVDB-I/O getrennt, damit Bewertungsregeln unabhängig wartbar bleiben.
4. `SeriesArchiveService` entscheidet, ob direkt neu gemuxt wird, ob eine bestehende Archivdatei wiederverwendet wird oder ob eine Arbeitskopie nötig ist.
   - Die Archiventscheidung ist dafür in vorbereitende Analyse und eigentliche Integrationsentscheidung getrennt.
5. `SeriesEpisodeMuxPlan` beschreibt den vollständigen mkvmerge-Aufruf.
6. `MuxWorkflowCoordinator` führt Arbeitskopie, Mux und temporäres Aufräumen aus.
7. `BatchRunLogService` schreibt bei Batch-Läufen Log- und Reportdateien in `.\Logs`.
   - Der persistierte Log sammelt gezielt den aktuellen Batch-Lauf, damit Planung, Arbeitskopien und Mux-Ausführung zusammen diagnostizierbar bleiben.

## Warum DocFX

Für C# ist der direkte Gegenpart zum typischen Doxygen-Stil nicht ein anderes Kommentarformat, sondern der Standard aus XML-Dokumentationskommentaren (`/// <summary>`, `/// <param>`, `/// <returns>`). DocFX baut darauf auf und erzeugt daraus API-Seiten plus eine statische Doku-Site.
