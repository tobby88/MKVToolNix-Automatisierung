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
- [ ] A06 (O07, O08, O25): Sidecar-Muster, strikte Trackwerte und Mehrfachfolgen-Regeln.
- [ ] A07 (O21): Schutz vor konkurrierenden App-Instanzen.
- [ ] A08 (O14, O23): Abbrechbare Probes, Timeouts und generationensichere Caches.
- [ ] A09 (O20): Batchweiter Cleanup gemeinsam genutzter Quellen nach Erfolg aller Verbraucher.
- [ ] A10 (O26): Ereignisgesteuerter Grid-Refresh statt Idle-Polling.
- [ ] A11 (O16): Explizite Emby-Zuordnung, kein unbeabsichtigter globaler Scan.
- [ ] A12 (O15): Durchgängiger Emby-Abbruch mit Erhalt von Teilresultaten.
- [ ] A13 (O11): Gemessene/gezielte Emby-Listen-, Log- und Lookup-Verbesserungen.
- [ ] A14 (O09): IMDb-Kandidatensuche und konsistente Ergebnislimits.
- [ ] A15 (O22): Indexintegrität, Importplausibilität und Aktivierungs-/Settings-Abgleich.
- [ ] A16 (O17, O19): MediathekView-Migrationsschutz und deterministische Toolauswahl.
- [ ] A17 (O24): Begrenzte Extraktionsgröße und Speicherplatzprüfung.
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
