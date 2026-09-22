# Review: Mux Core, 2026-09-22

## Stand und Grenzen

- Ausgang: `master`, `323634f` (beim Start sauber). Der Untertitel-Fix dieses Commits wurde nicht zurückgenommen: passende Archivuntertitel werden weiterhin nur bei tatsächlichem Wechsel der Primärquelle ersetzt.
- Nur zugewiesene Produktionsdateien, zugehörige Tests und dieser Bericht wurden editiert, ausschließlich per `apply_patch`. Kein Commit, Staging, Push, Paketupdate oder eigener Build/Test/Format-Lauf.
- Keine echten Medien oder Archivdateien wurden verändert. Neue Tests verwenden Tempdateien und den vorhandenen FakeMkvMerge.
- `git diff --check` für die eigenen geänderten Bereiche einschließlich Nachtrag: ohne Befund. Ein vom zentralen Build gemeldeter CS9007 in den neuen Identify-Parser-Tests wurde durch normale JSON-Erzeugung behoben.
- Zentraler Stand laut Parent vor MC-O1/O2/O3: Unit/WPF insgesamt **1081/1081 bestanden**; vollständige Integration **129/131 bestanden**. Die beiden gemeldeten Integrationsfehler wurden im Nachtrag gezielt korrigiert (Details unten). Prozessschutz einschließlich Callback-Exceptions ist laut Parent grün.
- Der Nachtrag mit MC-O1/O2/O3 und den zwei Integrationskorrekturen wurde hier ausschließlich statisch geprüft. Kein eigener Build/Test/Format-Lauf und noch kein Ergebnis eines zentralen Wiederholungslaufs für diesen Nachtrag. Quellstand zur zentralen Kompilierung freigegeben.

## Behobene Findings

### MC-01, P1: AD-Ersatz löschte fremdsprachige Archiv-AD

`SeriesArchiveService.Preparation.cs`, beide Entscheidungszweige: Sobald eine neue AD-Datei ausgewählt war, wurde die gesamte Liste vorhandener AD-Spuren verworfen. Eine neue deutsche AD beseitigte damit auch eine englische AD, obwohl kein Ersatz für diese Sprache vorhanden war.

Fix: Neue AD wird zuerst geplant; vorhandene AD anderer Sprachen bleiben erhalten. Eine dafür erforderliche Archiv-Arbeitskopie wird auch bei einem Video-Upgrade eingeplant. Die Nutzungsanzeige listet nur wirklich entfernte AD-Spuren, auch beim Beibehalten des Archivvideos. Die bestehende Regel, dass ein expliziter Ersatz alle bisherigen AD-Spuren derselben Sprache ersetzt, bleibt bestehen.

Regression: `CreatePlanAsync_ReplacingGermanAd_PreservesEnglishArchiveAd` mit und ohne Video-Upgrade; prüft Auswahl, Arbeitskopie und Removed-Anzeige.

### MC-02, P2: Mehrspurige AD-Datei verwendete normalen Ersttrack

Planer und AD-Reuse-Vergleich nahmen pauschal die erste Audiospur. Bei normalem Ton auf Track 1 und expliziter AD auf Track 2 wurde die falsche Spur als AD gemuxt beziehungsweise gegen das Archiv verglichen.

Fix: Gemeinsamer `AudioTrackClassifier.SelectAudioDescriptionTrack`: markierte AD hat Vorrang. Eine unmarkierte Einspurdatei bleibt erlaubt. Mehrere unmarkierte Audiospuren führen zu einem verständlichen Fehler statt einer stillen Vermutung. Die allgemeine API `ReadFirstAudioTrackMetadataAsync` behält ihre dokumentierte Ersttrack-Semantik.

Regression: drei neue Classifier-Tests; `CreatePlanAsync_FreshTarget_SelectsMarkedAdRatherThanNormalFirstTrack`; auch MC-01 nutzt eine Mehrspur-AD-Quelle.

### MC-03, P2: Untertitel anderer Schnittfassungen gelangten zurück in die Auswahl

`SeriesEpisodeMuxPlanner.Detection.cs`: Nach der Laufzeitfilterung normaler Quellen wurden Untertitel von defekten MP4s und Subtitle-only-Seeds ungefiltert angehängt. Das konnte eine 84-Minuten-Untertitelspur mit einem 42-Minuten-Video mischen. Der bereits berechnete Filter für Supplement-Cleanup wurde nicht für die Untertitelauswahl verwendet.

Fix: Gerettete Begleiter und Subtitle-only-Seeds durchlaufen dieselbe Laufzeitgrenze. Bei fehlendem Video dient die deklarierte Laufzeit des gewählten Einstieg-Seeds als Referenz. Bekannte unpassende Untertitel werden auch nicht als Cleanup-Begleiter zurückgegeben; unbekannte Dauern behalten das bisherige Verhalten.

Regression: `Detection_DoesNotSalvageSubtitlesFromDifferentCut` für defektes Video und Subtitle-only-Sibling; `Detection_SubtitleOnly_DoesNotMixDifferentCuts`.

### MC-04, P2: Ausgeschlossene defekte Quellen wurden trotzdem analysiert

Normale Video-Seeds wurden erst nach dem Identify-Aufruf aus der Auswahl entfernt. Eine explizit ausgeschlossene Datei ohne Videospur konnte dadurch die gesamte Erkennung abbrechen.

Fix: Ausgeschlossene normale Quellen vor dem Probe-Aufruf aussieben; Ausschlüsse ebenfalls auf gerettete Untertitel und zurückgegebene Begleitpfade anwenden.

Regression: `Detection_DoesNotProbeExcludedInvalidVideo`.

### MC-05, P2: Frisches Ziel umging Untertitel-Deduplizierung und Formatgrenze

Die Archivplanung verdichtete manuelle Untertitel auf einen Pfad je Format, die frische Planerstellung dagegen nicht. Ferner akzeptierte die Request-Validierung beliebige existierende Dateien als externe Untertitel; deren Argumentblock begrenzte den Import nicht auf Untertitel.

Fix: Auch frische Pläne nutzen `SubtitleSourceSelection` unter Erhalt der Request-Priorität. Externe Formate sind auf ASS/SRT/VTT begrenzt. Ihr Argumentblock deaktiviert Video, Audio und Attachments und wählt explizit Subtitle-Track 0. Manuelle Anhänge werden ebenfalls auf Existenz validiert, auch wenn sie nicht nochmals in `AttachmentPaths` stehen.

Regression: `CreatePlanAsync_FreshTarget_DeduplicatesSubtitleKindsUsingRequestPriority`, `CreatePlanAsync_RejectsContainerAsExternalSubtitle`; erweiterter Argumenttest in `SeriesEpisodeMuxPlanTests`.

### MC-06, P2: Sync-over-async konnte den aufrufenden UI-Kontext blockieren

Der synchrone Kandidatenbau wartet auf `ReadPrimaryVideoMetadataAsync`; `FfprobeDurationProbe.TryReadDuration` wartet ebenfalls synchron auf seine asynchrone Kernprobe. `ConfigureAwait(false)` am äußeren GetAwaiter-Aufruf verhindert nicht, dass innere Awaits den aufrufenden SynchronizationContext einfangen.

Fix: Kontextfreie Awaits in Identify-Runner, asynchronen Probe-Service-Methoden sowie ffprobe-Kernprobe und Drain-Pfad.

Regression: `ReadPrimaryVideoMetadataAsync_DoesNotCaptureCallingSynchronizationContext` mit aufzeichnendem Kontext. Die ffprobe-Kernprobe wurde statisch auf alle Awaits geprüft; ein echter WPF/COM/ffprobe-Deadlocktest wurde nicht ausgeführt.

### MC-07, P2: Cancellation wurde bei Cachetreffern ignoriert

Die asynchronen Identify-Probes und ffprobe prüften das Token erst im Prozesspfad. Ein vorab abgebrochener Aufruf konnte aus dem Cache erfolgreich zurückkehren. Ein ffprobe-Reader, der trotz Cancellation noch einen Wert lieferte, konnte diesen zudem cachen.

Fix: Token vor Lookup/Cache und nach asynchronem Ergebnis prüfen.

Regression: `CachedProbeMethods_RespectPreCanceledToken` für alle drei Identify-Probes; `TryReadDurationAsync_ObservesCancellationBeforeLookupAndOnCacheHit`; `TryReadDurationAsync_DoesNotCacheResultReturnedAfterCancellation`.

### MC-08, P2: Fataler Identify-Exit wurde bei gültigem JSON als Erfolg akzeptiert

`MkvMergeIdentifyRunner.ParseIdentifyResult` prüfte zuerst ausschließlich die JSON-Syntax. Auch Exitcode 2 mit gültiger Trackliste wurde weiterverarbeitet.

Fix: Nur 0 und 1 erlauben ein Erfolgsergebnis; andere Exitcodes werfen mit Diagnose. Warn-Exitcode 1 bleibt unterstützt.

Regression: neue `MkvMergeIdentifyRunnerTests` für 0/1 sowie 2/-1 mit gültigem Fehler-JSON.

### MC-09, P2: Dateihinweis überschrieb alle Sprachen einer Mehrspurquelle

Der Planer wendete einen dateiweiten `op Platt`-Hinweis auf jede normale Audiospur an. Der Archivvergleich betrachtete bei Mehrspurdateien dagegen die individuellen Tracksprachen. Dadurch konnten tatsächliche englische Spuren als Plattdeutsch ausgegeben und Archiv-Replacement-Slots inkonsistent bewertet werden.

Fix: Automatische dateiweite Sprachkorrektur nur bei genau einer normalen Audiospur. Explizite manuelle Overrides gelten weiterhin wie dokumentiert für alle Spuren.

Regression: `CreatePlanAsync_MultilingualPlattSource_PreservesIndividualAudioLanguages`.

### MC-10, P2: Nicht verwendeter TXT-Begleiter konnte den letzten Archivtext entfernen

`BuildAttachmentReusePlanAsync` wertete jeden automatischen Request-Anhang als vorhandenen Ersatz. Die spätere Planung hängt jedoch nur TXT-Dateien tatsächlich gewählter Videos an. So konnte der einzige Archivtext gelöscht werden, obwohl der angenommene neue Text gar nicht im Endplan landete.

Fix: Ersatzsignal kommt nur von Begleitern gewählter frischer Videos oder expliziten manuellen Anhängen.

Regression: `CreatePlanAsync_UnusedFreshTextDoesNotRemoveOnlyArchiveText`.

### MC-11, P2: Sprachalias mit Subtag fiel auf Deutsch zurück

Bekannte dreibuchstabige Sprachcodes mit Region/Script (beispielsweise `swe-SE` oder `eng-US`) wurden nicht wie ihre Grundsprache behandelt. Zusätzlich genügte der deutsche Fallback für unbekannten Audiocode, um ein englisches Video fälschlich in den deutschen Slot zu korrigieren.

Fix: Normalisierung anhand des primären Sprachsubtags; die en/de-Sonderkorrektur fordert tatsächlich als Deutsch erkannte Audiosprache.

Regression: Erweiterte `MediaLanguageHelperTests` für eng-US, swe_SE, fra-CA, cmn-Hans, ger-DE sowie und/unknown beim Audiocode.

### MC-12, P2: Windows-Laufzeitprobe verewigte transiente Fehlversuche

`WindowsMediaDurationProbe` cachierte `null` für unveränderte Dateien dauerhaft, anders als der ffprobe-Pfad. Ein einmaliges COM-/Readiness-Problem verhinderte jeden späteren Versuch.

Fix: Nur positive erfolgreiche Laufzeiten cachen. Interner Reader-Einstieg erlaubt einen Test ohne echten Windows-Media-Player.

Regression: neue `WindowsMediaDurationProbeTests.TryReadDuration_RetriesMissingResultAndCachesSuccess`.

### MC-13, P2/P3: Prozessstatus war inkonsistent oder konkurrierend

`SeriesEpisodeMuxService.ExecuteAsync` aggregierte stdout/stderr ohne Synchronisierung (P2). Exitcode 1 ohne erkannte deutsch-/englischsprachige Warnzeile ergab `HasWarning=false` (P3). Header-Edit-Fehler meldeten stets 100 Prozent (P3). Ein direkt ausgeführter Skip-Plan startete dennoch die Executable ohne Argumente (P3).

Fix: Callback-Aggregation beider Streams pro Ausführung serialisieren; Exitcode 1 setzt Warnstatus; nur erfolgreiche Header-Edits liefern 100 Prozent; Skip-Pläne starten keinen Prozess und respektieren vorab Cancellation.

Regression: `ExecuteAsync_WarningExitWithoutRecognizedWarningLine_SetsWarningStatus`, `ExecuteAsync_FailedHeaderEdit_DoesNotReportCompletion`, `ExecuteAsync_SkipPlanDoesNotLaunchExecutable`. Keine gezielte gleichzeitige stdout/stderr-Stresstest-Ausführung.

### MC-14, P2/P3: Argumente ignorierten explizite Planwerte

Eine leere Attachment-ID-Auswahl konnte bei aktiviertem Import alle Primär-Attachments durchlassen (P2); das Primärvideo bekam unabhängig von `IsDefaultTrack` immer `yes` (P3).

Fix: Leere Attachment-Auswahl bedeutet keine Attachments; Primärvideo respektiert sein konfiguriertes Default-Flag.

Regression: `Build_EmptyPrimaryAttachmentSelectionDoesNotImportAllAttachments`, `Build_PrimaryVideoHonorsDisabledDefaultFlag`.

### MC-15, P3: Kleine Identify-/Dauer-Inkonsistenzen

- Nicht-Objekt-JSON-Roots warfen vor der hilfreichen Dateidiagnose. Der Roottyp wird nun vor `TryGetProperty` geprüft.
- Negative oder als String gelieferte Track-IDs wurden nicht sauber als ungültige IDs zurückgewiesen. Es sind jetzt nur nichtnegative Integer erlaubt.
- Null-/negative Dauern konnten als präzise AD-Vergleichsdauer gelten. Parser und AD-Kompatibilitätsregel lehnen sie nun ab.
- H.265-Labels ohne HEVC beziehungsweise H/265 wurden anders als H.264 nicht auf das kanonische Codec-Label normalisiert.

Regression: neue/erweiterte `MkvMergeIdentifyParserTests`; `SeriesArchiveServiceTests.AreAudioDescriptionDurationsCompatible_RejectsNonPositiveDurations`.

## Nachtrag: MC-O1/O2/O3 Behoben

Die drei zunächst als offen dokumentierten Findings wurden anschließend ausdrücklich zur eng begrenzten Behebung freigegeben. Die Auswahlregeln bleiben bestehen; MC-O2 und MC-O3 ändern ausschließlich die Darstellung.

### MC-O1, P2: Rohe Sprachwerte gingen vor der Header-Korrektur verloren

Der Identify-Parser bevorzugt einen Sprachhinweis im Tracknamen gegenüber den Container-Sprachfeldern. Die Header-Normalisierung verglich danach die bereits abgeleitete Modell-Sprache erneut mit der daraus abgeleiteten Zielsprache. Bei `Plattdeutsch - AAC` mit tatsächlichem Containerwert `de` war die erforderliche Korrektur `de -> nds` nicht mehr erkennbar.

Fix: `ContainerTrackMetadata.RawLanguage` ist eine zusätzliche optionale `string?`-`init`-Property. Bestehender Positionskonstruktor und Deconstruction bleiben unverändert. Der Parser erhält den nichtleeren Wert aus `language_ietf`, ersatzweise `language`, unverändert; fehlen beide, bleibt die Property `null`. `Language` bleibt unverändert die abgeleitete Sprache für Slotwahl, Gruppierung und Zielwerte. Die Header-Normalisierung vergleicht gegen den Rohwert, normalisiert bekannte Code-Aliase und behandelt unbekannte tatsächliche Werte wie `und` nicht bereits als `de`. Für ältere Modellaufrufer ohne Rohwert bleibt der bisherige Vergleich erhalten. Der Parent koordiniert die Verwendung durch den Archivpflege-Agenten.

Regression: `CreateContainerMetadata_PreservesRawLanguageSeparatelyFromInferredLanguage` prüft IETF-Vorrang, Legacy-Fallback, leeren IETF-Wert, `und` und fehlende Tags. `BuildForArchiveFile_UsesRawLanguageForInferredLanguageCorrections` prüft Korrektur und anschließende Idempotenz; `BuildForArchiveFile_AcceptsEquivalentOrUnavailableRawLanguage` deckt Alias- und Altaufrufer-Kompatibilität ab. `CreatePlanAsync_KeepingArchivePrimary_CorrectsRawLanguageWithoutChangingSlots` prüft die gesamte Identify-/Archiv-/Header-Planung inklusive `mkvpropedit`-Argumenten und Vorschau, ohne Medien zu verändern.

### MC-O2, P3: Keep-Archive-Nutzungsanzeige verschwieg entfernte Normal-Audios

Der Keep-Primary-Zweig setzte `UsageComparison.Audio` auf `null`, obwohl frische zusätzliche Videospuren Archiv-Audios derselben Sprache verdrängen können.

Fix: Der Zweig bildet dieselbe Differenz zwischen bisherigen und tatsächlich behaltenen Normal-Audios wie der Replace-Primary-Zweig und übergibt diese an die vorhandene Anzeigeformatierung. Auswahl, Reihenfolge und Argumente bleiben unverändert.

Regression: `CreatePlanAsync_KeepingArchivePrimary_ReportsOnlyRemovedNormalAudio` prüft, dass eine ersetzte deutsche Spur angezeigt, eine behaltene englische Spur dagegen nicht als entfernt dargestellt wird. Der vorhandene Test `CreatePlanAsync_KeepingArchivePrimary_DropsArchiveAudioLanguages_ThatFreshMultiAudioSourceAlreadyCovers` prüft zusätzlich die Anzeige beider tatsächlich entfernten Archiv-Audios mit Begründung.

### MC-O3, P3: Header-Vorschau zeigte nur Namen, auch bei reinen Flag-Edits

Die Vorschau formatierte stets `CurrentTrackName -> ExpectedTrackName` statt `ValueEdits`. Dadurch stand bei reinen Flag-Änderungen derselbe Name auf beiden Seiten.

Fix: Die Vorschau verwendet die bereits vorhandene gemeinsame Formatierung `ArchiveHeaderNormalizationService.BuildHeaderChangeNotes`. Tatsächliche Sprach-/Flag-Wertänderungen, Container-Titel und alte namensbasierte Operationen werden konsistent dargestellt. Ausführungsargumente bleiben unverändert.

Regression: `BuildArguments_UsesMkvPropEditForDirectTrackHeaderValueEdits` ist jetzt eine Theorie für Accessibility-Flag, Default-Flag und Sprache; sie prüft Argumente und passende Vorschau ohne scheinbaren Namenswechsel. Die vorhandenen Tests für namensbasierte Operationen und Container-Titel prüfen auch deren unveränderte Darstellung.

## Korrekturen Aus Dem Zentralen Integrationlauf

- **Hinweis, Subtitle-Test zu breit:** `CreatePlanAsync_PrimaryUpgradeWithCompleteSubtitleReplacementDoesNotReuseArchiveInput` verbot global `--subtitle-tracks`. MC-05 benötigt diese Option ausdrücklich für die externe SRT-Spur 0. Der Test prüft jetzt genau diese einzige externe Spurwahl, die leere Untertitelauswahl der frischen Primärquelle, den ersetzten externen Untertitel und das Fehlen des Archivpfads in den Eingabereferenzen. Der Archivpfad kommt ausschließlich als `--output` vor; keine Arbeitskopie wird benötigt. Damit bleibt der Untertitel-Upgrade-Fix aus `323634f` ausdrücklich abgesichert.
- **Hinweis, Attachment-Testaufbau inkonsistent:** `CreatePlanAsync_ReplacingArchivePrimary_UsageSummary_ShowsRemovedArchiveParts_WithReasons` scheiterte bei `summary.Attachments.HasRemoved`, nicht bei der Audioanzeige. Die Testdatei hieß manuell, war aber nur in `AttachmentPaths` und weder als Videobegleiter noch in `ManualAttachmentPaths` angegeben. MC-10 behält in dieser Situation zu Recht den Archivtext, weil kein tatsächlich geplanter Ersatz vorhanden ist. Der Request markiert den beabsichtigten manuellen Anhang jetzt ausdrücklich; zusätzliche Assertions prüfen seine Aufnahme, das Entfernen von `bestehend.txt` und dessen Anzeige. Der Gegenfall ohne verwendbaren Ersatz bleibt in `CreatePlanAsync_UnusedFreshTextDoesNotRemoveOnlyArchiveText` abgedeckt. Keine Abschwächung des Schutzfixes.

## Übergreifende Findings Und Restliche Risiken

- **P1, vom Parent übernommen:** direkte Ausgabe gegen finale Ziel-MKV und Entfernen der Arbeitskopie nach Fehler/Abbruch. Der Parent bearbeitet `MuxExecutionService`, CopyService, Workflow und FakeMkvMerge für atomare Veröffentlichung. Diese Dateien wurden hier nicht editiert. Die Modul-Schnittstelle bleibt kompatibel; neue Ausführungstests brauchen die Fake-Unterstützung für temporäre Outputpfade.
- **P2, extern behoben laut Parent:** Exceptions aus externen `onOutput`-Callbacks wurden im Prozessdienst abgefangen und durch eine zentrale Regression abgesichert; Prozessschutz-Tests einschließlich dieses Falls sind laut Parent grün. Keine Bearbeitung dieser fremden Datei durch diesen Agenten.
- **Hinweis:** Direkte mkvpropedit-Header-Edits bleiben In-place-Änderungen; keine transaktionale Rollback-Garantie, keine Testausführung mit Kill während eines echten Header-Edits.
- **Hinweis:** `IMediaDurationProbe` hat weiterhin keinen CancellationToken. Synchrone ffprobe-/COM-Dauerabfragen im Kandidatenbau können nach Rückgabe eines abgebrochenen STA-Tasks noch laufen; Windows-COM hat hier kein hartes Timeout. Keine API-weite Änderung über fremde Aufrufer hinweg.
- **Hinweis:** Identify hat ohne Aufrufer-Abbruch keinen eigenen Prozess-Timeout. Der Cancellation-Drain des Identify-Runners wurde nicht neu strukturiert; Rennen beim Beenden/Disposen bleiben durch den zentralen Prozess-Testlauf zu prüfen.
- **Hinweis:** Probe-Caches beruhen auf Dateigröße und LastWriteTime, nicht auf Dateihash; Invalidate-versus-inflight-Probe und identische Dateistatistik sind nicht gesondert abgesichert. Vorbereitete Directory-Kontexte sind Snapshots, keine Live-Watcher.
- **Hinweis:** Die bewusst deutschzentrierte Sprachfallback-Regel, externe Untertitel standardmäßig Deutsch/HI, H.264-vor-H.265-Präferenz und normale Audioauswahl mit AD-only-Fallback wurden nicht grundsätzlich geändert. Unbekannte Sprachen und AD-only-Container bleiben fachliche Grenzen dieser Regeln.
- **Hinweis:** Video-Upgrades werden anhand der bestehenden Sprach-/Codec-/Auflösungsregeln entschieden. Laufzeitwarnungen ersetzen keinen Nachweis identischer Schnittfassungen oder Synchronität; keine Medienanalyse an Realdateien.
- **Hinweis:** Quellengruppierung verwendet heuristische Serien-/Titel-/Code-/Dauerkriterien. Keine allgemeine transitive Gruppierungs-Neuimplementierung, keine unbelegten Massenrefactorings.

## Geprüfte Dateien

Statische Durchsicht der folgenden Produktionsdateien; besonders tiefe Pfadverfolgung für Auswahl, Archiv-Reuse, Argumente, Probe-Cancellation und Ausgabe. Keine Behauptung vollständiger formaler Verifikation aller Kombinationen.

- `Modules/SeriesEpisodeMux/Models.cs`
- `Modules/SeriesEpisodeMux/EpisodeUsageSummary.cs`
- `Modules/SeriesEpisodeMux/SubtitleSourceSelection.cs`
- `Modules/SeriesEpisodeMux/SeriesEpisodeMuxPlan.cs`
- `Modules/SeriesEpisodeMux/SeriesEpisodeMuxArgumentBuilder.cs`
- `Modules/SeriesEpisodeMux/SeriesEpisodeMuxHeaderEditArgumentBuilder.cs`
- `Modules/SeriesEpisodeMux/SeriesEpisodeMuxPresentationBuilder.cs`
- `Modules/SeriesEpisodeMux/SeriesEpisodeMuxPlanner.cs`
- `Modules/SeriesEpisodeMux/SeriesEpisodeMuxPlanner.CandidateSelection.cs`
- `Modules/SeriesEpisodeMux/SeriesEpisodeMuxPlanner.Detection.cs`
- `Modules/SeriesEpisodeMux/SeriesEpisodeMuxPlanner.DirectoryContext.cs`
- `Modules/SeriesEpisodeMux/SeriesEpisodeMuxPlanner.Parsing.cs`
- `Modules/SeriesEpisodeMux/SeriesEpisodeMuxPlanner.PlanCreation.cs`
- `Modules/SeriesEpisodeMux/SeriesEpisodeMuxService.cs`
- `Services/SeriesArchiveService.cs`
- `Services/SeriesArchiveService.Preparation.cs`
- `Services/SeriesArchiveService.AttachmentReuse.cs`
- `Services/SeriesArchiveService.UsageReporting.cs`
- `Services/ArchiveHeaderNormalizationService.cs`
- `Services/AudioTrackClassifier.cs`
- `Services/MediaCodecPreferenceHelper.cs`
- `Services/MediaLanguageHelper.cs`
- `Services/SeriesOriginalLanguageRules.cs`
- `Services/MkvMergeIdentifyParser.cs`
- `Services/MkvMergeIdentifyRunner.cs`
- `Services/MkvMergeOutputParser.cs`
- `Services/MkvMergeProbeService.cs`
- `Services/FfprobeDurationProbe.cs`
- `Services/PreferredMediaDurationProbe.cs`
- `Services/WindowsMediaDurationProbe.cs`
- `Services/IMediaDurationProbe.cs`
- `Services/MediaFileHealth.cs`

Relevante vorhandene Unit-/Integrationstests wurden gelesen und gezielt ergänzt; nicht jede unveränderte Testzeile wurde einzeln auditiert. Fremde Ausführungsdateien, Fake-Helfer und Testprojektdateien wurden nur zur Schnittstellenprüfung gelesen.

## Eigene Geänderte Dateien

- `Modules/SeriesEpisodeMux/Models.cs`
- `Modules/SeriesEpisodeMux/SeriesEpisodeMuxPresentationBuilder.cs`
- `Modules/SeriesEpisodeMux/SeriesEpisodeMuxArgumentBuilder.cs`
- `Modules/SeriesEpisodeMux/SeriesEpisodeMuxPlanner.Detection.cs`
- `Modules/SeriesEpisodeMux/SeriesEpisodeMuxPlanner.PlanCreation.cs`
- `Modules/SeriesEpisodeMux/SeriesEpisodeMuxService.cs`
- `Services/ArchiveHeaderNormalizationService.cs`
- `Services/AudioTrackClassifier.cs`
- `Services/FfprobeDurationProbe.cs`
- `Services/MediaLanguageHelper.cs`
- `Services/MkvMergeIdentifyParser.cs`
- `Services/MkvMergeIdentifyRunner.cs`
- `Services/MkvMergeProbeService.cs`
- `Services/SeriesArchiveService.AttachmentReuse.cs`
- `Services/SeriesArchiveService.Preparation.cs`
- `Services/SeriesArchiveService.UsageReporting.cs`
- `Services/SeriesArchiveService.cs`
- `Services/WindowsMediaDurationProbe.cs`
- `MkvToolnixAutomatisierung.Tests/Modules/SeriesEpisodeMuxArgumentBuilderTests.cs`
- `MkvToolnixAutomatisierung.Tests/Modules/SeriesEpisodeMuxPlanTests.cs`
- `MkvToolnixAutomatisierung.Tests/Services/ArchiveHeaderNormalizationServiceTests.cs`
- `MkvToolnixAutomatisierung.Tests/Services/AudioTrackClassifierTests.cs`
- `MkvToolnixAutomatisierung.Tests/Services/FfprobeDurationProbeTests.cs`
- `MkvToolnixAutomatisierung.Tests/Services/MediaLanguageHelperTests.cs`
- `MkvToolnixAutomatisierung.Tests/Services/MkvMergeIdentifyParserTests.cs`
- `MkvToolnixAutomatisierung.Tests/Services/MkvMergeIdentifyRunnerTests.cs` (neu)
- `MkvToolnixAutomatisierung.Tests/Services/SeriesArchiveServiceTests.cs`
- `MkvToolnixAutomatisierung.Tests/Services/WindowsMediaDurationProbeTests.cs` (neu)
- `MkvToolnixAutomatisierung.IntegrationTests/Modules/SeriesEpisodeMuxServiceIntegrationTests.ArchiveVideo.cs`
- `MkvToolnixAutomatisierung.IntegrationTests/Modules/SeriesEpisodeMuxServiceIntegrationTests.Subtitles.cs`
- `MkvToolnixAutomatisierung.IntegrationTests/Modules/SeriesEpisodeMuxServiceIntegrationTests.Review.cs` (neu)
- `MkvToolnixAutomatisierung.IntegrationTests/Services/MkvMergeProbeServiceIntegrationTests.cs`
- `docs/reviews/2026-09-22/mux-core.md` (neu)

## Benötigte Zentrale Testfilter

Gezielter Unit-Wiederholungslauf für den Nachtrag:

```text
FullyQualifiedName~MkvMergeIdentifyParserTests|FullyQualifiedName~ArchiveHeaderNormalizationServiceTests|FullyQualifiedName~SeriesEpisodeMuxPlanTests
```

Gezielter Integration-Wiederholungslauf für die beiden zentral gemeldeten Fehler und die neuen MC-O1/O2-Regressionen:

```text
FullyQualifiedName~CreatePlanAsync_PrimaryUpgradeWithCompleteSubtitleReplacementDoesNotReuseArchiveInput|FullyQualifiedName~CreatePlanAsync_ReplacingArchivePrimary_UsageSummary_ShowsRemovedArchiveParts_WithReasons|FullyQualifiedName~CreatePlanAsync_KeepingArchivePrimary_CorrectsRawLanguageWithoutChangingSlots|FullyQualifiedName~CreatePlanAsync_KeepingArchivePrimary_ReportsOnlyRemovedNormalAudio|FullyQualifiedName~CreatePlanAsync_KeepingArchivePrimary_DropsArchiveAudioLanguages_ThatFreshMultiAudioSourceAlreadyCovers
```

Für die thematische Gesamtintegration weiterhin die folgenden vollständigen Bereichsfilter verwenden und Archivpflege-Tests wegen der gemeinsamen Metadaten-/Header-Normalisierung einbeziehen.

Unit-Projekt: `MkvToolnixAutomatisierung.Tests/MkvToolnixAutomatisierung.Tests.csproj`.

```text
FullyQualifiedName~SeriesEpisodeMux|FullyQualifiedName~SeriesArchiveServiceTests|FullyQualifiedName~ArchiveHeaderNormalizationServiceTests|FullyQualifiedName~AudioTrackClassifierTests|FullyQualifiedName~MediaLanguageHelperTests|FullyQualifiedName~MkvMergeIdentifyParserTests|FullyQualifiedName~MkvMergeIdentifyRunnerTests|FullyQualifiedName~MkvMergeOutputParserTests|FullyQualifiedName~FfprobeDurationProbeTests|FullyQualifiedName~WindowsMediaDurationProbeTests
```

Integration-Projekt: `MkvToolnixAutomatisierung.IntegrationTests/MkvToolnixAutomatisierung.IntegrationTests.csproj`.

```text
FullyQualifiedName~MkvToolnixAutomatisierung.IntegrationTests.Modules.SeriesEpisodeMuxServiceIntegrationTests|FullyQualifiedName~MkvToolnixAutomatisierung.IntegrationTests.Services.MkvMergeProbeServiceIntegrationTests
```

Der Integrationsfilter umfasst bewusst alle bestehenden Partial-Tests einschließlich Untertitel-Upgrade, Attachments, FreshAudio, ArchiveVideo und AD-only. Zusätzlich zentral die Prozess-/Copy-/Workflow-Tests des Parents und die Archivpflege-Tests der Nachbarbereiche ausführen, da sie gemeinsame Metadaten- und Sprachregeln verwenden.

## Nicht Abgedeckt

- Keine eigene Build-/Testausführung. Die oben genannten zentralen Ergebnisse beziehen sich auf den Stand vor dem Nachtrag; dessen Build-, Unit- und Integrationsergebnis muss der Parent ergänzen.
- Kein WPF-UI-Lauf, kein echter mkvmerge/mkvpropedit/ffprobe-Mux, keine echte COM-Registrierung oder Windows-Media-Player-Messung.
- Keine Realarchive, keine großen Dateien, kein Netzlaufwerk, keine vollständige Performance-/Race-/Fault-Injection-Matrix.
- Keine fremden Services editiert, keine paket- oder frameworkweite Änderung, keine Neubewertung sämtlicher redaktioneller Serienausnahmen.
