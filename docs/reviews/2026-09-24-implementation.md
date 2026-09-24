# Umsetzung der offenen Reviewpunkte

Ausgangspunkt: `37b643d`. Auftrag vom 2026-09-24: alle Punkte der Restliste bearbeiten,
seriell, mit Regressionstests, Kommentaren und kleinen englischsprachigen Commits.
Die ursprüngliche Liste unter `2026-09-22/open-findings.md` bleibt als Ausgangsbefund erhalten.

## Arbeitspakete

- [x] A01 (O06, O12): Windows-Dateinamen/Pfadlängen und umgeleitete Downloads-Ordner.
- [x] A02 (O05): Physische Pfadgrenzen und Alias-/Link-Schutz für schreibende Operationen.
- [x] A03 (O01): Konfliktsichere NFO-/Report-Aktualisierung und entsprechende Rennfalltests.
- [x] A04 (O03, O04): Vollständige Sortier-Vorprüfung und paketweises Rollback.
- [x] A05 (O02): Wiederaufnahmefähige Archivänderungen über Header, NFO und Rename.
- [x] A06 (O07, O08, O25): Sidecar-Muster, strikte Trackwerte und Mehrfachfolgen-Regeln.
- [x] A07 (O21): Schutz vor konkurrierenden App-Instanzen.
- [x] A08 (O14, O23): Abbrechbare Probes, Timeouts und generationensichere Caches.
- [x] A09 (O20): Batchweiter Cleanup gemeinsam genutzter Quellen nach Erfolg aller Verbraucher.
- [x] A10 (O26): Ereignisgesteuerter Grid-Refresh statt Idle-Polling.
- [x] A11 (O16): Explizite Emby-Zuordnung, kein unbeabsichtigter globaler Scan.
- [x] A12 (O15): Durchgängiger Emby-Abbruch mit Erhalt von Teilresultaten.
- [x] A13 (O11): Gemessene/gezielte Emby-Listen-, Log- und Lookup-Verbesserungen.
- [x] A14 (O09): IMDb-Kandidatensuche und konsistente Ergebnislimits.
- [x] A15 (O22): Indexintegrität, Importplausibilität und Aktivierungs-/Settings-Abgleich.
- [x] A16 (O17, O19): MediathekView-Migrationsschutz und deterministische Toolauswahl.
- [x] A17 (O24): Begrenzte Extraktionsgröße und Speicherplatzprüfung.
- [ ] A18 (O18): Sichere Wiederherstellung/Bereinigung erkannter Arbeits-/Backupreste.
- [ ] A19 (O10): Asynchrone UI-Pfadstatusprüfungen ohne veraltete Rückmeldungen.
- [ ] A20 (O13): Layouttests und gezielte Korrekturen für lange Texte/kleine Fenster.
- [ ] A21 (O27, O28): Explizite Erkennungs-/Format-/Interaktionsverträge und Grenzfalltests.
- [ ] A22 (T01-T05): Reale isolierte Tooltests, Last-/Ausfalltests und ergänzende Regressionen.
- [ ] A23 (T06): GitHub-CI des Ausgangsstands prüfen, lokale vollständige Abschlussprüfung.
- [ ] A24: README/DocFX/Screenshots aktualisieren, Gesamtstatus und Debug-EXE/DLL prüfen.

## Sicherheitsgrenzen

Keine Tests schreiben in das Benutzerarchiv oder an echte Emby-NFOs. Medien-/Ausfalltests
verwenden isolierte temporäre Dateien. Keine ungefragten großen Datensatzdownloads oder
produktiven Server-Scans. Nicht verfügbare Praxisprüfungen werden als solche ausgewiesen,
nicht als erfolgreich abgehakt. Temporäre Medien enden weiterhin ausschließlich auf `.tmp`.

## Ergebnisse

### A01

Reservierte Gerätenamen, unzulässige Zeichen und Komponenten-/Gesamtpfadlängen werden
vor Archivumbenennungen geprüft. Gültige Apostrophe und Auslassungspunkte bleiben erlaubt;
das historische 260-Zeichen-Limit wird nicht künstlich eingeführt. Windows-Known-Folders
liefern den tatsächlich konfigurierten Downloads-Pfad, mit isolierbarem Profil-Fallback.
63 gezielte Unit-Tests erfolgreich.
Grundlagen: [Windows-Dateinamen](https://learn.microsoft.com/windows/desktop/FileIO/naming-a-file)
und [Known Folders](https://learn.microsoft.com/en-us/windows/win32/shell/known-folders).

### A02

Schreibgrenzen in Archivpflege, Sortierung, Mux-Veröffentlichung und Quellcleanup prüfen
vorhandene Pfadvorfahren auf Reparse-Points sowie unterstützte Windows-Case-Sensitive-Flags.
Mehrfach verlinkte Dateien werden vor Änderungen abgewiesen. Es gibt bewusst keine
automatische Auflösung fremder UNC-/Link-Aliase in zugelassene Quellordner. Exakte
Zielkollisionen bleiben zusätzlich geprüft. Kein Sicherheitsversprechen gegen nachträglichen
Pfadaustausch durch lokale Angreifer oder nicht kooperierende SMB-Server.
100 gezielte Tests erfolgreich, darunter echte isolierte Hardlinks und Junctions.
Die nativen Vorfahrenprüfungen erfordern beim Testlauf außerhalb des App-Betriebs
eine Ausführung außerhalb der Codex-Dateisystem-Sandbox.

### A03

NFO- und Reportbearbeitung nutzen einen bytegenauen, größenbegrenzten Lesesnapshot.
Vor Veröffentlichung wird unter exklusiver Dateisperre erneut verglichen; auch gleich
große Änderungen mit erhaltenem Zeitstempel werden erkannt und nicht überschrieben.
Die Sperre schließt das bisherige Compare/Replace-Rennfenster. Die kurze In-place-Schreibphase
wird durch eine vorab geflushte Original-Sicherung geschützt; normale Fehler werden
zurückgerollt, bei hartem Prozessabbruch bleibt eine `.edit-*.tmp`-Sicherung erhalten.
Unveränderte NFOs werden weiterhin nicht geschrieben. 96 gezielte Tests erfolgreich.
Die Wiederherstellungsoberfläche für Absturzsicherungen folgt in A18.

### A04

Alle ausgewählten Pakete werden vor relevanten Ordnerumbenennungen geprüft. Ungültige
oder fehlende Quellen lösen keine Ordnerumbenennung mehr aus. Video, Untertitel und
Defekt-Teilmenge werden gemeinsam verschoben; ein späterer Fehler rollt frühere Moves
und ersetzte Ziele zurück. Abbruch erfolgt zwischen vollständigen Paketen. Alte einzelne
Move-/Replace-Helfer wurden entfernt. 53 Sortier-/Pakettests erfolgreich, einschließlich
echter Sidecar-Sperre nach bereits erfolgreichem Videoersatz und Defekt-Teilrollback.

### A05

NFO-Lesbarkeit und leere Provider-Aufträge werden vor Headeränderungen geprüft.
Vor jedem schreibenden Archivschritt wird der freigegebene Auftrag mit Schrittname
dauerhaft protokolliert. Fehler/Abbruch benennen ausdrücklich mögliche Teiländerungen
und den Wiederaufnahmebeleg. Erfolgreiche Vorgänge entfernen den Beleg.
Bewusste Strategie: erneuter Scan plus Freigabe der verbleibenden Differenzen, kein
blindes Replay alter Aufträge und keine riesige MKV-Vollkopie für jeden Header-Edit.
In-place-mkvpropedit ist damit ausdrücklich nicht als atomar/rollbackfähig ausgewiesen.
33 gezielte Tests erfolgreich. Sichtung verbliebener Belege folgt in A18.

### A06

Regionale/Script-Sprachsuffixe sowie gezielte zusätzliche Artwork-Suffixe werden beim
Umbenennen mitgenommen, fremde Titelpräfixe weiterhin nicht. Track-Properties, Selektoren,
Flagwerte und Sprachsyntax werden gemeinsam validiert; unbekannte Flagtexte werden nicht
mehr zu Nein. Ungültige manuelle Dateinamen bleiben auch in der UI kontrollierte Fehler.
Bei Doppelfolgen bleiben Staffel/Range, Dateititel und MKV-Titel unabhängig erhalten,
statt einen einzelnen TVDB-Episodentitel auf die gesamte Datei anzuwenden. Manuelle
Korrekturen bleiben möglich. 80 gezielte Archiv-/Header-Tests erfolgreich.

### A07

Ein benutzerbezogener systemweiter Mutex verhindert mehrere GUI-Prozesse auch aus
verschiedenen portablen Ordnern. Die zweite Instanz beendet sich vor Initialisierung
und Downloads mit verständlichem Hinweis. Verwaiste Sperren nach Absturz sind wieder
übernehmbar. Fünf Start-/Mutex-Tests erfolgreich.

### A08

Laufzeitprobes erhalten den Aufrufer-Token bis in ffprobe. Identify hat auch ohne
UI-Abbruch ein 60-Sekunden-Limit. Native COM-Abfragen werden begrenzt abgewartet;
maximal eine noch laufende native Abfrage bleibt pro Probeinstanz zurück, keine
unbegrenzte Ansammlung hängender Threads. Invalidierte Identify-Generationen dürfen
keine Cachewerte nachliefern. Dateisnapshots berücksichtigen verfügbare Windows-Datei-IDs
und Änderungszeiten zusätzlich zu Größe/mtime. Kein Vollhash großer Videos; unbekannte
Serversemantik und In-place-Änderungen innerhalb der Zeitstempelauflösung bleiben Grenzen.
30 gezielte Unit- und sieben Prozess-Integrationstests erfolgreich.

### A09

Gemeinsam genutzte Quellen werden bis zum Ende zurückgestellt und erst verschoben,
wenn alle referenzierenden/aufräumenden Pläne erfolgreich abgeschlossen sind. Bei
Abbruch oder einem fehlgeschlagenen Verbraucher bleiben sie erhalten. Batch-Ausgaben
bleiben grundsätzlich ausgeschlossen. Cleanup-Fehler ändern nicht rückwirkend die
bereits erzeugten Ausgabereports. 20 Batch-Runner-Tests erfolgreich.

### A10

Batch-CollectionViews warten bei offenen Edit-Transaktionen auf deren PropertyChanged-
Ende statt immer neue ContextIdle-Arbeit zu erzeugen. Refreshes werden zusammengefasst,
nach Dispose verworfen und am Dispatcher der tatsächlichen View ausgeführt. Elf Tests
erfolgreich, einschließlich echtem WPF-Dispatcher und Nachweis ohne Idle-Retry-Schleife.

### A17 (vorgezogen)

Toolarchive sind auf 100.000 Dateien und 8 GiB tatsächlich entpackten Payload begrenzt.
Vor dem ersten Dateiinhalt werden deklarierte Größe und messbarer freier Speicher
(64 MiB Reserve) geprüft; während der Extraktion zählt das Limit tatsächliche Bytes.
Nicht benötigter ffprobe-/MKVToolNix-Payload zählt nicht zum Entpackbedarf. 20 ZIP-/7z-
und Sicherheitsgrenzentests erfolgreich, ohne reale große Downloads.

### A11

Emby-Einstellungen unterstützen einen expliziten Server-Archivpfad und optional eine
Bibliotheks-ID. Explizite Angaben erlauben keinen heuristischen Ersatzpfad; Linux-Pfade
bleiben case-sensitive. Ohne eindeutige Bibliothek wird kein Scan gestartet, insbesondere
kein globaler Fallback. Die kompatible automatische Zuordnung verlangt mindestens zwei
gemeinsame Pfadsegmente. 99 gezielte Service-/ViewModel-/Settings-Tests erfolgreich.

### A12

Ein gemeinsamer Abbrechen-Button gilt für Import/Nachprüfung, Providerprüfungen, Scan
und Schreiben. Netzabfragen erhalten denselben Token; eine begonnene NFO-/Reportschreibphase
wird vollständig beendet. Teilresultate und Reviewfortschritt bleiben erhalten. Nach
geschriebener NFO bleibt ein abgebrochener Refresh ausdrücklich offen. Modale Providerdialoge
können selbst abgebrochen werden; einzelne synchrone Datei-/JSON-Zugriffe werden nicht mitten
im Lesen/Schreiben zerrissen. 86 gezielte Emby-/WPF-Tests erfolgreich.

### A13

Zeilensummen und Auswahlbefehle verwenden inkrementelle Zähler statt wiederholter
Vollscans. Ein 10.000-Zeilen-Test prüft Änderungen, Entfernen und Reset. Logzeilen werden
gepuffert, UI-Benachrichtigungen auf höchstens vier pro Sekunde plus Abschluss begrenzt.
Ein einzelner 30-Sekunden-Library-Snapshot erfasst auch Fehlschläge der Zuordnung; neue
Scans und Serverfortschritt bleiben frisch. 100 identische Fehlzuordnungen benötigen im
Test eine statt 100 Library-Abfragen. 84 gezielte Tests erfolgreich.

### A14

Die Serien-Kandidatensuche verliert keine Treffer mehr an einem willkürlichen
256-Zeilen-/12-Serien-Limit. Das öffentliche Ergebnislimit gilt auch bei größerer Anfrage
und bereits warmem Cache. Ein Edit am Wortanfang (Tausch, Ersetzen, fehlender/zusätzlicher
Buchstabe) wird über zusätzliche indexgestützte Präfixbereiche gefunden. Exakte Namen
benötigen diese Erweiterung nicht. 41 IMDb-Tests erfolgreich, darunter 400 gleichnamige
Serien und vier Wortanfangsfehler; keine vollständigen IMDb-Daten heruntergeladen.

### A15

Verfügbarkeit prüft SQLite-Struktur/Abschlussmarker statt nur Dateiexistenz. Der Update-
Worker prüft aktive und neue Datenbanken mit `quick_check`; Ergebnisse gelten nur für
denselben Dateisnapshot. Ein Rückgang um mehr als 20 Prozent bei mindestens 100 bisherigen
Serien/Episoden/Aliasnamen verhindert die Aktivierung. Kleine Testbestände bleiben erlaubt.
Version/Schema/Aufbauzeit werden aus dem aktiven Index mit den Settings abgeglichen,
damit ein zuvor fehlgeschlagener Settings-Save keinen erneuten Download auslöst.
81 IMDb-Tests erfolgreich; Vollprüfungen bleiben außerhalb des UI-Threads.

### A16, Teil 1

Vor und nach der Übernahme portabler MediathekView-Einstellungen wird auf laufende
MediathekView-/Java-Prozesse geprüft. Bei unklarer Java-Zuordnung wird konservativ
verschoben statt ein möglicherweise veralteter Snapshot aktiviert. Ein solcher Aufschub
bleibt sichtbar und setzt keinen zweistündigen Fehler-Backoff. 37 Installer-Tests grün.

### A16, Teil 2

Fallbackordner werden nach numerischer Version und Stable-vor-Prerelease sortiert,
erst danach nach Datum und Name. Unversionierte Snapshots behalten einen deterministischen
Datumsfallback. Neue manuelle Pfadwahlen erhalten einen Herkunftsmarker, damit auch ein
Downloads-Pfad nicht später als alter automatisch erkannter Override entfernt wird.
Historische Werte ohne Marker folgen aus Kompatibilitätsgründen der bisherigen Migration.
72 gezielte Tool-/Settings-Tests erfolgreich.
