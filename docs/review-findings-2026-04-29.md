# Review-Arbeitsliste 2026-04-29

Diese Liste zerlegt den Gesamt-Review in kleine, seriell abzuarbeitende Aufgaben.
Jeder Punkt wird nach Umsetzung mit passenden Tests und einem thematischen Commit abgehakt.

## Infrastruktur, Tests und Doku

- [x] Zeilenend-/Encoding-Konfiguration vereinheitlichen.
- [x] Dupliziertes `CopyCodeCoverageShim`-MSBuild-Ziel zentralisieren.
- [ ] README/Testing-Doku um kritische Provider-/Emby-Edgecases ergänzen.

## Download, Einsortieren und Dateioperationen

- [x] Batch-Artefaktnamen kollisionssicher machen.
- [x] `done` beim Einsortieren als reservierten Ordner behandeln.
- [x] Stale Quellen in der DownloadSort-Konfliktprüfung robust überspringen.
- [x] DownloadSort-Overwrite zwischen Prüfung und Move TOCTOU-sicherer machen.
- [x] DownloadSort-Move-Quellen auf den gewählten Root begrenzen.
- [x] UI-Cancellation für lange Einsortierläufe ergänzen.
- [x] Case-only Ordner-Kanonisierung beim Einsortieren prüfen und umsetzen.
- [x] `FileStateSnapshot.TryCreate` race-sicher machen.
- [x] Arbeitskopien im `FileCopyService` atomar ersetzen.
- [x] Leere gewählte Quellordner nach Cleanup erhalten oder gezielt neu anlegen.

## Muxen

- [x] Stale Dispatcher-Progress-Callbacks nach Abschluss/Abbruch ignorieren.
- [x] `TestSelectedSourcesCommand` nach manueller AD-Änderung neu auswerten.
- [x] Lange Einzel-Erkennung und Batch-Redetect abbrechbar machen.
- [x] `SxxExx`-Ausführung blockieren oder explizit bestätigen lassen.
- [x] Batch-Abbruchmeldungen nach Review-Ursache differenzieren.
- [x] Laufzeitfremde Varianten derselben Episode nicht im Cleanup miträumen.
- [x] Inkompatible AD-Laufzeit nicht automatisch wählen.
- [x] Eingebettete Untertitel frischer Primärquellen explizit ausschließen.
- [x] Videoreihenfolge im PlanCache-Key berücksichtigen.
- [x] Originalsprache-Regeln für ISO-Code-Normalisierung erweitern.
- [x] Arbeitskopie-Staleness stärker validieren.
- [x] AD-Heuristik um englische Marker erweitern.
- [x] Single-/Batch-XAML-Duplikate prüfen und sinnvoll reduzieren.

## Archivpflege

- [x] Rename/NFO-Apply-Fehler konsistent behandeln.
- [x] Remux-Hinweise nach direktem Apply sichtbar halten.
- [x] Apply-Logging vollständig machen.
- [x] NFO-Lock beim Zurücktippen auf Ist-Wert bereinigen.
- [x] Case-sensitive Zielkonflikte bei Case-only-Renames prüfen/verbessern.

## Emby, TVDB und IMDb

- [x] TVDB-Specials aus dem manuellen Dialog als `S00E..` übernehmen.
- [x] `Keine IMDb-ID` auch ohne weitere Provider-ID in die NFO schreiben.
- [x] Lokale NFO-Analyse bei Emby-Netzwerkfehlern weiterführen.
- [x] Erledigt-Markierung ohne Emby-Refresh sauber modellieren.
- [x] Emby-Scan-Scope deutlicher begrenzen oder anzeigen.
- [x] TVDB-/IMDb-Netzwerkfehler benutzerfreundlich übersetzen.
- [x] TVDB-/IMDb-Pagination mit Limit und Loop-Erkennung absichern.
- [x] Status `Ohne NFO-Sync` farblich klarer darstellen.

## Tooling, Startup und Einstellungen

- [x] Settings-Recovery-Warnung bei Tool-State-Save erhalten.
- [x] Toolprüfung im Einstellungsdialog abbrechbar machen.
- [x] Verwaltete MediathekView-Installation aus `Tools` wiederfinden.
- [ ] HTTP-`Accept`-Header pro Toolquelle passend setzen.
- [ ] Startup-Cancellation in Migration/Cleanup beachten.
- [ ] Numerische Einstellungen sichtbar validieren.
