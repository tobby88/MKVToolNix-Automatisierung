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
- [ ] DownloadSort-Move-Quellen auf den gewählten Root begrenzen.
- [ ] UI-Cancellation für lange Einsortierläufe ergänzen.
- [ ] Case-only Ordner-Kanonisierung beim Einsortieren prüfen und umsetzen.
- [ ] `FileStateSnapshot.TryCreate` race-sicher machen.
- [ ] Arbeitskopien im `FileCopyService` atomar ersetzen.
- [ ] Leere gewählte Quellordner nach Cleanup erhalten oder gezielt neu anlegen.

## Muxen

- [ ] Stale Dispatcher-Progress-Callbacks nach Abschluss/Abbruch ignorieren.
- [ ] `TestSelectedSourcesCommand` nach manueller AD-Änderung neu auswerten.
- [ ] Lange Einzel-Erkennung und Batch-Redetect abbrechbar machen.
- [ ] `SxxExx`-Ausführung blockieren oder explizit bestätigen lassen.
- [ ] Batch-Abbruchmeldungen nach Review-Ursache differenzieren.
- [ ] Laufzeitfremde Varianten derselben Episode nicht im Cleanup miträumen.
- [ ] Inkompatible AD-Laufzeit nicht automatisch wählen.
- [ ] Eingebettete Untertitel frischer Primärquellen explizit ausschließen.
- [ ] Videoreihenfolge im PlanCache-Key berücksichtigen.
- [ ] Originalsprache-Regeln für ISO-Code-Normalisierung erweitern.
- [ ] Arbeitskopie-Staleness stärker validieren.
- [ ] AD-Heuristik um englische Marker erweitern.
- [ ] Single-/Batch-XAML-Duplikate prüfen und sinnvoll reduzieren.

## Archivpflege

- [ ] Rename/NFO-Apply-Fehler konsistent behandeln.
- [ ] Remux-Hinweise nach direktem Apply sichtbar halten.
- [ ] Apply-Logging vollständig machen.
- [ ] NFO-Lock beim Zurücktippen auf Ist-Wert bereinigen.
- [ ] Case-sensitive Zielkonflikte bei Case-only-Renames prüfen/verbessern.

## Emby, TVDB und IMDb

- [ ] TVDB-Specials aus dem manuellen Dialog als `S00E..` übernehmen.
- [ ] `Keine IMDb-ID` auch ohne weitere Provider-ID in die NFO schreiben.
- [ ] Lokale NFO-Analyse bei Emby-Netzwerkfehlern weiterführen.
- [ ] Erledigt-Markierung ohne Emby-Refresh sauber modellieren.
- [ ] Emby-Scan-Scope deutlicher begrenzen oder anzeigen.
- [ ] TVDB-/IMDb-Netzwerkfehler benutzerfreundlich übersetzen.
- [ ] TVDB-/IMDb-Pagination mit Limit und Loop-Erkennung absichern.
- [ ] Status `Ohne NFO-Sync` farblich klarer darstellen.

## Tooling, Startup und Einstellungen

- [ ] Settings-Recovery-Warnung bei Tool-State-Save erhalten.
- [ ] Toolprüfung im Einstellungsdialog abbrechbar machen.
- [ ] Verwaltete MediathekView-Installation aus `Tools` wiederfinden.
- [ ] HTTP-`Accept`-Header pro Toolquelle passend setzen.
- [ ] Startup-Cancellation in Migration/Cleanup beachten.
- [ ] Numerische Einstellungen sichtbar validieren.
