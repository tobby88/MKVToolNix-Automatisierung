# Review Mux-UI, 2026-09-22

## Auftrag und Prüfstand

- Basis laut Auftrag: master 323634f. Beim Einstieg war der Worktree sauber. Spätere disjunkte Änderungen anderer Agenten wurden nicht angefasst.
- Schreibbereich: zugewiesene Mux-ViewModels, Commands, Mux-Views und ihre unten aufgeführten Tests. Keine Shared Services, Composition, Pakete oder Projektdateien geändert.
- Bestehender Untertitel-Fix bleibt erhalten. Keine Änderung an Planner-/ArgumentBuilder-Untertitellogik; subtitle-only bleibt in der Single-Validierung zugelassen.
- Alle manuellen Änderungen per apply_patch. Kein Commit, Stage oder Push. Keine eigenen dotnet-Build-/Test-/Formatläufe. Keine echten Medien-, Archiv- oder Nutzerdaten verändert.
- Statisch geprüft: Diffs, Ablauf-/Cancellation-Grenzen, referenzierte Tests, XAML-Bindings und Eingaberouten. git diff --check im Scope ohne Befund; XML-Parsing der fünf Mux-XAML-Dateien erfolgreich.
- Zentraler Prüfstand vor dem ausdrücklich autorisierten Nachtrag MUX-18 bis MUX-21: **1081/1081 Tests grün** laut Parent. Die zuvor gemeldeten Fehler in Task-Typ-Assertions, WPF-Dispatcher-Testaufbau und einer überholten Freeze-Assertion sind behoben.
- Im Parent-Zwischenlauf während des Nachtrags waren zwei frühere OCE-Erwartungen in BatchExecutionRunnerTests noch veraltet. Beide prüfen jetzt das abgebrochene Teilergebnis und erhaltene Resultate.
- Finaler zentraler Prüfstand laut Parent einschließlich Nachtrag: **1167/1167 Unit-/WPF-Tests grün, keine Fehler und keine übersprungenen Tests**. DocFX und README-Screenshots sind ebenfalls grün. Die Integration läuft anschließend seriell beim Parent; ihr Ergebnis liegt bei Berichtsabschluss noch nicht vor. Kein eigener Build oder Testlauf. Der Schreibbereich ist mit diesem Bericht eingefroren.
- Vom Parent gemeldeter CS0200 im neuen Grid-Test wurde korrigiert: echte generierte Zelle statt Zuweisung an DataGridCell.Column.

Alle folgenden Pfade sind relativ zu C:\Users\tobby\Documents\mkvtoolnix-Automatisierung. Methoden-/Testnamen dienen als stabile Fundstellen.

## Findings und Fixes

### MUX-01, P1: Batch-Hinweisprüfung vor dem tatsächlichen Ausführungsplan

In BatchMuxViewModel.Execution.cs/RunBatchAsync wurden Pflichtprüfungen vor BuildExecutionWorkItemsAsync abgeschlossen. Der finale Plan übernahm seine Hinweise nicht in die Zeile; vorher noch nicht verglichene oder inzwischen geänderte Pläne konnten neue Schnittfassungs-/Archivhinweise ohne Freigabe ausführen.

Fix: finale Planpräsentation inklusive Notes übernehmen, Hintergrunddetails schon vor dem finalen Planbau einfrieren und danach offene Prüfungen erneut blockierend prüfen. Nach Review wird der Episodencode ebenfalls erneut validiert. Eine neue Freigabe erfolgt bewusst erst beim nächsten kontrollierten Lauf, nicht implizit.

Regression: BatchMetadataReviewTests.RunBatchCommand_FinalPlanIntroducesReview_BlocksExecution. Der bestehende RunBatchCommand_UserCancellation_RefreshesSelectedItemPlanSummary_AfterRealCommandFlow prüft jetzt den finalen eingefrorenen Plan und den Refresh nach Abbruch; awaitbarer Command statt unkontrolliertem Fire-and-forget im Test.

### MUX-02, P1: Mehrere Batch-Pläne können dasselbe Ziel oder fremde Eingaben überschreiben

In RunBatchAsync war ein Ausgabezielkonflikt lediglich ein bestätigbarer Hinweis. Es fehlte eine unabhängige Sperre für gleiche finale Ausgabepfade. Auch ein Ausgabeziel, das ein anderer vorbereiteter Plan als Eingabe verwendet, war nicht ausgeschlossen. Atomisches Schreiben allein verhindert den fachlich falschen Folgeschreibvorgang nicht.

Fix: vor Copy/Mux finale Ausgabepfade normalisieren und doppelte Ziele sowie Ausgabe-/Eingabekonflikte zwischen unterschiedlichen WorkItems blockieren. Eine absichtlich geänderte Ausführungsreihenfolge umgeht die Sperre nicht.

Regression: RunBatchCommand_DuplicateFinalOutputPaths_BlockEvenWithoutPlannerWarning und RunBatchCommand_OutputOverlapsAnotherPlanInput_BlocksExecution mit beiden Reihenfolgen. Tests laufen komplett auf WpfTestHost und verwenden nur synthetische eindeutige Temp-Pfade, ohne echte Werkzeugausführung.

### MUX-03, P1: Done-Cleanup konnte Eingaben späterer Batch-Pläne entfernen

BatchExecutionRunner.ExecutePlansAsync verschob CleanupFiles direkt nach jeder Episode. Eine gemeinsam verwendete Quelle oder eine versehentlich im Cleanup enthaltene andere Batch-Ausgabe war nicht batchweit geschützt.

Fix: vor dem Lauf Eingabe-Eigentümer erfassen, inklusive Arbeitskopien. Cleanup entfernt keine Datei, die ein anderes WorkItem referenziert, und keine finale Batch-Ausgabe. Solche Dateien bleiben konservativ am Quellort, auch wenn alle Folgen erfolgreich waren; QUELLENSCHUTZ im Log erklärt dies. Kein automatischer Versuch, nach einem späteren Teilfehler doch noch gemeinsam verwendete Quellen zu löschen.

Regression: BatchExecutionRunnerTests.ExecutePlansAsync_DoesNotMoveInputsOfOtherPlans_OrBatchOutputs. Nur Test-Tempdaten und ein aufzeichnender Cleanup-Double.

### MUX-04, P2: Einzel-Hintergrundplanung und editierbare Felder während Operationen

SingleEpisodeMuxViewModel hielt _currentPlan als gemeinsam veränderlichen Zustand. Ein bereits geplanter Refresh konnte während Vorschau/Mux ApplyPlanPresentation ausführen; Metadaten und Sprach-Overrides waren trotz deaktivierter Commands noch editierbar. Auch Quellen-/TVDB-Review sperrte andere Commands nicht während asynchroner Wartephasen.

Fix: Beginn einer expliziten Operation entwertet und cancelt Hintergrundplanung. Neue Refreshes werden bis zum Ende aufgeschoben; atomare Shared-State-Übernahmen erzeugen keine Zwischenrefreshes. Erfolgreicher Input-Reset cancelt Restrefreshes. IsInteractive sperrt Metadaten und Korrekturen, lässt den Abbruchknopf frei. Review bekommt einen Busy-Rahmen. Eine verschachtelte Neuerkennung gibt nicht den Busy-Zustand des Reviews frei. Archivkonfigurationsmeldungen werden im Einzel- und Batchmodus bis zum Ende einer laufenden Operation aufgeschoben. Tokenprüfungen nach asynchronen Schritten verhindern Rückschreiben nach Abbruch; Abbruch entwertet auch Detection-Fortschritt.

Regression: SingleEpisodeMuxViewModelTests.BeginCurrentOperation_CancelsBackgroundPlan_AndDefersNewRefreshes; PlanReviewLayoutTests.SingleEpisodeEditors_AreDisabledWhileBusy_WithoutDisablingCancel; vorhandene Single-/Batch-Cancellation-Tests im zentralen Gesamtlauf. Keine direkte Integration mit echten mkvmerge-Prozessen im neuen Test.

### MUX-05, P2: Batch-Neuerkennung gab fremden Busy-Zustand frei und verlor Cancellation

BatchMuxViewModel.Detection.cs/ApplyDetectionToItemAsync setzte im finally immer SetBusy(false), auch wenn RunBatchAsync den Busy-Zustand besaß. ReviewEpisodeAsync reichte den Batch-Token nicht an die alternative Detection weiter. Einzelne Detection-Fortschrittscallbacks waren nach Abschluss nicht mehr zuzuordnen.

Fix: Busy-State nur bei eigener Übernahme freigeben; Token durch alternative Detection und nachfolgende Metadatenvergleiche reichen; bei jedem Review-Schleifendurchlauf und nach asynchronem Quellenreview Cancellation prüfen. Detection-Callbacks tragen eine Sessionversion und prüfen diese im Dispatcher. Standalone-Sammel-/Metadatenreview bekommt einen Busy-Rahmen.

Regression: ReviewSelectedMetadataCommand_KeepsOtherCommandsDisabled_UntilReviewFinishes; vorhandene SourceReviewIntroducesMetadataReview- und reale Abbruchtests. Die verschachtelte reale alternative Dateierkennung ist weiterhin nicht separat mit einem blockierenden Scan-Double abgedeckt, da BatchScanCoordinator konkret verdrahtet ist.

### MUX-06, P2: Manuelle Batch-Metadaten mit manuellem Ziel entwerteten laufende Vergleiche nicht

BatchEpisodeItemViewModel.ComparisonInputVersion wurde für SeriesName/SeasonNumber/EpisodeNumber/Title nicht direkt erhöht. Bei manuellem Ausgabeziel fehlte zudem der indirekte Pfadwechsel. Ein laufender Vergleich konnte alte Hinweise zur neuen Episode anzeigen.

Fix: Versionsnummer bereits vor der PropertyChanged-Verteilung dieser Kerndaten anheben. Außerdem wird die Version in RefreshComparisonForItemAsync erst nach einer eigenen supplemental-only-Archivumleitung aufgenommen, damit die eigene Zielkorrektur den Vergleich nicht sofort als veraltet verwirft; Cancellation/Stale-Checks bleiben davor erhalten.

Regression: ManualMetadataChange_InvalidatesComparisonVersion_WithManualOutput für alle vier Eigenschaften. Der automatische Umleitungspfad wurde statisch nachvollzogen, aber nicht um einen neuen realen Archivtest erweitert.

### MUX-07, P2: Leere Pflichtprüfliste galt als freigegeben

EpisodeEditModel.IsManualCheckApproved leitete Freigabe ausschließlich aus fehlendem CurrentReviewTargetPath ab. RequiresManualCheck=true mit leerer/blanker Liste wurde dadurch automatisch freigegeben.

Fix: Pflichtprüfung ohne mindestens einen brauchbaren Pfad bleibt offen; Blank-Pfade werden in beiden Initialisierungspfaden entfernt. Batch-Review bricht den inkonsistenten Fall mit einer konkreten Neuerkennungsaufforderung ab, statt in eine erfolglose Wiederholung zu geraten. Einzel-Mux bleibt durch die vorhandene Pflichtprüfung blockiert.

Regression: EpisodeEditModelManualCheckTests.RequiredManualCheck_WithoutUsablePaths_IsNotImplicitlyApproved.

### MUX-08, P2: Eigene Exclude-Sicht wurde beim Ersetzen geleert

ReplaceExcludedSourcePaths konnte item.ExcludedSourcePaths selbst erhalten, unter anderem beim Redetect. Clear vor Enumeration leerte dann zugleich die Eingabe; bewusst ausgeschlossene Quellen konnten wieder zugelassen werden.

Fix: Eingabe vor Clear materialisieren. Regression: ReplaceExcludedSourcePaths_PreservesOwnReadOnlyView.

### MUX-09, P2: Debounce-Tokenquelle zu früh freigegeben und Dispose entwertete nicht

DebouncedRefreshController.CancelCore gab die CancellationTokenSource bereits frei, während die Aktion noch lief. Nachlaufende Token-Nutzung konnte ObjectDisposedException erzeugen. Dispose ließ die letzte Version formal aktuell und erlaubte neue Schedules.

Fix: jede laufende Aktion besitzt ihre Quelle bis zum finally, danach wird sie freigegeben. CurrentTask wird nur für die identische aktuelle Quelle geleert. Dispose entwertet laufende Versionen und sperrt neue Schedules.

Regression: Cancel_KeepsTokenSourceAlive_UntilRunningRefreshFinishes und Dispose_InvalidatesRunningRefresh_AndRejectsNewSchedules; bestehende Latest-only-/Older-completion-Tests bleiben relevant. Der Controller bleibt ein serialisiert vom UI verwendeter Controller, keine Zusage für beliebige konkurrierende Schedule-Aufrufe mehrerer Threads.

### MUX-10, P2: Freigabe bestätigte Hinweise, die nicht angezeigt wurden

Single-/Batch-Warnflächen und Batch-ConfirmPlanReview zeigten nur PrimaryActionablePlanNote, ApprovePlanReview gab jedoch alle aktuellen planrelevanten Hinweise frei.

Fix: ActionablePlanNotesDisplayText für beide sichtbaren Freigaben und den Batch-Reviewdialog verwenden. Mindestbreite des Freigabeknopfs bleibt erhalten.

Regression: beide bestehenden PlanReviewLayoutTests prüfen jetzt auch einen zweiten sichtbaren Hinweis. Echte WPF-Layouttests mit langem Text, keine reine XML-Assertion.

### MUX-11, P2: Grid-Eingabe konnte bei Inline-Text crashen

DataGridSelectionInput.FindVisualParent rief für jedes DependencyObject VisualTreeHelper.GetParent auf. Ein Run als OriginalSource ist ein ContentElement, kein Visual.

Fix: Visual/Visual3D, FrameworkContentElement und ContentElement über ihre jeweils passende Elternkette traversieren. Regression: SelectionColumnSource_AcceptsInlineContent_InsteadOfThrowingVisualTreeException über eine real generierte Grid-Zelle. Bestehende Space-, Maus-, Doppelklick- und Column-Reorder-Tests bleiben im zentralen Filter.

### MUX-12, P2: Arbeitskopien nur nach Quellpfad dedupliziert

BuildCopyPreparation verwarf eine zweite Kopie derselben Quelle mit anderem Arbeitsziel. Der zweite Plan konnte dadurch eine nicht vorbereitete Datei referenzieren.

Fix: nach Quelle UND Ziel deduplizieren. Regression: BuildCopyPreparation_KeepsDifferentDestinations_ForSameSource; bisheriger Deduplizierungstest bleibt erhalten.

### MUX-13, P2: Synchrone Commands ignorierten CanExecute bei direktem Aufruf

RelayCommand.Execute führte die Aktion auch bei CanExecute=false aus, anders als AsyncRelayCommand. Direkte Command-Aufrufer konnten damit Busy-/Pflichtzustandssperren umgehen.

Fix: CanExecute auch in Execute prüfen. Regression: AsyncRelayCommandTests.RelayCommand_Execute_RespectsDisabledState; zusätzlich Async-Regression für Reentry, Cancellation und beide CanExecuteChanged-Transitions. AsyncRelayCommand selbst blieb unverändert.

### MUX-14, P3: Log-Nachscrollen zerstörte Copy-Auswahl und plante redundante Dispatcherarbeit

ReadOnlyTextBoxAutoScroll setzte bei jedem Flush CaretIndex und SelectionLength zurück. Markierter Protokolltext ließ sich bei laufendem Mux nicht verlässlich kopieren. Viele Flushes erzeugten mehrere identische Scrollaufträge. Die Single-Logbox hatte im äußeren ScrollViewer außerdem keine Maximalhöhe.

Fix: Auswahl unangetastet lassen, bei aktiver Markierung nicht nachscrollen, sonst ScrollToEnd ohne Caret-Manipulation; pro TextBox nur ein ausstehender Dispatcherauftrag via ConditionalWeakTable. Single-Preview auf 320 DIP begrenzen.

Regression: ReadOnlyLogAutoScroll_PreservesSelectedText. Keine gemessene Last-/Speicherbenchmark-Aussage; normales ScrollToEnd folgt weiterhin dem bisherigen Auto-Follow-Verhalten.

### MUX-15, P3: Veralteter grüner Status nach Leeren der Einzel-Eingabe

InvalidateCurrentPlan löschte den Plan, nicht aber ein altes UpToDate-Badge. Nach Leeren des Titels blieb die Episode optisch aktuell.

Fix: ohne laufende Operation und außerhalb Shared-State-Übernahme auf Ready bei unvollständigen Eingaben bzw. ComparisonPending bei neu zu vergleichenden Eingaben wechseln. Regression: ClearingTitle_RemovesStaleUpToDateStatus.

### MUX-16, P3: Gleicher manuell gewählter Zielpfad meldete Ursprungsänderung nicht

SetOutputPath setzte den manuellen Ursprung auch bei unverändertem Pfad, dessen Setter jedoch früh zurückkehrte. UsesAutomaticOutputPath wurde nicht benachrichtigt.

Fix: die Ursprungsänderung explizit melden. Regression: SetOutputPath_SameAsAutomaticPath_NotifiesManualOriginChange.

### MUX-17, P3: Subtitle-only-Einstieg wurde als leere AD-Datei beschrieben

MainVideoDisplayText verwendete im Modus ohne primäres Video stets AudioDescriptionPath. Subtitle-only hatte deshalb eine leere/falsche Beschreibung.

Fix: neutrale Zusatzquellenbeschreibung mit MainVideoPath als Fallback. Keine Änderung an der Untertitelplanung. Bestehender Single-Test BuildFreshPlanAsync_SubtitleOnlyWithoutPrimary_DoesNotRequireAudioDescription bleibt im Filter; für die reine Textbeschreibung wurde kein eigener Test hinzugefügt.

### MUX-18, P2: Batch-Abbruch verwarf erfolgreiche Teilresultate und Reports

BatchExecutionRunner warf bei Benutzerabbruch vor Rückgabe der bereits gesammelten Ergebnisse. RunBatchAsync persistierte zudem erst nach dem optionalen Papierkorb-Cleanup. Damit konnten erfolgreiche Ausgaben ohne Dateiliste, JSON-Metadaten und gespeichertes Laufprotokoll bleiben.

Fix: BatchExecutionOutcome enthält WasCanceled und bleibt bei kontrolliertem Abbruch verfügbar. Bereits veröffentlichte Erfolge, Metadaten und gemeldete Done-Moves bleiben erhalten; nur ein tatsächlich laufender, noch nicht abgeschlossener Mux wird als Cancelled markiert. Ein Cleanup-Ergebnis mit WasCanceled stoppt auch ohne gesetzten Token weitere Episoden. RunBatchAsync persistiert das Teilergebnis bei Mux-/Done-Abbruch und vor dem optionalen Papierkorb-Schritt. Dessen späterer Abbruch ergänzt über den bestehenden Logservice einen separaten Cleanup-Abschnitt, ohne doppelte JSON-/Dateilisten. Bei Speicherfehler wird kein optionaler Papierkorb-Cleanup gestartet; Zeilen bleiben erhalten. Keine Änderung am Shared-Logservice und keine UI-Staginglösung.

Regressionen: BatchExecutionRunnerTests.ExecutePlansAsync_MarksCurrentItemCancelled_WhenUserCancelsRun und ExecutePlansAsync_AbortsBatch_WhenDoneMoveReturnsPartialCancellationState auf Teilergebnisse umgestellt; neu ExecutePlansAsync_CancelSecondMux_PreservesFirstOutputAndMetadata sowie ExecutePlansAsync_CancelDoneMove_PreservesPublishedMuxResult mit Token-OCE und Cleanup-Abbruch ohne Token. BatchMetadataReviewTests.RunBatchCommand_CancelSecondMux_PersistsSuccessfulPartialArtifacts und RunBatchCommand_CleanupCancellation_PersistsBeforeCleanup_WithoutDuplicateReports prüfen gespeicherte Dateiliste, JSON, TVDB-ID, Log und Erhalt der Zeilen. WPF-Dispatcher und bestehende PortableStorage-Testisolation; ausschließlich synthetische Testdateien, keine realen Medienprozesse.

### MUX-19, P2: Planfehler fehlten in der Abschlussstatistik

BuildExecutionWorkItemsAsync markierte Planfehler nur an Zeile und Log; die spätere Ausführungsstatistik zählte allein gestartete WorkItems. Bei ausschließlich fehlgeschlagenen Plänen erschien eine irreführende Fertig-/Skip-Meldung.

Fix: Planfehler nach der finalen Planung separat zählen und einmalig in Status und persistierte Ergebnisstatistik übernehmen. Auch Läufe ohne ausführbaren Plan erhalten eine korrekte Zusammenfassung und ein Log. Bereits aktuelle Episoden ohne Cleanup werden zusätzlich gezählt, aber nicht doppelt mit ausführbaren Skip-Plänen. Fehlerhafte Zeilen werden beim Abschluss nicht mehr automatisch geleert.

Regression: BatchMetadataReviewTests.RunBatchCommand_PlanFailures_AreIncludedInStatusAndPersistedLog für ausschließlich fehlerhafte Planung sowie gemischten Lauf mit einem Erfolg. Fehlende synthetische Quellen lösen den Planfehler vor Werkzeugausführung aus.

### MUX-20, P3: Custom-Ausgabe wurde als Bibliotheksdatei beschriftet

ArchiveState prüft die Existenz am tatsächlichen OutputPath, nicht die Zugehörigkeit zur Bibliothek. Spaltenkopf und Tooltip suggerierten dennoch einen Bibliotheksstatus.

Fix: Batch-Spaltenkopf "Zieldatei" und neutrale Ausgabeziel-Texte in BuildArchiveStateTooltip. Keine Änderung der Zielpfad- oder Archivregeln. Regressionen: bestehender Custom-Output-Test prüft den neutralen Tooltip; BatchPlanReviewButton_UsesSameReadableMinimumWidth prüft zusätzlich den Spaltenkopf.

### MUX-21, P1: Einzel- und Batch-Mux konnten parallel gestartet werden

MuxModuleViewModel erlaubte während einer Operation den Tabwechsel; beide Kinder besaßen unabhängige Command-Sperren. Ein bereits geladener zweiter Tab konnte dadurch ebenfalls mutierende Vorgänge starten.

Fix: SingleEpisodeMuxViewModel, BatchMuxViewModel und MuxModuleViewModel implementieren das vom Parent bereitgestellte IModuleInteractionState. Der Wrapper aggregiert IsInteractive und meldet Änderungen an die Shell. SelectedTabIndex lehnt Wechsel während einer Operation ab; nur der inaktive Tab wird deaktiviert, damit der aktive Abbruchknopf erreichbar bleibt. Der Wrapper überträgt zusätzlich den eigenen Busy-Zustand jedes Kindes als Geschwistersperre auf das andere. Dessen IsInteractive und sämtliche normalen Command-Prädikate berücksichtigen sie, einschließlich direkter Execute-/ExecuteAsync-Aufrufe auf dem UI-Thread. Cancel bleibt separat steuerbar. Keine neue DI-Schicht, kein globaler Mutex und keine Änderung am Parent-Interface.

Regressionen: BatchMetadataReviewTests.MuxModule_BusyChild_BlocksTabSwitchAndSiblingCommands für beide Richtungen, PropertyChanged, CanExecuteChanged, direkte synchrone/asynchrone Commands und Freigabe nach Abschluss. PlanReviewLayoutTests.MuxTabs_DisableOnlyInactiveTab_AndKeepCancelEnabled für beide Tabs mit echten WPF-Views. Der Parent prüft die Shell-Sperre für Settings und andere Module separat.

## Offene Findings und Integrationsrisiken

Die zuvor offenen zwei P2, der Custom-Output-P3 und die normale tabübergreifende UI-Konkurrenz sind durch den ausdrücklich autorisierten Nachtrag oben bearbeitet. Folgende Grenzen bleiben:

- Hinweis, Nicht-UI-Grenze: Die Geschwistersperre gilt für die zusammen im MuxModuleViewModel gehaltenen VMs und serialisierte UI-Thread-Commands. Unabhängig konstruierte VM-Paare, direkte Property-/Service-Manipulation, gleichzeitige Fremdthread-Aufrufe oder mehrere App-Prozesse werden nicht global serialisiert. Kein dateisystemweites Lock behauptet.
- Hinweis, Persistierung: Kontrollierte Abbrüche erhalten Teilergebnisse; Prozessabsturz, Stromausfall oder dauerhaft nicht beschreibbarer Logdatenträger sind keine neue Durabilitätsgarantie. Ein Fehler beim Cleanup-Lognachtrag wird gemeldet, während die vorher gespeicherten Mux-Reports erhalten bleiben.
- Hinweis, UI-Thread-I/O: EpisodeEditModel.RefreshArchiveState, OpenOutput-CanExecute und weitere UI-Pfadprüfungen verwenden File.Exists; auf langsamen Netzlaufwerken kann das UI warten. Cache-/Dateisystemdienste gehören nicht zu diesem Schreibbereich. Kein gemessener Perf-Befund für konkrete Laufwerke.
- Hinweis, BatchEpisodeCollectionController pollt eine offene Edit-Transaktion per erneutem ContextIdle-BeginInvoke. Das aktuelle Batch-Grid ist read-only; für andere/künftige lang laufende Edit-Transaktionen besteht ungebremste Idle-Arbeit. Keine Performance-Messung und kein Refactoring in dieser Runde.
- Hinweis, Pfadidentität: Batch-Ausgabeprüfungen normalisieren Vollpfade; der Cleanup-Quellschutz vergleicht die vom Planner gelieferten Pfade case-insensitiv. Unterschiedliche Reparse-/Hardlink-/UNC-Aliase derselben Datei sind kein gelöstes globales Identitätsproblem.
- Hinweis, konservatives Cleanup behält gemeinsam genutzte Quellen absichtlich zurück. Der Benutzer muss diese später separat aufräumen. Das ist ein bewusstes Sicherheitsverhalten, keine vollständige neue Dependency-/Cleanup-Engine.
- Hinweis zum Parent-Fix: keine UI-Staginglösung hinzugefügt. MuxExecutionService/MuxWorkflowCoordinator und die finale Dateiveröffentlichung liegen beim Parent. UI wertet weiterhin Ergebnis, Ausgabesnapshot und Exit-Code über den bestehenden Klassifizierer aus. Eine eigene End-to-End-Abnahme mit echten Prozessen für Exit 0/1, Fehler und Cancellation erfolgte nicht; zentraler Prüfstand siehe oben.

## Abdeckung

### Geprüfte Produktionsdateien

ViewModels/Commands: AsyncRelayCommand.cs, RelayCommand.cs.

ViewModels/Modules: BatchEpisodeCollectionController.cs, BatchEpisodeItemViewModel.cs, BatchExecutionRunner.cs, BatchExecutionWorkItem.cs, BatchRunProgressTracker.cs, BatchOperationController.cs, BatchMuxViewModel.cs, BatchMuxViewModel.Scan.cs, BatchMuxViewModel.Detection.cs, BatchMuxViewModel.Planning.cs, BatchMuxViewModel.Review.cs, BatchMuxViewModel.ReviewWorkflow.cs, BatchMuxViewModel.Execution.cs, SingleEpisodeMuxViewModel.cs, SingleEpisodeMuxViewModel.Selection.cs, SingleEpisodeMuxViewModel.Execution.cs, SingleEpisodeManualTitlePolicy.cs, EpisodeEditModel.cs, EpisodeEditModel.Properties.cs, EpisodeEditModel.Mutations.cs, EpisodeEditTextBuilder.cs, EpisodeUiStates.cs, EpisodeUiStyleBuilder.cs, DebouncedRefreshController.cs, InspectableFileOpenHelper.cs, MuxModuleViewModel.cs, MuxLanguageOverrideOption.cs.

Views: BatchMuxView.xaml, BatchMuxView.xaml.cs, SingleEpisodeMuxView.xaml, SingleEpisodeMuxView.xaml.cs, MuxModuleView.xaml, MuxModuleView.xaml.cs, EpisodeUsageSummaryView.xaml, EpisodeUsageSummaryView.xaml.cs, MuxCommonStyles.xaml, DataGridSelectionInput.cs, ReadOnlyTextBoxAutoScroll.cs. View-Prüfung war statische Struktur-/Binding-/Eingabeprüfung plus ergänzte WPF-Tests, keine eigene vollständige visuelle Laufzeitabnahme aller Fensterzustände.

### Geprüfte Tests und Kontext

Zugehörige Tests gelesen, in großen Dateien die relevanten Abschnitte/Helper: AsyncRelayCommandTests, BatchExecutionRunnerTests, BatchMetadataReviewTests, BatchOperationControllerTests, BatchEpisodeCollectionControllerTests, DebouncedRefreshControllerTests, EpisodeEditModelManualCheckTests, EpisodeUiStateTests, SingleEpisodeMuxViewModelTests, PlanReviewLayoutTests, SelectionGridInteractionTests. CleanupCancellationViewModelTests nur als Integrations-/Filterreferenz geprüft, nicht bearbeitet.

Nur lesender Kontext außerhalb Scope: EpisodePlanCache, EpisodePlanInputSnapshot, EpisodeDetectionWorkflow, SeriesEpisodeMuxPlan-Signaturen, ViewModelTestContext, BatchRunArtifactPersistence, BatchRunLogService, PortableStorageCollection und das neue Parent-Interface IModuleInteractionState. Keine vollständige Shared-Service-Reviewbehauptung.

Nicht abgedeckt: reale Archive, Netzwerkshares, echte Player-/TVDB-Dialoge und MKVToolNix-Prozesse, dauerhaft hohe Batch-Last, Shutdown während aller asynchronen Phasen, High-DPI-/Screenreader-Abnahme, Pfadidentität über Dateisystemaliase. Die ursprünglichen Regressionen und die Nachtragsregressionen sind im bestätigten grünen Unit-/WPF-Gesamtlauf enthalten. Automatisierte Tests ersetzen keine manuelle Laufzeitabnahme der genannten Bereiche; der separate Integrationslauf des Parents ist noch nicht abgeschlossen gemeldet.

## Geänderte Dateien

```text
ViewModels/Commands/RelayCommand.cs
ViewModels/Modules/BatchEpisodeItemViewModel.cs
ViewModels/Modules/BatchExecutionRunner.cs
ViewModels/Modules/BatchMuxViewModel.cs
ViewModels/Modules/BatchMuxViewModel.Detection.cs
ViewModels/Modules/BatchMuxViewModel.Execution.cs
ViewModels/Modules/BatchMuxViewModel.Planning.cs
ViewModels/Modules/BatchMuxViewModel.Review.cs
ViewModels/Modules/BatchMuxViewModel.ReviewWorkflow.cs
ViewModels/Modules/DebouncedRefreshController.cs
ViewModels/Modules/EpisodeEditModel.cs
ViewModels/Modules/EpisodeEditModel.Properties.cs
ViewModels/Modules/EpisodeEditModel.Mutations.cs
ViewModels/Modules/EpisodeEditTextBuilder.cs
ViewModels/Modules/MuxModuleViewModel.cs
ViewModels/Modules/SingleEpisodeMuxViewModel.cs
ViewModels/Modules/SingleEpisodeMuxViewModel.Selection.cs
ViewModels/Modules/SingleEpisodeMuxViewModel.Execution.cs
Views/BatchMuxView.xaml
Views/SingleEpisodeMuxView.xaml
Views/MuxModuleView.xaml
Views/DataGridSelectionInput.cs
Views/ReadOnlyTextBoxAutoScroll.cs
MkvToolnixAutomatisierung.Tests/ViewModels/AsyncRelayCommandTests.cs
MkvToolnixAutomatisierung.Tests/ViewModels/BatchExecutionRunnerTests.cs
MkvToolnixAutomatisierung.Tests/ViewModels/BatchMetadataReviewTests.cs
MkvToolnixAutomatisierung.Tests/ViewModels/DebouncedRefreshControllerTests.cs
MkvToolnixAutomatisierung.Tests/ViewModels/EpisodeEditModelManualCheckTests.cs
MkvToolnixAutomatisierung.Tests/ViewModels/SingleEpisodeMuxViewModelTests.cs
MkvToolnixAutomatisierung.Tests/Views/PlanReviewLayoutTests.cs
MkvToolnixAutomatisierung.Tests/Views/SelectionGridInteractionTests.cs
docs/reviews/2026-09-22/mux-ui.md
```

## Zentrale Testfilter

Direkte Regressionen, Filter zur Reproduktion durch Parent:

```text
FullyQualifiedName~AsyncRelayCommandTests|FullyQualifiedName~BatchExecutionRunnerTests|FullyQualifiedName~BatchMetadataReviewTests|FullyQualifiedName~DebouncedRefreshControllerTests|FullyQualifiedName~EpisodeEditModelManualCheckTests|FullyQualifiedName~SingleEpisodeMuxViewModelTests|FullyQualifiedName~PlanReviewLayoutTests|FullyQualifiedName~SelectionGridInteractionTests
```

Angrenzende bestehende Regressionen:

```text
FullyQualifiedName~BatchOperationControllerTests|FullyQualifiedName~BatchEpisodeCollectionControllerTests|FullyQualifiedName~EpisodeUiStateTests|FullyQualifiedName~CleanupCancellationViewModelTests|FullyQualifiedName~EpisodePlanCacheTests|FullyQualifiedName~BatchRunArtifactPersistenceTests
```

Für den Nachtrag gezielt: `FullyQualifiedName~BatchExecutionRunnerTests|FullyQualifiedName~BatchMetadataReviewTests|FullyQualifiedName~PlanReviewLayoutTests|FullyQualifiedName~CleanupCancellationViewModelTests`. Die Filter bleiben zur gezielten Reproduktion dokumentiert. Der abschließende zentrale Unit-/WPF-Gesamtlauf ist mit **1167/1167 erfolgreichen Tests** bestätigt; die anschließende Integration liegt beim Parent. Kein Test wurde durch diesen Agenten gestartet.
