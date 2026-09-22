# Review: Archivpflege, Download-Sortierung und Cleanup

Stand: 2026-09-22. Ausgang: `master`, `323634f`.

## Arbeitsgrenzen und Verifikation

- Ausschließlich die zugewiesenen Service-, ViewModel-, View- und Testdateien sowie dieser Bericht wurden editiert, jeweils per `apply_patch`.
- Keine Builds, Tests, Formatierung, Commits, Stage- oder Push-Operationen durch diesen Review-Agenten. Keine echten Archiv-/Download-Dateien verändert.
- Neue Dateisystemtests verwenden eigene GUID-Tempverzeichnisse. Recyclingtests verwenden austauschbare Testaktionen, nicht den echten Papierkorb.
- `git diff --check` für die eigenen Änderungen war sauber. Beide XAML-Dateien wurden als wohlgeformtes XML eingelesen. Das ersetzt weder WPF-Kompilierung noch Laufzeittests.
- Der Parent bestätigte abschließend den vollständigen zentralen Unit-/WPF-Lauf mit **1081/1081 erfolgreichen Tests**, einschließlich des korrigierten `Apply_SkipsWholeGroup_WhenDefectiveTargetAlreadyExists`. Im vorherigen Lauf mit 1079 Tests entfernte dieser Test den jetzt bereits beim Scan als `Conflict` markierten Kandidaten vor `Apply`. Der Test prüft nun den Vorschau-Konflikt explizit und übergibt den Request dennoch direkt an `Apply`, um dessen defensive Prüfung zu erhalten. Die Laufbestätigung stammt vom Parent; dieser Agent hat keinen eigenen Testlauf gestartet.
- Der bestehende Untertitel-Upgrade-Fix aus `323634f` wurde nicht editiert. Die neuen Sidecar-Regeln betreffen externe Begleitdateien, nicht die Auswahl eingebetteter Archiv-Untertitel beim Mux.
- Koordination mit Mux-core: Rohe Containersprache und Shared-Normalizer-Fix bleiben in dessen Zuständigkeit. Archivpflege verwendet für automatische Korrekturen bereits `automaticEdit.CurrentDisplayValue`; Models/Normalizer wurden hier nicht editiert. Die Rohsprachenintegration wird nur noch beobachtet und bei Bedarf abgestimmt, insbesondere die Ist-Anzeige `de -> nds`. Keine zusätzliche funktionale Änderung in diesem Bereich.

## Findings und Fixes

### F01 - P1 - Defekt-Ziele konnten nach der Vorprüfung überschrieben werden

Ort: `Services/DownloadSortService.cs`, `Apply`, `MoveFiles`, `MoveFileSafely`.

Die Vorprüfung sperrte vorhandene Dateien in `defekt`, danach verwendete die Ausführung aber denselben ersetzenden Move wie für reguläre Ziele. Eine inzwischen angelegte gleichnamige Datei konnte daher ohne Freigabe ersetzt werden. Defekt-Moves verwenden jetzt unmittelbar `File.Move(..., overwrite: false)`; Ordnerbelegungen werden ebenfalls als Konflikt erkannt. Vorschau und Apply beurteilen Defekt-Konflikte konsistent.

Tests: `EvaluateTarget_DefectiveDestinationConflict_MatchesApply`, angepasster `Apply_SkipsWholeGroup_WhenDefectiveTargetAlreadyExists`. Die konkrete Race zwischen Vorprüfung und Move ist nicht deterministisch injiziert; der no-overwrite-Aufruf wurde statisch geprüft.

### F02 - P1 - Nach gescheitertem Video-Move wurden Begleiter trotzdem ersetzt

Ort: `Services/DownloadSortService.cs`, `MoveFiles`.

Bei einer gesperrten oder verschwundenen Videoquelle lief die Dateischleife weiter und konnte TXT-/Untertitel-Ziele ersetzen, obwohl das zugehörige Video nicht ausgetauscht wurde. Das zerstört die Konsistenz des Zielpakets und kann dessen bisherige Sidecar-Inhalte verlieren. Jetzt werden alle Quellen vorab auf Existenz geprüft, Videos zuerst verschoben und die restlichen Begleiter nach einem Move-Fehler nicht mehr angefasst. Andere Gruppen können weiterlaufen. Zielordner an einer Dateiposition blockieren die gesamte Gruppe schon vor dem ersten Move.

Tests: `Apply_MissingVideoWithoutTarget_DoesNotMoveItsSidecars`, `Apply_LockedVideo_DoesNotOverwriteSidecars_AndContinuesOtherGroups`, `Apply_DirectoryAtSidecarDestination_BlocksWholeGroup`.

### F03 - P1 - Cleanup expandierte geschützte abgelehnte Medien vor dem Schutzfilter

Ort: `Services/EpisodeCleanupFilePlanner.cs`, `BuildRejectedSourceCleanupFileList`.

Wurde Output oder Working Copy als abgelehnte Quelle übergeben, fiel die Mediendatei zwar aus der Liste, ihre zuvor expandierten Sidecars konnten aber im Cleanup bleiben und später recycelt werden. Jetzt werden die abgelehnten Medien selbst zuerst gegen Output, Working Copy, Archiv und Source-Root gefiltert und erst danach expandiert.

Tests: `BuildRejectedSourceCleanupFileList_DoesNotExpandProtectedOutputOrWorkingCopy` für beide Schutzarten; bestehender Root-/Archivfilter-Test bleibt erhalten.

### F04 - P2 - Archiv-Umbenennung ließ MKV und Sidecars nach Fehlern auseinanderfallen

Ort: `Services/ArchiveMaintenanceService.cs`, `ValidateRename`, `ApplyRename`.

Die MKV und bereits verschobene Begleiter blieben am neuen Ort, wenn ein späterer Begleiter fehlte oder gesperrt war. Der Rückgabepfad zeigte weiterhin auf die alte MKV. Jetzt werden fehlende Quellen und Datei-/Ordnerkollisionen vorab geprüft, erfolgreich verschobene Einträge bei Fehlern rückwärts zurückverschoben und Rollback-Fehler mit konkreten Pfaden gemeldet. Bei verbleibender MKV am Ziel wird deren tatsächlicher Pfad zurückgegeben. Kein Abbruch mitten in der Rename-Gruppe, sondern Abschluss oder Rollback.

Tests: `ApplyAsync_RollsBackMediaAndMovedSidecars_WhenLaterSidecarIsLocked`, `ApplyAsync_RejectsMissingPlannedSidecar_BeforeMovingMedia`.

### F05 - P2 - Bekannte Rename-Konflikte wurden erst nach Header-/NFO-Änderungen bemerkt

Ort: `Services/ArchiveMaintenanceService.cs`, `ApplyCoreAsync`.

Ein bereits belegtes Rename-Ziel verursachte zuvor erst nach schreibenden Metadatenoperationen einen Fehler. Rename-Quelle und Zielgruppe werden jetzt vor diesen Schreibschritten geprüft. Die Rename-Quelle muss zur angeforderten MKV passen. Manuelle Rename-Zielnamen akzeptieren keine Verzeichnispfade oder fremden Erweiterungen.

Tests: `ApplyAsync_PreflightsRenameCollision_BeforeChangingNfo` für Datei und Verzeichnis, `BuildManualRenameOperation_RejectsPathsAndNonMkvNames`.

### F06 - P2 - Extension-only Case-Rename versuchte unveränderte Sidecars zu verschieben

Ort: `Services/ArchiveMaintenanceService.cs`, `ApplyRename`.

Bei `Pilot.mkv` nach `Pilot.MKV` bleibt `Pilot.nfo` identisch; ein Move auf sich selbst führte nach dem Medien-Rename zum Fehler. Identische Quell-/Zielpfade werden jetzt übersprungen. Bestehende echte Case-Renames behalten ihren temporären Zwischenschritt. Auch Verzeichnisse werden bei der temporären Namenswahl ausgeschlossen.

Tests: `ApplyAsync_AllowsExtensionOnlyCaseRename_WithoutMovingUnchangedSidecars`; bestehender `ApplyAsync_RenamesCaseOnlyMediaAndSidecars`.

### F07 - P2 - Externe Untertitel blieben bei Rename/Season-Wechsel zurück

Ort: `Services/ArchiveMaintenanceService.cs`, `BuildSidecarRenameOperations`.

Die bisherige Liste enthielt NFO und einige Bilder, aber keine externen Untertitel. Jetzt werden direkte Untertitel sowie bekannte Sprach-/Accessibility-Suffixe mitgeführt, einschließlich `.sub`/`.idx`. Unbekannte angehängte Episoden-/Titelteile werden nicht pauschal als Begleiter eingesammelt.

Test: `ApplyAsync_MovesSubtitleSidecarsToNewSeason_WithoutTakingOtherEpisodes`.

### F08 - P2 - NFO-Sperren wurden ohne erfolgreiche TVDB-Auflösung nicht respektiert

Ort: `Services/ArchiveMaintenanceService.cs`, `AnalyzeContainer`.

Ein gesperrter NFO-Titel wurde nur im optionalen TVDB-Lookup berücksichtigt. Ohne Mapping, TVDB-ID oder erfolgreiche Abfrage fiel die Analyse auf den Dateititel zurück und konnte einen absichtlich gepflegten Container-Titel wieder ändern. Der NFO-Titellock wirkt jetzt bereits bei der lokalen Analyse, auch auf den Rename-Vorschlag. Nicht lesbare NFOs werden als Analysefehler behandelt statt als scheinbar leere Metadatenbasis.

Tests: `AnalyzeContainer_UsesLockedNfoTitle_WithoutTvdbLookup`, `AnalyzeContainer_UnreadableNfo_IsNotReportedAsSafe`.

### F09 - P2 - Seit dem Scan geänderte NFO-Texte/Sperren konnten überschrieben werden

Ort: `Services/ArchiveMaintenanceService.cs`, `ApplyCoreAsync`.

Die im Text-Edit bereits vorhandenen Ist-Werte wurden bislang nicht gegen die aktuelle NFO geprüft. Jetzt werden Titel, Sortiertitel und beide Feldsperren vor dem ersten Schreibschritt verglichen; unlesbare oder veränderte Werte erfordern einen neuen Scan.

Test: `ApplyAsync_RejectsNfoLockChangedSinceScan_BeforeProviderEdits`. Keine Dateisperre über alle Schreibschritte; verbleibendes Race siehe Restrisiken.

### F10 - P2 - Cancellation und Cache-Invalidierung waren für nicht-prozessbasierte Writes unvollständig

Ort: `Services/ArchiveMaintenanceService.cs`, `ApplyAsync`, `ApplyCoreAsync`.

NFO-only/Rename-only Requests prüften das Abbruchsignal nicht. Jetzt wird es beim Einstieg und vor jedem separaten NFO-/Rename-Schritt geprüft. Die Probe-Invalidierung steht in `finally`, weil auch fehlgeschlagene oder abgebrochene Header-Writes die Datei schon verändert haben können.

Tests: `ApplyAsync_AlreadyCanceled_DoesNotWriteNfoOrRename`, `ApplyAsync_CancellationAfterProviderEdit_StopsTextEditAndRename`. Cache-Invalidierung bei echtem externem Toolabbruch wurde nur statisch nachvollzogen.

### F11 - P2 - Manuelle Archivkorrekturen konnten eine explizite Abwahl aufheben

Ort: `ViewModels/Modules/ArchiveMaintenanceItemViewModel.cs`, `IsSelected`, `NotifyManualCorrectionChanged`.

Jede weitere gültige Korrektur setzte ein abgewähltes Item wieder auf ausgewählt. Die explizite Abwahl bleibt jetzt erhalten, auch über einen zwischenzeitlich ungültigen Dateinamen. Erstmalige manuelle Änderungen an zuvor neutralen Zeilen bleiben wie bisher automatisch ausgewählt.

Tests: erweiterter `ManualChange_SelectsPreviouslyOkItemUntilUserClearsSelection`, `ManualChange_DoesNotReselectExplicitlyDeselectedItem_AfterValidationRecovers`.

### F12 - P2 - Season-only Vorschläge ließen sich weder ablehnen noch mit Ist-Wert deaktivieren

Ort: `ViewModels/Modules/ArchiveMaintenanceItemViewModel.cs`, Suppression/Reset/Rename-Vorschau.

Die Ablehnung verglich nur Dateinamen, während der Rename-Builder selbst bei unverändertem Namen einen anderen Season-Ordner erzeugte. Jetzt umfasst die gespeicherte Ablehnung bei Verzeichniswechseln die konkreten Pfade. Alte filename-basierte Ablehnungen für echte Dateinamenänderungen bleiben kompatibel. Ist-Wert behält auch den aktuellen Ort; Aufheben aktiviert den Vorschlag erneut. Pfadwechsel werden als solche angezeigt, nicht als scheinbare Umbenennung auf denselben Namen. Detail-Sidecars stammen aus dem aktuellen statt dem ursprünglichen Plan.

Tests: `SuppressFileNameChange_AlsoSuppressesSeasonOnlyMove_AndRestoresIt`, `ResetFileNameToCurrent_KeepsCurrentSeasonLocation`; bestehende Persistenztests bleiben erhalten. Kein separater Laufzeittest für die Legacy-Ablehnungsmigration.

### F13 - P2 - Archiv-Editor blieb während Apply editierbar

Ort: `Views/ArchiveMaintenanceView.xaml`, `ViewModels/Modules/ArchiveMaintenanceViewModel.cs`.

Der manuelle Editor und seine separate Dateiauswahl waren anders als die Tabelle nicht an `IsInteractive` gebunden. Dadurch konnten laufende Writes und angezeigte/anschließend als angewendet markierte Zielwerte auseinanderlaufen. Beide Bedienelemente sind jetzt im Busy-Zustand gesperrt. Gemeldete Apply-Fehler deaktivieren die veraltete Zeile für weitere Writes und zeigen gegebenenfalls den tatsächlichen Restpfad an.

Tests: `ManualEditorAndFileSelector_AreDisabledWhileBusy`, `MarkApplyFailed_UsesActualPath_AndPreventsStaleRetry`.

### F14 - P2 - Archiv-Dateisystemarbeit lief auf dem UI-Thread, Toolausgabe war nicht serialisiert

Ort: `ViewModels/Modules/ArchiveMaintenanceViewModel.cs`, `Services/ArchiveMaintenanceService.cs`.

Rekursive Enumeration und synchrone NFO-/Rename-Arbeit konnten vor/nach dem ersten Await den Dispatcher blockieren. Scan/Apply laufen jetzt über `Task.Run`; Progress wird über den erfassten SynchronizationContext zugestellt. Synchrone Zustellung verhindert nachlaufende Ausgabe nach Abschluss. Die gemeinsame stdout/stderr-Ausgabeliste ist gegen gleichzeitige Schreibzugriffe geschützt. Alte Item-Handler werden vor dem erneuten Scan abgehängt.

Bestehende Scan-, Abbruch-, Apply- und Log-Tests decken die Grundpfade ab; der zentrale Unit-/WPF-Gesamtlauf ist bestätigt grün. Keine gemessene UI-Latenz und kein spezieller stdout/stderr-Stresstest; darüber hinausgehende Integration bleibt vom Unit-/WPF-Nachweis getrennt.

### F15 - P2 - Abgebrochene Sortierung verlor die Liste bereits ausgeführter Schritte

Ort: `Services/DownloadSortService.cs`, `ViewModels/Modules/DownloadSortViewModel.cs`.

Der Service warf zwischen Gruppen nach bereits erfolgten Moves und verwarf damit Ergebniszähler und Log. Nach Start liefert er jetzt ein Teilergebnis mit `WasCanceled`; vor Start bleibt ein abgebrochener Token eine Exception. Cancellation wird zusätzlich zwischen Dateien beachtet. Das ViewModel übernimmt das Teilprotokoll, sperrt veraltete Ergebnisse bis zu einem erfolgreichen Rescan und ignoriert verspätete Scan-Progress-Meldungen. Erfolgs-/Ersetzungsprotokolle nennen nur tatsächlich verschobene/ersetzte Dateien.

Tests: `Apply_CancellationBetweenGroups_ReturnsCompletedWorkAndLog`, `CanceledRescan_DisablesSortingUntilFreshScan`; bestehende Pre-Cancellation-Tests. Kein Abbruch eines bereits laufenden einzelnen `File.Move`.

### F16 - P2 - Automatischer Rescan wählte zuvor abgewählte Downloads erneut aus

Ort: `ViewModels/Modules/DownloadSortViewModel.cs`, `ScanCoreWithoutBusyAsync`.

Nach Ausführung einer Teilmenge wurden verbleibende Pakete wieder mit den Default-Auswahlwerten erzeugt. Der automatische Rescan bewahrt jetzt die Abwahl über die noch vorhandenen Quellpfade. Ein ausdrücklich neuer Scan behält sein bisheriges Default-Verhalten.

Test: `RunSortCommand_PreservesDeselectedRemainingPackage_AfterAutomaticRescan`.

### F17 - P2 - Beliebiges ausgewähltes Video umging den Schutz anderer Sidecars

Ort: `Services/DownloadSortService.cs`, `FindBlockingLooseVideoCompanion`.

Der bisherige Early-return bei irgendeiner MP4 erlaubte gemischte Requests mit Sidecars einer anderen, nicht ausgewählten gesunden MP4. Jetzt wird für jeden Begleiter die eigene Videozuordnung geprüft, in Vorschau und Apply.

Test: `Apply_UnrelatedSelectedVideo_DoesNotPermitMovingDeselectedVideoSidecars`.

### F18 - P2 - Excluded-Quellen schützten sprachmarkierte Sidecars nicht

Ort: `Services/EpisodeCleanupFilePlanner.cs`, `IsCompanion`, `CleanupExclusion`.

Die Exclusion erkannte nur identische Stems. `srf.de.forced.srt` konnte trotz bewusst ausgeschlossener `srf.mp4` aufgeräumt werden. Bekannte Sprach-/Accessibility-Tokens werden jetzt konsistent geschützt; ausdrücklich verworfene Quellen können diese Begleiter dagegen im berechtigten Rejected-Cleanup mitnehmen. Fremde Suffixe bleiben unberührt.

Tests: `BuildCleanupFileList_ProtectsLanguageAndAccessibilitySidecarsOfExcludedSource`, `BuildRejectedSourceCleanupFileList_IncludesOnlyRecognizedLanguageSidecars`.

### F19 - P2 - Sortierziele und Rename-Plans waren nicht durchgehend konservativ

Ort: `Services/DownloadSortService.cs`, Zielprüfung und `BuildFolderRenamePlans`.

Direkte Zielunterordner werden jetzt auch im regulären Apply über denselben begrenzenden Check geprüft. Bereits vorhandene Reparse-Point-Zielordner werden abgelehnt und nicht als Rename-Kandidaten gescannt. Reservierte Quellordner wie `done` dürfen durch einen direkt übergebenen Plan nicht umbenannt werden. Eine unerkannte Downloadgruppe wird nicht mehr aus der Inhaltsbewertung entfernt, sodass ein gemischter Ordner nicht allein wegen einer erkannten Gruppe als sicherer Rename erscheint.

Tests: `Apply_DoesNotRenameReservedSourceFolder`, `Scan_DoesNotRenameFolder_WhenAnotherPackageIsUnidentified`; bestehende Scope-Tests für fremde/nested Quellpfade. Kein privilegierter Junction-/Symlinktest durchgeführt.

### F20 - P3 - Schreibweisen und äquivalente Pfade erzeugten inkonsistente Defekt-/Cleanup-Ergebnisse

Ort: `Services/DownloadSortService.cs`, `Services/EpisodeCleanupService.cs`, `Services/EpisodeCleanupFilePlanner.cs`.

Die TXT-Ausnahme entfernte Begleiter mit case-sensitivem `List.Remove`, obwohl die Zuordnung case-insensitiv war. `.TXT` landete deshalb nicht mit der defekten MP4 im Defekt-Ordner. Bei Defekt-Teilmengen wurden normalisierte und rohe Pfade vermischt. Kandidaten werden jetzt zuerst vereinheitlicht und dedupliziert. Cleanup dedupliziert ebenfalls nach `GetFullPath`, damit `file` und `./file` nicht zweimal verarbeitet werden.

Tests: `Scan_UppercaseTxtCompanion_GoesWithDefectiveVideo`, `Apply_NormalizesAndDeduplicatesDefectivePaths`, `BuildCleanupFileList_DeduplicatesEquivalentFullPaths`, `RecycleFilesAsync_DeduplicatesEquivalentPaths_BeforeCallingRecycle`.

### F21 - P3 - Eindeutige Cleanup-Zielnamen ignorierten vorhandene Verzeichnisse

Ort: `Services/EpisodeCleanupService.cs`, `BuildUniqueTargetPath`.

Die Kollisionsprüfung betrachtete nur Dateien. Ein Verzeichnis mit Dateinamen führte zum Move-Fehler statt zur nächsten freien Nummer. Beide Eintragsarten werden jetzt ausgeschlossen.

Test: `MoveFilesToDirectoryAsync_SkipsDirectoryNameCollisions`.

### F22 - P3 - Headergruppen und Fehlerdetails waren irreführend

Ort: `ViewModels/Modules/ArchiveMaintenanceItemViewModel.cs`.

Unterschiedliche Tracks mit demselben Anzeigenamen wurden in eine manuelle Gruppe zusammengefasst. Gruppiert wird jetzt nach Track-Selector. Analysefehler ohne Änderungen zeigten außerdem einen grünen Keine-Änderung-Befund; Fehlertext und Fehlerzustand bleiben jetzt sichtbar.

Tests: `VisibleHeaderCorrectionGroups_DoesNotMergeDifferentTracksWithSameName`, `FailedAnalysis_DoesNotShowNoFindingsMessage`.

### F23 - P2 - Automatische NFO/TVDB-Auflösung konnte eine Doppelfolge auf eine Folge verkürzen

Ort: `Services/ArchiveMaintenanceService.cs`, `TryResolveExpectedMetadataFromNfoAsync`.

Eine einzelne TVDB-ID in der NFO ersetzte die geparste Episodenangabe auch bei `E01-E02` durch eine einzelne Episodennummer. Die erkannte Range bleibt jetzt erhalten. Kein dedizierter Online-/NFO-Lookup-Test ergänzt; der kleine Zweig ist nur statisch geprüft. Titel-/Season-Zuordnung einer Mehrfachfolge bleibt ein fachlicher Restpunkt.

### F24 - Hinweis - Vorschau-IO bewusst begrenzt

Die erweiterte Sidecar-Erkennung darf nicht bei jedem WPF-Property-Getter das Verzeichnis erneut aufzählen. Der Rename-Vorschlag wird im Item pro Zielnamen gecacht und beim Erzeugen des Apply-Requests frisch aufgebaut. Keine allgemeine Cache-/Pfadhelper-Refaktorierung; kein Benchmark durchgeführt.

## Verbliebene Risiken und nicht gefixte Punkte

- P2, übergreifend: `PathComparisonHelper` vergleicht Pfade lexikalisch und case-insensitiv. Das beweist keine physische Containment-Grenze bei Junctions, Symlinks, Hardlinks oder case-sensitiven Unterbäumen. Die lokale Sperre bereits sichtbarer Download-Ziel-Reparse-Points löst weder gemeinsame Ancestor-Links noch Linkwechsel zwischen Check und Move. Shared Helper nicht editiert.
- P2, übergreifend: Die gelesene Fassung von `EmbyNfoProviderIdService.IsLockedField` wertete `lockedfields`, aber nicht `lockdata` aus. Ob globale Emby-Locks in diesen beiden Feld-Flags erscheinen sollen, muss der zuständige Agent fachlich prüfen. Dort arbeiten andere Agenten; deren Endstand ist hier nicht erneut voll reviewed.
- P2: Header-, Provider-ID-, Text- und Rename-Schritte sind keine gemeinsame Transaktion. Ein späterer Fehler kann frühere Metadatenänderungen hinterlassen. Cache-Invalidierung, Fehleranzeige und gesperrte Wiederholung reduzieren die Folgen, ersetzen aber keine Datei-Backups.
- P2: NFO-Istvergleich und Download-Ziel-Snapshot schließen TOCTOU-Races nicht vollständig. Kein durchgängiger Datei-Handle/Lock, keine Snapshot-Identität über alle Mux/Headerdaten. Die Default-Regel für vergleichbare reguläre Download-Ziele bleibt bewusst ersetzend.
- P2: Download-Pakete werden nicht vollständig transaktional verschoben. Scheitert ein Sidecar nach erfolgreichem Video, bleiben bereits erfolgreiche Moves bestehen. Protokoll/Zähler bilden dies ab; die restlichen Begleiter werden nicht weiter angefasst. Prozessabsturz/Volume-Abbruch und fehlgeschlagener Rename-Rollback bleiben manuelle Wiederherstellungsfälle.
- P2: Folder-Renames werden vor der gruppenweisen Apply-Prüfung ausgeführt. Ein ausgewählter Request mit später festgestelltem Quellen-/Zielkonflikt kann deshalb bereits eine zulässige Ordnervereinheitlichung ausgelöst haben. Zur Konsolidierung nicht auf einen neuen Transaktionsplan umgestellt.
- P3: `BuildManualRenameOperation` prüft Einzeldateiname/Extension, aber nicht alle Windows-Gerätenamen oder Dateisystem-Längenlimits. Move-Fehler bleiben möglich, werden jedoch ohne Overwrite behandelt.
- P3: Unbekannte Sprach-/Regionalsuffixe, z.B. nicht gelistete BCP-47-Varianten, und weitere Poster-/Artwork-Namensschemata werden nicht pauschal mitgenommen. Konservative explizite Listen statt riskanter Prefix-Massenmoves.
- P3: Manueller Track-Text hat weiterhin keine vollständige mkvpropedit-Propertyvalidierung; unbekannte Flagtexte können intern als false abgebildet werden, die UI bietet für Flags aber nur ja/nein. Keine Änderung an Shared Header-Normalisierung.
- Hinweis: Ein aktiver einzelner `File.Move` oder synchroner Shell-Papierkorb-Aufruf ist nicht unterbrechbar. Echte Papierkorb-/Netzlaufwerksregeln, Shell-COM-Apartment-Verhalten und permanente-Lösch-Fallbacks wurden nicht ausgeführt oder als sicher bestätigt.
- Hinweis: Kein UI-Screenshot-/DPI-/Tastaturtest und kein Lasttest mit sehr großen Archiven oder langsamem SMB. Bestehende Layoutmechanik und Download-View blieben außerhalb des belegten Busy-Bindings unverändert.
- Hinweis: Keine Überprüfung der parallelen Agents-Änderungen außerhalb dieses Bereichs. Insbesondere Shared Header-Normalisierung, Pfadhelper und `DataGridSelectionInput` wurden nicht editiert.

## Geprüfte Dateien

Produktivbereich, gelesen und wo oben beschrieben geändert:

- `Services/ArchiveMaintenanceService.cs`
- `Services/DownloadSortService.cs`
- `Services/EpisodeCleanupService.cs`
- `Services/EpisodeCleanupFilePlanner.cs`
- `ViewModels/Modules/ArchiveMaintenanceViewModel.cs`
- `ViewModels/Modules/ArchiveMaintenanceItemViewModel.cs` einschließlich Header-Correction-Typen
- `ViewModels/Modules/DownloadSortViewModel.cs`
- `ViewModels/Modules/DownloadSortItemViewModel.cs` (unverändert)
- `Views/ArchiveMaintenanceView.xaml`
- `Views/ArchiveMaintenanceView.xaml.cs` (unverändert)
- `Views/DownloadSortView.xaml` (unverändert)
- `Views/DownloadSortView.xaml.cs` (unverändert)

Zugeordnete Tests, vorhandene Szenarien gesichtet und neue Regressionen ergänzt:

- `MkvToolnixAutomatisierung.Tests/Services/ArchiveMaintenanceServiceTests.cs`
- `MkvToolnixAutomatisierung.Tests/Services/DownloadSortServiceTests.cs`
- `MkvToolnixAutomatisierung.Tests/Services/EpisodeCleanupServiceTests.cs`
- `MkvToolnixAutomatisierung.Tests/Services/EpisodeCleanupFilePlannerTests.cs`
- `MkvToolnixAutomatisierung.Tests/ViewModels/ArchiveMaintenanceViewModelTests.cs`
- `MkvToolnixAutomatisierung.Tests/ViewModels/DownloadSortViewModelTests.cs`
- `MkvToolnixAutomatisierung.Tests/ViewModels/DownloadSortItemViewModelTests.cs` (unverändert)
- `MkvToolnixAutomatisierung.Tests/Views/ArchiveMaintenanceLayoutTests.cs`

Nur kontextuell gelesen, kein Gesamtreview und keine eigenen Edits: `Services/PathComparisonHelper.cs`, relevante Teile von `Services/EpisodeFileNameHelper.cs`, `Services/Emby/EmbyNfoProviderIdService.cs`, `Services/MuxExecutionService.cs`, `Services/MediaFileHealth.cs`, `MkvToolnixAutomatisierung.Tests/TestInfrastructure/WpfTestHost.cs`, Testprojektdefinition.

## Geänderte Dateien

Die acht Produktivdateien `ArchiveMaintenanceService.cs`, `DownloadSortService.cs`, `EpisodeCleanupService.cs`, `EpisodeCleanupFilePlanner.cs`, `ArchiveMaintenanceViewModel.cs`, `ArchiveMaintenanceItemViewModel.cs`, `DownloadSortViewModel.cs`, `ArchiveMaintenanceView.xaml` unter den oben genannten Pfaden; alle sieben oben aufgeführten Testdateien außer `DownloadSortItemViewModelTests.cs`; dieser Bericht. Insgesamt 16 eigene Dateien.

## Zentrale Testfilter

Der vollständige zentrale Unit-/WPF-Lauf ist mit 1081/1081 erfolgreichen Tests bestätigt. Die folgenden Filter bleiben für gezielte Nachläufe dokumentiert.

Unit-Services und ViewModels, zusammen oder serialisiert in Teilgruppen:

```text
FullyQualifiedName~ArchiveMaintenanceServiceTests|FullyQualifiedName~DownloadSortServiceTests|FullyQualifiedName~EpisodeCleanupServiceTests|FullyQualifiedName~EpisodeCleanupFilePlannerTests|FullyQualifiedName~ArchiveMaintenanceViewModelTests|FullyQualifiedName~DownloadSortViewModelTests|FullyQualifiedName~DownloadSortItemViewModelTests
```

WPF-Layout separat/serialisiert:

```text
FullyQualifiedName~ArchiveMaintenanceLayoutTests
```

Gezielter Retest des vom Parent gemeldeten Fehlers:

```text
FullyQualifiedName~DownloadSortServiceTests.Apply_SkipsWholeGroup_WhenDefectiveTargetAlreadyExists
```

Der zentrale Unit-/WPF-Gesamtlauf ist abgeschlossen. Eine separate Mux-/Archiv-Untertitel-Integration für den erhaltenen Fix aus `323634f` wird damit nicht als durchgeführt behauptet. Dieser Agent hat dafür keine Integrationstestdateien angefasst oder eigene Testläufe gestartet.
