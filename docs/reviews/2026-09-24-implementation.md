# Umsetzung der offenen Reviewpunkte

Ausgangspunkt: `37b643d`. Auftrag vom 2026-09-24: alle Punkte der Restliste bearbeiten,
seriell, mit Regressionstests, Kommentaren und kleinen englischsprachigen Commits.
Die ursprüngliche Liste unter `2026-09-22/open-findings.md` bleibt als Ausgangsbefund erhalten.

## Arbeitspakete

- [x] A01 (O06, O12): Windows-Dateinamen/Pfadlängen und umgeleitete Downloads-Ordner.
- [ ] A02 (O05): Physische Pfadgrenzen und Alias-/Link-Schutz für schreibende Operationen.
- [ ] A03 (O01): Konfliktsichere NFO-/Report-Aktualisierung und entsprechende Rennfalltests.
- [ ] A04 (O03, O04): Vollständige Sortier-Vorprüfung und paketweises Rollback.
- [ ] A05 (O02): Wiederherstellbare Archivänderungen über Header, NFO und Rename.
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
