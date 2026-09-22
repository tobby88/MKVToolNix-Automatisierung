# Emby-Review, 2026-09-22

## Stand und Grenzen

- Ausgang: `master`, `323634f` (`Replace matching archive subtitles only when upgrading the primary source`). Der bestehende Untertitel-Fix wurde nicht verändert.
- Schreibbereich: `Services/Emby/*`, `Services/BatchOutputMetadataReport.cs`, `Services/BatchOutputMetadataEntryFactory.cs`, `ViewModels/Modules/EmbySync*`, `Views/EmbySync*`, die fünf dedizierten Emby-Testdateien und dieser Bericht.
- Andere Agenten arbeiten im selben Worktree. Deren Dateien wurden nicht editiert, zurückgesetzt, gestaged oder committed.
- Keine Paketupdates, kein eigener Build/Test/Format-Aufruf, kein Commit/Stage/Push. Keine echten NFOs, Archive oder Nutzerdaten wurden bearbeitet. Neue Tests arbeiten ausschließlich mit GUID-Tempverzeichnissen bzw. dem vorhandenen `PortableStorage`-Fixture und HTTP-Fakes.
- Alle unten genannten Tests sind hinzugefügte Regressionstests, nicht als lokal erfolgreich ausgeführt zu verstehen.

## Verifikation und Integrationsstatus

- `git diff --check` für die zugewiesenen Produktions- und Testdateien: sauber.
- `Views/EmbySyncView.xaml` mit `XmlDocument.Load` eingelesen: syntaktisch gültiges XML. Das ersetzt weder WPF-Kompilierung noch visuelle QA.
- Der Parent meldete beim zentralen Build `CS9007` im neuen JSON-Raw-String. Korrigiert durch `$$$` und entsprechend dreifache Interpolationsklammern.
- Der Parent meldete anschließend einen fehlgeschlagenen Test `ChangedEmbyConnection_DiscardsCachedServerItemIds`: Der Test schrieb über einen zweiten, unabhängig gecachten `AppSettingsStore`. Der Testhelper akzeptiert jetzt die gemeinsam injizierte Store-Instanz; Serverwechsel- und Cancellation-Test verwenden sie. Keine Änderung am allgemeinen Settings-Store.
- Extern gemeldeter Stand vor dem abschließenden Nachtrag vom 2026-09-22: Der Parent meldet den vollständigen zentralen Unit-/WPF-Lauf mit **1081/1081 bestandenen Tests**, einschließlich `ChangedEmbyConnection_DiscardsCachedServerItemIds`. Dieser Erfolg wurde vom Parent übermittelt und nicht durch einen eigenen Build oder Testlauf verifiziert. Die bisherigen zentral gemeldeten Compiler-/Testprobleme sind damit für diesen Stand behoben.
- Danach ausdrücklich beauftragter enger Nachtrag: R02 (Refresh-Wiederholung), R08 (Asset-Zähler), globale NFO-Sperre und Implementierung des vom Parent eingeführten `IModuleInteractionState`. Die zugehörigen neuen/erweiterten Tests sind noch nicht zentral als erfolgreich gemeldet. Statische Kontrolle und `git diff --check` sind sauber; kein eigener Compiler-/Testlauf. Der Stand ist zur serialisierten zentralen Prüfung bereit.

## Behobene Findings

Keine belegte P1-Einstufung in diesem Teilreview. Die folgenden P2/P3-Befunde sind auf konkrete Kontrollflüsse bzw. Eingabedaten zurückzuführen.

### E01, P2: Unbekannte Report-Metadaten gingen beim Speichern verloren

- Ort: `Services/BatchOutputMetadataReport.cs`, alle fünf JSON-Modelltypen; `EmbyMetadataSyncService.MarkSingleOutputReportDone`.
- Reproduktion: Einen kompatiblen Report mit zusätzlichen Root-, Item-, Provider-, TVDB- oder Review-Feldern laden und nach `partial`/`done` schreiben. Der typisierte Roundtrip verwarf vorher unbekannte Felder.
- Fix: `JsonExtensionData` auf allen Ebenen; auch beim Ersetzen der bekannten Review-Werte bleibt die vorhandene Review-Erweiterung erhalten. Ursprüngliche Mux-Daten werden nicht durch Review-Daten ersetzt.
- Test: `ReportProgress_PreservesUnknownFieldsAtEveryLevelIncludingReview`, einschließlich eines erneuten bytegleichen No-op im `done`-Ordner.

### E02, P2: Inkompatible oder strukturell ungültige Reports wurden nicht kontrolliert abgewiesen

- Ort: `BatchOutputMetadataReportJson.Deserialize`.
- Reproduktion: `schemaVersion: 2`, `schemaVersion: 0`, `items: null` oder `items: [null]`. Vorher war ein inkompatibler Roundtrip bzw. eine NullReferenceException möglich.
- Fix: Nur Schema 1 mit nichtnull Eintragsliste und nichtnull Einträgen zulassen; klare `InvalidDataException` vor jeglichem Schreiben/Verschieben. Fehlende Schemaangabe behält den bisherigen Default 1.
- Test: `InvalidReport_IsRejectedWithoutRewritingOrMoving` mit vier Fällen.

### E03, P2: XML-Roundtrip konnte fremde Textinhalte verändern

- Ort: `EmbyNfoProviderIdService.SaveAtomically`.
- Reproduktion: Unbeteiligtes `<plot>First&#xD;Second</plot>`; der Standard-XML-Writer normalisiert CR-Zeichen und verändert damit den beim erneuten Lesen sichtbaren Text.
- Fix: Expliziter XML-Writer mit `NewLineHandling.Entitize`, ohne zusätzliche Einrückung. Kommentare, CDATA, Namespaced-Erweiterungen, fremde Provider und Attributwerte bleiben semantisch erhalten. Atomarer Austausch bleibt bestehen.
- Test: `Updates_PreserveUnrelatedXmlIncludingCarriageReturns` für Provider- und Titelupdates; prüft auch Tempdatei-Cleanup.

### E04, P2: Nicht unterstützte XML-Wurzeln konnten irreführend aktualisiert werden

- Ort: `EmbyNfoProviderIdService.LoadEpisodeDocument`, verwendet von Lesen und beiden Schreibpfaden.
- Reproduktion: Eine namespaced Episoden-NFO wurde nicht erkannt; stattdessen konnten neue unqualifizierte Provider-/Titelfelder neben den eigentlichen Feldern entstehen. Auch fremde Wurzeltypen wurden behandelt wie Episoden.
- Fix: Nur `episodedetails` ohne Default-Namespace akzeptieren. DTD-Verarbeitung explizit verbieten; XML-Resolver deaktivieren. Fehler werden als NFO-Hinweis/fehlgeschlagenes Update gemeldet, Originalbytes bleiben erhalten.
- Test: `UnsupportedOrMalformedNfo_IsReportedAndNeverChanged` für fremde Wurzel, Namespace, DTD und defektes XML.

### E05, P2: Mehrere lockedfields-Container wurden inkonsistent gelesen/geändert

- Ort: `EmbyNfoProviderIdService.IsLockedField`, `SetLockedFields`.
- Reproduktion: Sperren im zweiten Container wurden beim Lesen ignoriert; Entsperren im ersten Container konnte die tatsächlich weiter vorhandene Sperre im zweiten Container stehenlassen.
- Fix: Alle vorhandenen Sperren zusammenführen, nur explizit adressierte Sperren ändern, Duplikatcontainer konsolidieren. Fremde Felder wie `Actors` und `Studios` bleiben erhalten.
- Test: `UpdateTextFields_MergesDuplicateLocksWithoutDroppingUnrelatedFields`, inklusive No-op-Wiederholung.

### E06, P3: Provider-Duplikate und Whitespace erzeugten unnötige bzw. falsche Ergebnisse

- Ort: `EmbyNfoProviderIdService.ReadProviderId`, `SetUniqueId`, `UpdateProviderIds`, `UpdateTextFields`.
- Reproduktion: Leere Default-Unique-ID verdeckte eine weitere gefüllte ID; beim Schreiben wurde nicht das beim Lesen bevorzugte Default-Element behalten. IDs mit Rand-Whitespace bzw. Titel mit bedeutungstragendem Rand-Whitespace erzeugten wiederholte Updates.
- Fix: Nichtleere Providerwerte lesen, Default-Element samt Attributen als kanonisch erhalten, Provider-Eingaben trimmen, Titelwerte für Schreibentscheidungen exakt vergleichen.
- Tests: `ProviderIds_IgnoreEmptyDefaultsAndPreserveCanonicalAttributes`, `UpdateTextFields_WithUnchangedSignificantWhitespace_IsANoOp`.

### E07, P2: HTTP-Timeout endete nach den Headern statt nach dem Response-Body

- Ort: `EmbyClient.SendAsync`.
- Reproduktion: Server liefert Header, lässt aber JSON- oder Fehlerbody offen. Mit `ResponseHeadersRead` begrenzte `HttpClient.Timeout` den anschließenden Stream-Read nicht mehr; Workflows konnten unbegrenzt busy bleiben.
- Fix: `ResponseContentRead` umfasst den vollständigen Body im HTTP-Timeout. Benutzer-Cancellation bleibt `OperationCanceledException`; ausschließlich ein nicht vom Aufrufer ausgelöster Abbruch wird zur freundlichen Timeout-Meldung.
- Tests: `GetSystemInfoAsync_TimesOutWhileReadingResponseBody`, `GetSystemInfoAsync_PropagatesUserCancellationWhileReadingBody`, jeweils ausschließlich mit Fake-Content.

### E08, P2: Pfadlookup konnte das falsche Item für einen Refresh wählen

- Ort: `EmbyClient.FindItemByPathAsync`, `AreSameEmbyPath`, `AreEquivalentEmbyPath`.
- Reproduktion: Ein früher Suffix-Treffer gewann vor einem späteren exakten Treffer; mehrere gleich plausible Treffer wurden nicht als mehrdeutig behandelt. Außerdem konnten zwei verschiedene Linux-Pfade oder verschiedene Windows-Laufwerke allein über den Suffix als gleich gelten.
- Fix: Exakte Treffer haben Vorrang, danach nur genau ein eindeutiger Cross-Platform-Kandidat. Keine Suffix-Heuristik innerhalb derselben Pfadart; mindestens drei gemeinsame Suffixsegmente. Zwei POSIX-Pfade werden case-sensitive verglichen. URI-Escaping wird beim exakten SMB-Vergleich normalisiert.
- Tests: `FindItemByPathAsync_PrefersExactMatchOverEarlierSuffixMatch`, `FindItemByPathAsync_RejectsAmbiguousMatches`, `FindItemByPathAsync_DoesNotGuessSamePlatformOrShallowPaths`, `FindItemByPathAsync_MatchesEscapedAndUnescapedSmbPaths`.

### E09, P2/P3: Mehrdeutige Library-Wurzeln und escapte SMB-Wurzeln

- Ort: `EmbyMetadataSyncService.FindSeriesLibraryAsync`, `GetComparablePathSegments`, `CombineLibraryLocationWithRelativePath`.
- Reproduktion: Zwei Libraries mit exakt derselben Wurzel führten zum beliebigen ersten Scan-Ziel. `smb://.../Meine%20Serien` passte nicht zur lokalen Wurzel `Meine Serien`.
- Fix: Mehrere unterschiedliche Library-IDs bei exaktem Root als mehrdeutig behandeln; URI-Pfadsegmente für die Ausrichtung decodieren, hinzugefügte URI-Segmente korrekt escapen.
- Tests: `FindSeriesLibraryAsync_DoesNotChooseBetweenDuplicateExactLibraryRoots`, `FindItemByPathAsync_TranslatesEscapedSmbLibraryRoots`.
- Bestehende Regel bleibt: Kann keine Library zugeordnet werden, ist der globale Scan-Fallback im Status/Protokoll explizit als nicht bibliotheksscharf gekennzeichnet.

### E10, P2: Serien-ID wurde als TVDB-Episoden-ID verwendet

- Ort: `EmbyFileAnalysis.EmbyProviderIdsFromItem` und die Emby-Providerübernahme in `EmbySyncItemViewModel`.
- Reproduktion: Ein Emby-Item hat nur `TvdbSeries`. Dieser Wert wurde als Ersatz für die fehlende Episoden-ID übernommen und konnte in die Episoden-NFO geschrieben werden.
- Fix: Als Episoden-ID ausschließlich `Tvdb` verwenden; kein `TvdbSeries`-Fallback.
- Tests: `EffectiveProviderIds_DoesNotUseSeriesIdAsEpisodeId`, `ApplyEmbyItem_DoesNotTreatTvdbSeriesAsEpisodeProviderId`.

### E11, P2: Sichtbare automatische IMDb-ID wurde bei neuem Kandidaten fälschlich bestätigt

- Ort: `EmbySyncItemViewModel.BuildImdbCandidateValues`, `ApplyAutomaticImdbCandidate`.
- Reproduktion: TVDB A liefert automatisch IMDb A; danach wird TVDB B gewählt, die IMDb B liefert. War IMDb A in keiner Report-/NFO-/Serverquelle vorhanden, galt B als konfliktfrei, während sichtbar A stehenblieb und als bestätigt galt.
- Fix: Auch der aktuell sichtbare Wert ist eine Konfliktquelle. Entsprechend wird ein Konflikt geöffnet statt eine andere sichtbare ID zu bestätigen. Auch TVDB-Kandidaten berücksichtigen den sichtbaren Wert.
- Test: `AutomaticImdbCandidate_DoesNotApproveDifferentVisibleValueAfterTvdbChange`.

### E12, P2: Manuelle Review-Entscheidungen wurden falsch gelöscht bzw. weitervererbt

- Ort: `EmbySyncItemViewModel.ApplyTvdbImdbCandidate`, `SetTvdbId`, `SetImdbId`.
- Reproduktion: Eine fehlende TVDB-Verknüpfung setzte eine vorher manuell bestätigte IMDb-ID oder bewusste IMDb-Absage wieder auf offen. Umgekehrt blieb nach Leeren eines manuell geprüften Felds die alte manuelle Freigabe für später neu eingelesene Werte erhalten.
- Fix: Manuelle IMDb-Entscheidungen haben auch bei fehlendem Link Vorrang; Leeren eines Feldes entzieht dessen vorherige manuelle Freigabe. Eine explizite Absage setzt ihre eigene Freigabe weiterhin bewusst neu.
- Tests: `MissingTvdbLink_PreservesManualImdbDecision`, `ClearingManuallyReviewedIds_RevokesApprovalBeforeSourcesAreReadAgain`.

### E13, P2: Asynchrone Pflichtchecks ließen konkurrierende Workflows zu

- Ort: Commandverdrahtung und `RunBusyAsync` in `EmbySyncViewModel`.
- Reproduktion: Während eines TVDB-/IMDb-Abgleichs konnten Reportauswahl, Scan oder Schreiben ausgeführt werden. Die weiterlaufende Prüfung arbeitete dann mit alten Zeilen; Einzelprüfungen verwendeten nach einem Await erneut `SelectedItem`.
- Fix: Import, Einzel-TVDB-Prüfung und Pflichtchecks verwenden denselben modulweiten Busy-Schutz. Verschachtelte Busy-Bereiche stellen den vorherigen Zustand wieder her. Die Einzelprüfung hält die ursprünglich gewählte Zeile fest.
- Test: `ProviderReview_DisablesCompetingWorkflowsUntilFinished`; bestehende Scan-/Grid-Interaktionstests sind im extern gemeldeten vollständigen Unit-/WPF-Lauf enthalten.

### E14, P2: Fehlgeschlagener Import hinterließ alte Zeilen unter neuen Reportpfaden

- Ort: `EmbySyncViewModel.SelectReportAsync`, `ImportSelectedReportsAsync`.
- Reproduktion: Nach Report A wird ein ungültiger Report B ausgewählt. `_reportPaths` zeigte bereits auf B, obwohl das Laden scheiterte und die Zeilen aus A erhalten blieben.
- Fix: Reportauswahl und Zeilen werden erst nach erfolgreichem Laden aller ausgewählten Reports gemeinsam übernommen.
- Test: `FailedReportImport_KeepsPreviousRowsAndTheirReportPaths`.

### E15, P2: Serverwechsel behielt servergebundene Item-IDs

- Ort: `EmbySyncViewModel.HandleGlobalSettingsChanged`, `EmbySyncItemViewModel.ClearEmbyLookup`.
- Reproduktion: Nach Umstellung auf einen anderen Server konnte dessen Refresh-Endpunkt mit einer Item-ID des vorherigen Servers aufgerufen werden.
- Fix: Bei geänderter Serveradresse oder geändertem API-Key werden servergebundene Item-IDs und Provider-Caches verworfen. Die ausgewählten lokalen Providerwerte bleiben erhalten.
- Test: `ChangedEmbyConnection_DiscardsCachedServerItemIds`, mit derselben Store-Instanz für Settings-Dialog und Modul wie in der Anwendung.
- Grenze: Einstellungen während eines bereits laufenden Workflows siehe R03.

### E16, P2: Verwaiste NFO konnte trotz inzwischen fehlender MKV aktualisiert werden

- Ort: `EmbySyncViewModel.RunSyncAsync`.
- Reproduktion: MKV nach der Analyse entfernt, gleichnamige NFO noch vorhanden. Ein späterer Schreibschritt konnte die NFO bearbeiten oder den Eintrag als abgeschlossen markieren.
- Fix: Vor dem Schreibschritt Medienexistenz erneut prüfen; bei fehlender MKV NFO unverändert lassen, Zeile als fehlend markieren und Report nicht erfolgreich abschließen.
- Test: `RunSyncCommand_DoesNotModifyOrphanedNfoWhenMediaWasRemoved`.

### E17, P2: Scan-Endstatus wurde zu früh oder falsch bestätigt

- Ort: `TryGetActiveLibraryScanState`, `IsCompletedLibraryScanStatus`, `WaitForSeriesLibraryScanAsync`.
- Reproduktion: `99.9` wurde zu `100` gerundet; `Incomplete` enthielt den Teilstring `complete`; `Stopped` galt als Erfolg, `Failed` dagegen dauerhaft als aktiv. Verschwundener Status nach aktivem Fortschritt galt ohne weitere Evidenz als abgeschlossen.
- Fix: Aktiven Rohfortschritt unter 100 weiter als aktiv behandeln, bekannte Statusnamen exakt vergleichen, Fehler/Abbruch nicht als erfolgreichen Abschluss melden. Verschwindende Statusdaten ohne Abschlussbeleg bleiben unbestätigt.
- Test: `ScanState_DoesNotRoundRunningProgressToCompletionOrUseStatusSubstrings` mit sieben Fällen. Die komplette Polling-Zustandsmaschine ist nicht neu isoliert getestet; vorhandene WPF-Scan-/Cancellation-Tests bleiben wichtig.

### E18, P2: Item-Wartebudget begrenzte laufende Requests nicht

- Ort: `EmbySyncViewModel.ResolveEmbyItemsWithinBudgetAsync` und `EmbyMetadataSyncService.AnalyzeFileAsync`.
- Reproduktion: Das Budget wurde nur vor einer kompletten Runde über alle offenen Dateien geprüft. Viele langsame HTTP-Aufrufe konnten das konfigurierte Budget erheblich überschreiten. Lokal-only Analyse ignorierte bereits abgebrochene Tokens.
- Fix: Verknüpfter Budget-Token mit `CancelAfter` für jeden Lookup und die Wartepausen; Budgetablauf wird von Benutzerabbruch unterschieden. Lokale Analyse prüft Cancellation vor und nach dem synchronen Lesen.
- Tests: `AnalyzeFileAsync_ObservesCancellationEvenForLocalOnlyAnalysis`, `CancelScanCommand_CancelsInFlightHttpWorkAndRestoresInteractivity`.
- Grenze: Der exakte Zeitablauf des 5-600-Sekunden-Budgets hat keinen eigenen Timing-Test. Cancellation eines schon laufenden synchronen Dateisystemzugriffs ist damit nicht möglich.

### E19, P2/P3: Dateizugriffe blockierten die WPF-UI; Reportfehler waren schlecht sichtbar

- Ort: `EmbySyncViewModel` Import, lokale Analyse, NFO-Update, Reportabschluss; `EmbyMetadataSyncService.MarkSingleOutputReportDone`; `Views/EmbySyncView.xaml`.
- Reproduktion: Synchrone Report-/NFO-Zugriffe, insbesondere auf Netzlaufwerken, liefen auf dem UI-Thread. Ein verschwundener Report wurde beim Abschluss kommentarlos ignoriert. Nach Ende eines Workflows war der Status nur noch im eingeklappten Protokoll bzw. einem ausgeblendeten Fortschrittsbereich sichtbar.
- Fix: Datei-/Analysearbeit auf Hintergrundtasks; gebundene Zeilen und Status werden weiterhin nach Await im aufrufenden UI-Kontext aktualisiert. Providerwerte werden vor dem Hintergrund-Schreiben erfasst. Fehlender Report wird als Fehler gemeldet, Speichern nutzt atomaren Ersatz statt Überschreib-Move. Reportfehler erscheinen im sichtbaren Abschlussstatus; `StatusText` bleibt im View sichtbar.
- Tests: `MarkOutputReportsDone_ReportsDisappearedSource`; Import-/Sync-Regressionen und vorhandene WPF-Interaktionstests. Kein neuer Dispatcher-Last-/Netzwerkshare-Test.

### E20, P3: Validierung, Benachrichtigungen und HTTP-Diagnostik waren inkonsistent

- Ort: `EmbySyncItemViewModel` ID-Validierung/PropertyChanged; `EmbyClient` JSON-/Settings-Prüfung/Providerzugriff; `Views/EmbySyncView.xaml`.
- Reproduktion: TVDB `0`, Integer-Überlauf und Unicode-Ziffern wurden als formal gültig angezeigt, obwohl der Episodenlookup sie nicht verarbeiten kann. Ein neu befülltes TVDB-Feld aktivierte bei nicht parsebarem Dateinamen den Suchbutton nicht verlässlich. Providerzugriff versprach Case-Insensitivity, war aber vom Dictionary-Comparer abhängig.
- Fix: Positive TVDB-Int32-ID, ASCII-IMDb-Ziffern, normalisierte Eingangswerte, Lookup-Benachrichtigungen. Providerzugriff ist unabhängig vom Dictionary-Comparer case-insensitive und trimmt. HTTP-Basisadressen mit Query/Fragment/Userinfo werden klar abgewiesen; Nicht-Objekt-JSON wird erklärbar abgewiesen, Null-Einträge werden übersprungen und nichtendlicher Fortschritt ignoriert. Validierungsfehler werden im ID-Tooltip angezeigt; Textfelder haben Automation-Namen.
- Tests: `TvdbId_RejectsValuesNotSupportedByEpisodeLookup`, `ImdbId_RejectsUnicodeDigits`, `TvdbId_NotifiesLookupAvailabilityForUnparseableFileName`, `GetProviderId_IsCaseInsensitiveForAnyDictionaryAndTrimsValues`, `GetSystemInfoAsync_RejectsNonObjectJson`, `GetSystemInfoAsync_RejectsNonBaseServerAddresses`.
- Kleine Perfkorrektur: CanExecute verwendet den über den globalen Settings-Callback aktualisierten Settings-Snapshot statt pro PropertyChanged erneut einen Settings-Klon zu laden. Kein Performancebenchmark.

### E21, P2: Alter Server-Snapshot verursachte wiederholte identische Refresh-Anforderungen (R02)

- Ort: `EmbySyncItemViewModel.HasCurrentRefreshRequest`, `RememberRefreshRequest`, `ClearRefreshRequest`; `EmbySyncViewModel.RunSyncAsync` und Abschlusszusammenfassung.
- Reproduktion: NFO und lokale Auswahl sind korrekt, der zuletzt gelesene Serverstand weicht ab. Zweimal nacheinander schreiben löste zweimal denselben Refresh aus, da der alte Server-Snapshot unverändert blieb. Auch nach einem echten NFO-Update trat dies beim Folgelauf auf.
- Fix: Erfolgreich angeforderte Item-ID, Providerwerte und beide ausdrücklichen Absagen werden getrennt vom beobachteten Serverstand gemerkt. Bei unveränderter NFO wird dieselbe Anforderung nicht erneut gesendet. Neue NFO-Änderungen, neue Serverdaten und Verbindungswechsel entwerten den Merker. Ein fehlgeschlagener Request setzt ihn nicht; nach einer neuen lokalen Änderung kann auch ein älterer Erfolg keinen nötigen Wiederholungsversuch verdecken.
- Wahrheitsgemäßer Status: Der Server-Snapshot bleibt unverändert; `HasKnownEmbyProviderIdMismatch` kann weiter wahr sein. Zeile und Zusammenfassung benennen den bereits angeforderten, serverseitig noch nicht bestätigten Refresh. `done` bedeutet weiterhin lokal abgeschlossen und erforderlichen Refresh erfolgreich angefordert, nicht beobachteten Serverabschluss.
- Tests: `RunSyncCommand_DoesNotRepeatAcceptedRefreshWithoutNfoChanges` für anfänglich aktuelle/geänderte NFO, bytegleichen zweiten Lauf ohne neuen Schreibzeitpunkt, neue Providerwerte sowie erneute NFO-Korrektur mit fehlgeschlagenem und danach wiederholtem Refresh. `RunSyncCommand_DoesNotMarkDone_WhenRefreshOnlyUpdateFails` um Wiederholung ergänzt. `RefreshRequest_TracksTargetValuesWithoutConfirmingServerState` und `RefreshRequest_IsInvalidatedByFreshServerStateOrConnectionChange` decken getrennte Absagen und alle Übernahmepfade ab.
- Grenze: Der Merker gilt nur für die geladene Sitzung. Eine neue Serverprüfung darf eine weiterhin beobachtete Abweichung erneut behandeln; kein permanenter Refresh-Cache, kein Polling auf Serverabschluss.

### E22, P3: Erfolgreich abgeschlossene Assets fehlten im Abschlusszähler (R08)

- Ort: `EmbySyncViewModel.RunSyncAsync`, `BuildRunSyncSummary`.
- Reproduktion: Ein Report enthält nur ein erkanntes Asset unter `trailers`/`backdrops` ohne Episoden-NFO. Der Report wurde korrekt abgeschlossen, die Zusammenfassung meldete aber `0 aktualisiert, 0 übersprungen`.
- Fix: Eigener Zähler für erfolgreich bearbeitete Assets ohne NFO-Sync; reine Asset-Läufe erhalten eine passende Abschlussmeldung, gemischte Läufe nennen Assets zusätzlich zu aktuellen/geänderten NFOs. Keine Änderung an Providerreview oder Abschlussentscheidung.
- Test: `RunSyncCommand_CountsCompletedAssetsAlongsideCurrentNfos`, rein und gemischt. Prüft Abschlusszähler, `done`-Report, fehlende NFO-Neuanlage und ausbleibenden Refresh.

### E23, P2: Globale NFO-Sperre blieb beim Lesen von Titelfeld-Sperren unberücksichtigt

- Ort: `EmbyNfoProviderIdService.ReadEpisodeMetadata`, `IsLockedField`, `UpdateTextFields`, `SetLockedFields`.
- Reproduktion: `<lockdata>true</lockdata>` ohne `Name`/`SortName` in `lockedfields` wurde als ungesperrter Titel/Sortiertitel gemeldet. Konsumenten konnten deshalb automatische Korrekturen vorschlagen, obwohl global gesperrt war. Ein einzelner Entsperrversuch konnte bisher Erfolg melden, obwohl die globale Sperre wirksam blieb.
- Fachliche Grundlage: Der öffentliche Emby-NFO-Parser setzt für `lockdata=true` die globale Eigenschaft `IsLocked`, getrennt von `LockedFields`. Quelle: [Emby BaseNfoParser](https://github.com/MediaBrowser/Emby/blob/master/MediaBrowser.XbmcMetadata/Parsers/BaseNfoParser.cs). Keine Behauptung, jede aktuelle Serverversion live geprüft zu haben.
- Fix: Globale Sperre beim Lesen beider Titelfelder berücksichtigen. Konservativ wird ein vorhandenes `true` ohne Beachtung der Großschreibung und mit Rand-Whitespace als Sperre gewertet. Individuelles Entsperren wird vor jeder Mutation mit klarem Hinweis abgewiesen; `lockdata` wird niemals auf `false` gesetzt oder gelöscht. Bewusste Textkorrekturen bleiben möglich, ohne globale oder fremde Feldsperren zu verändern. Keine Umwandlung der globalen Sperre in eine möglicherweise unvollständige Feldliste.
- Tests: `ReadEpisodeMetadata_GlobalLockAlsoLocksBothTitleFields` (sechs Fälle), `UpdateTextFields_RejectsIndividualUnlockUnderGlobalLockWithoutAnyWrite` (Titel, Sortiertitel, beide; Originalbytes und keine Tempdateien), `UpdateTextFields_PreservesGlobalAndUnrelatedLocksDuringExplicitTextEdit` (implizite/ausdrückliche Sperren; wiederholter No-op).
- Integrationsgrenze: Archivplanung erhält die wirksamen Sperrwerte über den bestehenden Lesevertrag. Archivdateien/Tests wurden hier nicht editiert; deren automatische Planung und kombinierte Schreibaktionen müssen zentral mitgeprüft werden. NFO-Schreibaktionen bleiben keine dateiübergreifende Transaktion.

### E24, Hinweis: Shell-Busy-Vertrag für globale Settings und Modulwahl angeschlossen (R03)

- Ort: Klassendeklaration `EmbySyncViewModel`.
- Fix: `IModuleInteractionState` statt bloßem `INotifyPropertyChanged` implementiert; bestehendes `IsInteractive` und dessen Benachrichtigungen erfüllen den vom Parent definierten Vertrag. Interface, Shell-Sperre und Modulwahl bleiben ausschließlich beim Parent.
- Test: `ProviderReview_DisablesCompetingWorkflowsUntilFinished` prüft jetzt über das Interface und zeichnet den Zustandswechsel `false`, `true` auf. Shell-Integration benötigt zentral zusätzlich `MainWindowViewModelTests`.

## Beibehaltene Regeln

- Eine fehlende ID ist keine ausdrückliche Absage. TVDB-/IMDb-Absagen bleiben getrennt und werden nur auf ausdrücklichen Wunsch aus der NFO entfernt.
- Existierende NFOs sind Voraussetzung; es werden keine fachlich unvollständigen NFOs erzeugt.
- Eine allein abweichende Provider-Default-Markierung verursacht weiterhin kein Schreiben/Refresh.
- No-op-NFOs bleiben bytegleich und ohne neuen Schreibzeitpunkt erhalten.
- Teilweise Bearbeitung wird nach `partial`, vollständige Bearbeitung nach `done` verschoben; Wiederaufnahme nutzt Geschwisterordner. Kollisionen überschreiben keine anderen Reports.
- Fehlgeschlagener bzw. nicht möglicher erforderlicher Refresh verhindert `done`. Ohne konfigurierte Emby-Zugangsdaten bleibt der bisherige rein lokale Abschluss möglich.
- Ein bekanntes Emby-Item wird bei späterem fehlenden Pfadtreffer absichtlich wiederverwendet; der dazu bestehende Regressionstest wurde nicht umgedeutet.
- Der Scan-Abbruch beendet nur lokales Warten/Nachprüfen, nicht einen bereits auf dem Server laufenden Scan.

## Restpunkte und Risiken

- **R01, P2, offen:** NFO und Reports verwenden Read-Modify-Replace ohne Dateiversionsvergleich oder Prozess-übergreifende Sperre. Ein gleichzeitiger externer Emby-/Benutzer-Schreibvorgang zwischen Lesen und Replace kann weiterhin überschrieben werden. Atomarer Austausch verhindert Teil-Dateien, aber nicht Lost Updates. Nicht auf echten Dateien reproduziert; keine spekulative Lock-/Backup-Architektur eingeführt.
- **R02, P2, im Nachtrag behoben:** Separate Erinnerung erfolgreich angeforderter Werte verhindert identische Wiederholungen ohne neue lokale Änderung oder neue Serverdaten; siehe E21. Serverseitiger Abschluss bleibt ausdrücklich unbestätigt. Zentrale Tests für den Nachtrag noch ausstehend.
- **R03, Hinweis, Parent-Integration:** `EmbySyncViewModel` implementiert den angeforderten `IModuleInteractionState`; siehe E24. Der Parent übernimmt die Shell-Sperre für globale Settings und Modulwahl während `IsInteractive == false`. Ein programmatischer Settingswechsel außerhalb dieses UI-Vertrags ist dadurch nicht serialisiert. Gemeinsame Shell-/Emby-Tests zentral ausstehend.
- **R04, Hinweis:** Cross-Platform-Pfadzuordnung bleibt eine konservativere Heuristik, kein vom Benutzer konfiguriertes Mapping. Ein alleiniger falscher Treffer mit denselben letzten drei Segmenten kann ohne weitere Serverdaten nicht sicher ausgeschlossen werden. Same-Platform-Suffixmapping ist nun absichtlich nicht mehr erlaubt. Mehrdeutige Library-Zuordnung führt nach bestehender Regel weiterhin zum ausdrücklich markierten globalen Scan, nicht automatisch zum Abbruch.
- **R05, Hinweis:** Geänderte NFOs werden semantisch verlustfrei für unveränderte XML-Knoten geschrieben, nicht bytegetreu. Encoding/BOM, XML-Deklaration und Zeilenumbruchdarstellung können bei echten Änderungen wechseln. Fremde Root-/Namespace-/DTD-Varianten und Multi-Root-NFOs werden sicher abgewiesen, nicht konvertiert. Entfernte doppelte Providercontainer behalten nur die Attribute des ausgewählten kanonischen Elements.
- **R06, P3/Perf, offen:** Summary-Neuberechnungen scannen die Items mehrfach, Logtext wird wiederholt komplett konkatenert, und bei nicht gematchter Library können Folgelookups die Library-Liste erneut abrufen. Keine spekulative Collection-/Logging-/Cache-Umstrukturierung ohne Messung. HTTP-ContentRead puffert Bodies; absichtlich extrem große Serverantworten wurden nicht lastgetestet.
- **R07, Hinweis:** Import, Providerreview und NFO-Schreibworkflow haben weiterhin keinen eigenen Benutzer-Abbruchbutton. Nur Scan-Warten ist abbrechbar; HTTP ist zeitlich begrenzt. Synchrones Dateisystem-I/O auf einem hängenden Share lässt sich nicht hart abbrechen. Der UI-Thread wird dafür nicht mehr für die Dauer der Dateioperation blockiert.
- **R08, P3, im Nachtrag behoben:** Assets erhalten einen eigenen Abschlusszähler, auch in gemischten Läufen; siehe E22. Zentrale Tests für den Nachtrag noch ausstehend.

## Geprüfte Dateien und Abdeckung

Produktionsdateien im zugewiesenen Bereich statisch geprüft:

- `Services/Emby/AppEmbySettingsStore.cs` (unverändert).
- `Services/Emby/EmbyClient.cs` (geändert).
- `Services/Emby/EmbyMetadataSyncService.cs` (geändert).
- `Services/Emby/EmbyNfoProviderIdService.cs` (geändert).
- `Services/Emby/EmbyProviderReviewDialogService.cs` (unverändert).
- `Services/BatchOutputMetadataReport.cs` (geändert).
- `Services/BatchOutputMetadataEntryFactory.cs` (unverändert; Provider-/TVDB-Feldherkunft und Nullfälle geprüft, kein belegter separater Fix).
- `ViewModels/Modules/EmbySyncItemViewModel.cs` (geändert).
- `ViewModels/Modules/EmbySyncViewModel.cs` (geändert).
- `Views/EmbySyncView.xaml` (geändert).
- `Views/EmbySyncView.xaml.cs` (unverändert).

Dedizierte Tests: vorhandene Testfälle inventarisiert, betroffene Workflows/Helpers gelesen und gezielt ergänzt:

- `MkvToolnixAutomatisierung.Tests/Services/EmbyClientTests.cs` (geändert).
- `MkvToolnixAutomatisierung.Tests/Services/EmbyMetadataSyncServiceTests.cs` (geändert).
- `MkvToolnixAutomatisierung.Tests/Services/EmbyNfoProviderIdServiceTests.cs` (geändert).
- `MkvToolnixAutomatisierung.Tests/ViewModels/EmbySyncItemViewModelTests.cs` (geändert).
- `MkvToolnixAutomatisierung.Tests/ViewModels/EmbySyncViewModelTests.cs` (geändert).

Bereichsübergreifend nur lesend für Schnittstellen/Integrationsrisiken herangezogen: `Services/PathComparisonHelper.cs`, `Services/AppSettingsStore.cs`, `Services/AppArchiveSettingsStore.cs`, NFO-Aufrufstellen von `Services/ArchiveMaintenanceService.cs`, `ViewModels/Commands/AsyncRelayCommand.cs`, der vom Parent ergänzte Vertrag `ViewModels/Modules/IModuleInteractionState.cs`, globale Settings-Benachrichtigung sowie die Emby-Abschnitte von `MkvToolnixAutomatisierung.Tests/Views/SelectionGridInteractionTests.cs`. Keine vollständige Review dieser fremden Bereiche beansprucht.

Nicht abgedeckt: echter Emby-Server, TLS/Proxy/Redirect-Verhalten, alle Emby-Versionen und Statusstrings, echte SMB-/NAS-Failover-/ACL-Szenarien, Windows-UI bei unterschiedlichen DPI/Fenstergrößen, visuelle Regressionen, Lastmessungen, echte Crash-/Powerloss-Simulationen. Metadata-/IMDb-/TVDB-Implementierungen und ihre Dateien wurden nicht editiert.

## Zentrale Testfilter

Dedizierter Emby-Block:

```text
FullyQualifiedName~EmbyClientTests|FullyQualifiedName~EmbyMetadataSyncServiceTests|FullyQualifiedName~EmbyNfoProviderIdServiceTests|FullyQualifiedName~EmbySyncItemViewModelTests|FullyQualifiedName~EmbySyncViewModelTests
```

Emby-WPF-Integration in der gemeinsam gepflegten Testdatei:

```text
FullyQualifiedName~SelectionGridInteractionTests.Emby
```

NFO-/Report-Konsumenten, danach Gesamtlauf seriell durch den Parent:

```text
FullyQualifiedName~ArchiveMaintenanceServiceTests|FullyQualifiedName~ArchiveMaintenanceViewModelTests|FullyQualifiedName~BatchRunLogServiceTests|FullyQualifiedName~AppSettingsWindowViewModelTests|FullyQualifiedName~MainWindowViewModelTests
```

Für den eng beauftragten Nachtrag zuerst:

```text
FullyQualifiedName~EmbyNfoProviderIdServiceTests|FullyQualifiedName~EmbySyncItemViewModelTests|FullyQualifiedName~EmbySyncViewModelTests
```

Diese Filter bleiben für gezielte zentrale Wiederholungen dokumentiert. Der vollständige Unit-/WPF-Lauf **vor dem Nachtrag** ist extern mit 1081/1081 bestandenen Tests gemeldet. Der Nachtrag einschließlich Shell-Anschluss ist noch nicht als zentral kompiliert/getestet gemeldet; ein eigener Build/Test wurde nicht ausgeführt.
