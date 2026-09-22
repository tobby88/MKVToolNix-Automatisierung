# Tooling-, Startup- und Settings-Review

Stand: 2026-09-22. Ausgang: `master` / `323634f`. Der bestehende Untertitel-Fix wurde nicht berührt. Alle eigenen Änderungen liegen im zugewiesenen Bereich. Kein Staging, Commit, Push, Paketupdate oder eigener Build-/Test-/Formatlauf. Andere Arbeitsbereiche wurden nur an Schnittstellen gelesen.

## Findings und Umsetzung

### T01 - P1: Bereinigung kann portable Benutzerdaten löschen (behoben)

`Services/ManagedToolInstallerService.cs`, vormals `CleanupOlderManagedVersions` und `ReplaceVersionDirectoryWithStaging`: Die Bereinigung löschte alle anderen Unterverzeichnisse. Bei MediathekView schützte nur ein Verzeichnis namens `Downloads` vor der Löschung. Andere Aufnahmeordner, lose Videos und weitere portable Daten waren ungeschützt. Beim Ersetzen derselben Version wurde sogar die gesamte `.replaced-*`-Sicherung sofort gelöscht.

Fix: Alte MediathekView-Installationen bleiben grundsätzlich erhalten, ebenso Sicherungen bei einer Reparatur derselben Version. Bei MKVToolNix/ffprobe wird nur noch die explizit vorher referenzierte Version unter dem jeweiligen Toolroot bereinigt, nicht jeder beliebige Nachbarordner. Die Einstellungen werden weiterhin in das neue Staging kopiert. Rückverknüpfungen bei dieser Kopie werden abgelehnt; die Dateikopie ist abbrechbar.

Regressionen: `PreservesArbitraryMediathekDataDuringUpdateAndRepair` (Update und Reparatur), `DoesNotCleanUpUnreferencedDirectories`; bestehende Migrationsregression auf Erhalt der alten Einstellungen angepasst. Der vorhandene Test für einen Ordner namens `Downloads` bleibt erhalten.

### T02 - P2: Abbruch oder Speicherfehler nach Installation verliert den referenzierten Zustand (behoben)

`ManagedToolInstallerService.EnsureManagedToolsCoreAsync`: Vorher wurden Installationspfade aller drei Werkzeuge erst ganz am Ende gespeichert, alte Versionen aber vorher gelöscht. Abbruch bei einem späteren Werkzeug hinterließ veraltete Pfade bzw. bereits entfernte Vorgängerversionen.

Fix: Jede abgeschlossene Werkzeugaktion wird vor dem nächsten Werkzeug gespeichert. Erst nach erfolgreicher Persistenz darf die vorherige Version bereinigt werden. Ein Abbruch nach der Installation beendet den restlichen Ablauf, nicht den notwendigen Zustands-Commit. Bereits abgebrochene Aufrufe starten keine Arbeit.

Regressionen: `PersistsCompletedToolBeforeCancellationOfNextTool`, `KeepsPreviousVersionWhenSavingStateFails`; bestehende Cancellation-/Backofftests bleiben relevant.

### T03 - P2: Paketnamen können den Installationsbereich verlassen (behoben)

`ManagedToolInstallerService.ValidatePackagePaths`: `SanitizePathSegment("..")` lieferte unverändert `..`. Der Archivname wurde ungeprüft an den Downloadpfad angehängt. Metadaten mit Punktsegmenten oder Pfadseparatoren konnten daher Ziele außerhalb des vorgesehenen Versionsordners ergeben.

Fix: Validierung vor dem Download, Ablehnung von Punkt-/internen Verzeichnisnamen, ungültigen Archivnamen und inkonsistentem Tool-Kind. Die Versionsnamen werden weiterhin für erlaubte Dateinamen normalisiert.

Regression: `RejectsUnsafePackagePathsBeforeDownload` mit sechs Namen einschließlich `..`, `.staging-*`, Separator und ADS-Doppelpunkt. Die HTTP- und Extraktionsaufrufe müssen ausbleiben.

### T04 - P2: Extraktion kann bestehende Dateien überschreiben und Windows-Pfadaliasse zulassen (behoben)

`Services/ManagedToolArchiveExtractor.cs`: Trotz dokumentiert leerem Ziel war `FileMode.Create` aktiv. Mehrfache Archiveinträge konnten sich gegenseitig überschreiben; ADS-Namen, Windows-Gerätenamen und Pfadaliasse mit Punkten/Leerzeichen waren nicht explizit ausgeschlossen. Ein bestehendes verknüpftes Ziel wurde nicht erkannt.

Fix: Leeres Ziel voraussetzen, alle ausgewählten Zielpfade vor dem ersten Schreibzugriff prüfen, case-insensitive Duplikate ablehnen, `CreateNew` verwenden, ADS-/Geräte-/Aliasnamen und bestehende Reparse-Verzeichnisse ablehnen. Vorab-Cancellation legt keinen Ordner mehr an. Der Installer entfernt weiterhin sein eigenes Staging bei Fehlern.

Regressionen: `PreflightsUnsafePathsBeforeWritingAnyPayload` (sechs Pfade), `RejectsDuplicateWindowsPaths`, `RefusesNonemptyDestinationWithoutOverwritingFiles`; vorhandener Pre-Cancellation-Test angepasst. Keine Symlink-Privilegien für diese Tests erforderlich.

### T05 - P2: Synchrones Installer-I/O und zu viele Fortschrittsmeldungen blockieren WPF (behoben)

`ManagedToolInstallerService.EnsureManagedToolsAsync` und `ToolStartupProgressReporter`: Async-Methoden liefen auf dem aufrufenden SynchronizationContext weiter. Rekursive Dateisuche, Verzeichnistausch und Settings-Kopie konnten damit den Dispatcher blockieren. Jeder gelesene Datenblock erzeugte zudem einen UI-Post.

Fix: Installer insgesamt auf einem Worker ausführen, innerhalb des Prozesses mit einem gemeinsamen `SemaphoreSlim` serialisieren, Einstellungen dateiweise asynchron und abbrechbar kopieren. Fortschrittsmeldungen derselben Phase sind auf höchstens ca. zehn pro Sekunde begrenzt; Phasenwechsel und Abschluss bleiben sichtbar.

Regression: `RunsExtractionOutsideWpfDispatcher` prüft Worker-Extraktion und Rückkehr des Aufrufers zum Dispatcher. Bestehende Progress-Tests prüfen weiterhin Monotonie. Kein separater Stress-/Mehrprozess-Test.

### T06 - P2: Startfortschritt ohne Dispatcherbindung und zu späte Cancellation (behoben)

`Program.cs`: `Progress<T>` wurde vor `app.Run()` erstellt und konnte daher ohne Dispatcher-SynchronizationContext auf Threadpool-Callbacks zurückfallen. Bereits gepostete Meldungen konnten zudem den Abbruchstatus ersetzen.

Fix: Fortschrittsobjekt im `ContentRendered`-Startpfad erstellen, Meldungen nach Abbruch/Abschluss ignorieren und Cancellation vor dem Anzeigen des Hauptfensters erneut prüfen. `AppBootstrapper` lehnt bereits abgebrochene, wiederholte, entsorgte und dispatcherfremde Erzeugungsaufrufe ab. Eine nach Cancellation/Dispose erhaltene Komposition bzw. eine beim Fensterbau fehlgeschlagene Komposition wird entsorgt.

Regressionen in `StartupBootstrapperTests`: fehlender Dispatcher, bereits entsorgter Bootstrapper, vorab abgebrochener Start. Der bestehende `Composition.AppBootstrapperTests`-Test bleibt unverändert. Der gesamte `Program.Main`-Lebenszyklus wurde nicht ausgeführt.

### T07 - P2: Settings-Dialog schreibt veraltete Installationsmetadaten zurück (behoben)

`AppSettingsWindowViewModel.SaveSettings`: Die beim Öffnen geklonten Tool-/IMDb-Objekte ersetzten beim Speichern die aktuellen Objekte vollständig. Eine zwischenzeitliche Installation oder ein Wiederholungsversuch nach Abbruch konnte damit neue Pfade, Versionen und Zeitstempel zurücksetzen.

Fix: Nur editierbare Optionen in den aktuellen Storezustand schreiben. Nach Speichern sowie nach Abschluss/Fehler/Abbruch der Ressourcenprüfung werden die verwalteten Laufzeitdaten für die Anzeige neu geladen. `AppSettingsStore` und Composition bleiben unverändert.

Regression: `SaveSettings_PreservesRuntimeStateUpdatedAfterOpeningDialog` prüft Toolpfad, Version, Zeitstempel, IMDb-Zeitstempel, AutoManage-Option und Tooltip.

### T08 - P2: Bereits abgebrochener oder paralleler Settings-Aufruf kann trotzdem speichern (behoben)

`SaveSettingsAndEnsureManagedToolsAsync`: Vorab-Cancellation wurde erst von den Ressourcenservices beobachtet, nachdem Einstellungen gespeichert waren. Reentrante Aufrufe konnten die aktive CancellationSource ersetzen. Gepostete alte Fortschritte konnten den endgültigen Status überschreiben.

Fix: Cancellation und Busy-Zustand vor dem Speichern prüfen; parallele Ressourcen-/Emby-Prüfungen ablehnen. Fortschritt an genau den aktuellen Vorgang binden, nach dessen Ende oder Cancellation ignorieren. Cancellation auch zwischen Ressourcenphasen prüfen.

Regressionen: `DoesNotSaveWhenAlreadyCancelled`, `RejectsConcurrentOperations`, `QueuedProgressCannotOverwriteCompletion` in `AppSettingsWindowViewModelTests`.

### T09 - P2: MediathekView-Ordnersuche bevorzugt Standardstart statt portabler Einstellungen (behoben)

`MediathekViewPathResolver.TryFindExecutableUnderRoot`: Eine `MediathekView.exe` im Root gewann vor einer verschachtelten `Portable/MediathekView_Portable.exe`, obwohl `preferPortable` aktiviert war. Das widersprach der Installer-Auswahl und konnte ein anderes Profil starten.

Fix: Erst die bevorzugte Executable rekursiv suchen, dann die alternative. Explizit konfigurierte Executable-Pfade behalten weiterhin Vorrang.

Regression: `PathResolver_PrefersPortableExecutableWhenConfiguredPathIsDirectory` nutzt jetzt die reale Root-/Portable-Unterordnerstruktur.

### T10 - P2: Recovery-Verzeichnisse und unvollständige Paare beeinflussen Toolauflösung (behoben)

`ManagedToolModels.cs` und `MediathekViewLauncher.cs`: `.replaced-*` wurde als normale Version durchsucht. Die unabhängige Auswahl der kürzesten `mkvmerge.exe` und `mkvpropedit.exe` konnte ein vorhandenes vollständiges Paar übersehen. Die ffprobe-Downloadsuche materialisierte ihre rekursive Enumeration erst außerhalb des vorgesehenen Catch-Blocks.

Fix: Interne Punktverzeichnisse nicht als Fallback starten. MKVToolNix pro gemeinsamem Ordner auflösen, sowohl im Installer als auch im Locator. Rekursive ffprobe-Enumeration innerhalb der abgesicherten Suche materialisieren.

Regressionen: `Locators_IgnoreTemporaryAndRecoveryDirectories`, erweiterter MKVToolNix-Fallbacktest mit verwaistem kürzerem Executable, erweiterter MediathekView-Fallbacktest mit neuerer Recovery-Kopie.

### T11 - P3: Checksum-Suffix kann eine fremde Datei treffen (behoben)

`ManagedToolParsing.TryReadSha256FromChecksumText`: `EndsWith(archiveFileName)` akzeptierte z.B. `not-tool.zip` für `tool.zip` und beendete die Suche an einer ungültigen Zeile.

Fix: Ganzen Dateinamen vergleichen, übliche `*`-/`./`-Präfixe berücksichtigen und ungültige Zeilen überspringen.

Regression: `TryReadSha256FromChecksumText_MatchesWholeFilenameAndSkipsInvalidLines`.

### T12 - P2: Download-Modul sucht und startet synchron auf dem Dispatcher (behoben)

`DownloadViewModel`: Konstruktor, Neu-suchen- und Startaktion durchliefen die synchronen Dateisuchen/den Prozessstart auf dem UI-Thread. Fehler aus der Suche waren nicht zentral abgefangen. Nach einem abgebrochenen Settingsdialog erfolgte keine Aktualisierung, obwohl dieser bereits gespeichert haben konnte.

Fix: Awaitbare `AsyncRelayCommand`s, Worker für Auflösung und Start, gegenseitige Busy-Sperre und Fehlerbehandlung; UI-Updates bleiben auf dem aufrufenden Dispatcher. Erste Suche ist als `Initialization` awaitbar. Nach dem Settingsdialog wird unabhängig vom booleschen Dialogergebnis neu aufgelöst. Während einer Suche eingehende Settingsänderungen werden nachgezogen.

Regressionen: bestehende Downloadtests awaitbar angepasst, Dialogergebnis true/false abgedeckt, `Commands_RunLookupAndLaunchOutsideDispatcherAndNotifyOnDispatcher`.

### T13 - P3: Kleine belegbare UI-Unstimmigkeiten (behoben)

- `Windows/AppSettingsWindow.xaml`: Ein langer Status im ersten DockPanel-Kind konnte den rechten Buttons die Breite nehmen. Fix: Stern-/Auto-Grid, Ellipse und voller Tooltip. Regression: `Window_ReservesFooterWidthForActionsWhenStatusIsLong`.
- `SelectMediathekViewPath`: Der erste Filter blendete den bevorzugten portablen Launcher aus. Er enthält jetzt beide Executable-Namen. Statisch geprüft.
- `BuildExternalSourceTooltip`: Die Zusage, bei vorhandenem externen Tool werde kein Download erzwungen, widersprach dem Installer. Tooltip beschreibt nun den Fallback und die mögliche bevorzugte verwaltete Installation. Statisch geprüft.
- `Views/DownloadView.xaml`: Der eigene Button-Template ignorierte `Padding`. Der Border übernimmt es jetzt. XML-Prüfung, kein eigener visueller Lauf.

### T14 - P3: Ungültige Prozentwerte gelangen direkt in WPF (behoben)

`StartupProgressWindowViewModel.Report`: NaN, Infinity und Werte außerhalb 0..100 wurden unverändert übernommen.

Fix: Endliche Werte begrenzen, nicht messbare Werte indeterminiert mit neutralem Zahlenwert anzeigen.

Regression: `Report_NormalizesInvalidProgressValues` mit fünf Grenzfällen.

## Verifikation und Integration

- Eigene statische Prüfung: `git diff --check` für die geänderten Bereichsdateien ohne Beanstandung; XML-Parsing von AppSettingsWindow, StartupProgressWindow und DownloadView erfolgreich; keine doppelten Testmethodennamen in den betroffenen Testklassen.
- C#-Syntax-only-Parsing mit dem bereits in PowerShell geladenen Roslyn 5.0: 20 geänderte/neue C#-Dateien ohne Syntaxfehler. Das ist ausdrücklich keine semantische Kompilierung und kein WPF-XAML-Build. Ein vorheriger Versuch, SDK-Roslyn explizit zu laden, kollidierte mit der bereits geladenen Assembly; der anschließende reine Parse-Lauf verwendete nachweislich die geladene Version und war erfolgreich.
- Zentraler vollständiger Unit-/WPF-Lauf laut Parent: **1081/1081 Tests erfolgreich**, einschließlich `EnsureManagedToolsAsync_DoesNotTreatLegacyDownloadOverrideAsManualOverride`. Der zuvor gemeldete Fehler ist im vollständigen Wiederholungslauf nicht mehr aufgetreten; dort besteht kein offener Testfehler. Die ergänzte diagnostische Assertion bleibt erhalten. Ein gesonderter funktionaler Fehlerfix wird hierfür nicht behauptet.
- Keine eigenen Tests ausgeführt. Alle Testdateien und synthetischen Archive verwenden den bestehenden isolierten PortableStorage-Testoutput oder GUID-Tempverzeichnisse; keine echten Archivdateien oder Benutzeraufnahmen wurden angefasst.

## Verbleibende Risiken und Hinweise

- Hinweis: Die Installer-Sperre gilt pro Prozess, nicht zwischen zwei gleichzeitig gestarteten App-Instanzen. Mehrprozess-Settings-Transaktionen und atomarer Verzeichnistausch inklusive Settings-Datei erfordern eine übergreifende Entscheidung; nicht durch diesen Bereich gelöst.
- Hinweis: MediathekView-Rückfallkopien werden bewusst nicht automatisch gelöscht. Das benötigt zusätzlichen Speicher. Bei der Reparatur derselben Version liegen Altaufnahmen ggf. unter `.replaced-*`; es gibt noch keine UI zum Wiederherstellen/Bereinigen.
- Hinweis: Ein bereits laufendes MediathekView kann nach der Kopie seine alten Einstellungen weiter verändern. Die alte Installation bleibt erhalten, aber ein konsistenter Snapshot gegen ein fremdes schreibendes Programm ist nicht garantiert.
- Hinweis: Archive werden nicht gegen Entpack-Bomben mit Gesamtgrößenlimit abgesichert. Bestehende Reparse-Ziele werden geprüft, aber kein Schutzversprechen gegen einen lokalen Angreifer, der Pfade exakt zwischen Prüfung und Dateizugriff austauscht. Link-/Junction- und Low-Disk-/Dateisperren-Szenarien wurden nicht dynamisch getestet.
- P3, offen: `PreferredDownloadDirectoryHelper` nimmt weiterhin `USERPROFILE/Downloads` an und berücksichtigt keine Windows-Known-Folder-Umleitung. Das ist eine bestehende funktionale Einschränkung; keine Registry-/PInvoke-Erweiterung in der Konsolidierung.
- P3, offen: Settings-Toolstatus löst Pfade im Konstruktor und nach Feld-/Checkboxänderungen weiterhin synchron auf. Der lange Installer- und Download-Modulpfad ist ausgelagert, ein umfassendes Debouncing dieses Dialogs nicht enthalten.
- P3, offen: Das fixe Startup-Fenster kann bei sehr langen mehrzeiligen Fehlerdetails knapp werden. Kein visueller DPI-/Zoom-/Accessibility-Test in diesem Lauf.
- Hinweis: Fallbackreihenfolge bleibt nach Verzeichnis-LastWriteTime statt semantischer Version. Legacy-Overrides unter Downloads werden wie bisher normalisiert; explizite Benutzerwahl und historischer Legacy-Eintrag sind dort nicht unterscheidbar.
- Hinweis: `CreateMainWindow()` ist kein unterstützter Produktionsstartpfad; der echte Start verwendet bereits `CreateMainWindowAsync()` auf dem Dispatcher. Die bestehende synchrone Methode scheitert nun auch vor Serviceaufbau ohne Dispatcher statt spät beim WPF-Fensterbau.
- Übergreifend: Parent behebt JSON-null in `AppToolPathSettings.Clone` und Root-null-/Backup-Handling. Diese Dateien wurden hier nicht editiert. Globale Settingsänderungsbenachrichtigungen nach bereits gespeichertem, dann abgebrochenem Dialog betreffen auch andere Module; dieses Review aktualisiert nur das Download-Modul direkt nach Dialogende.

## Geprüfte Dateien und Abdeckung

Vollständig gelesen: `Services/ManagedToolArchiveExtractor.cs`, `ManagedToolInstallerService.cs`, `ManagedToolModels.cs`, `ManagedToolPackageSources.cs`, `MediathekViewLauncher.cs`, `FfprobeLocator.cs`, `MkvToolNixLocator.cs`, `ToolLocatorInterfaces.cs`, `PreferredDownloadDirectoryHelper.cs`; `AppBootstrapper.cs`, `Program.cs`; `ViewModels/AppSettingsWindowViewModel.cs`, `ViewModels/StartupProgressWindowViewModel.cs`, `ViewModels/Modules/DownloadViewModel.cs`; `Windows/AppSettingsWindow.xaml` und `.xaml.cs`, `Windows/StartupProgressWindow.xaml` und `.xaml.cs`, `Views/DownloadView.xaml` und `.xaml.cs`.

Zugehörige Tests gelesen und gezielt erweitert: ManagedToolInstallerServiceTests, ManagedToolArchiveExtractorTests, ManagedToolPackageSourceTests, ToolLocatorTests, MediathekViewLauncherTests, AppSettingsWindowViewModelTests, StartupProgressWindowViewModelTests, DownloadViewModelTests, AppSettingsWindowTests. StartupProgressWindowTests gelesen; StartupBootstrapperTests neu.

Nur Schnittstellen-/Kontextprüfung, keine Änderung: `AppCompositionRoot.cs`, `Services/AppSettingsStore.cs`, `Services/AppToolPathStore.cs`, `Services/PortableAppStorage.cs`, `Services/PathComparisonHelper.cs`, Emby-/IMDb-Schnittstellen, Relay-/AsyncRelayCommand, WpfTestHost und PortableStorageFixture. Bestehende breite Testdateien wurden an den relevanten Fällen/Fixtures geprüft, nicht als vollständige unabhängige Testcode-Auditierung.

Nicht abgedeckt: echte Netzwerkendpunkte/aktuelle Download-HTMLs, reale Java-/MediathekView-/MKVToolNix-Starts, Signatur-/Lieferkettenprüfung, reale portable Profilformate außerhalb `Einstellungen`, Installer-Wiederherstellung nach Prozessabsturz/Stromausfall, mehrprozessige Konkurrenz, echter Screenreader-/High-DPI-Lauf. Keine Paketversionen geändert.

## Eigene geänderte Dateien

```text
AppBootstrapper.cs
Program.cs
Services/ManagedToolArchiveExtractor.cs
Services/ManagedToolInstallerService.cs
Services/ManagedToolModels.cs
Services/ManagedToolPackageSources.cs
Services/MediathekViewLauncher.cs
ViewModels/AppSettingsWindowViewModel.cs
ViewModels/StartupProgressWindowViewModel.cs
ViewModels/Modules/DownloadViewModel.cs
Windows/AppSettingsWindow.xaml
Views/DownloadView.xaml
MkvToolnixAutomatisierung.Tests/Services/ManagedToolInstallerServiceTests.cs
MkvToolnixAutomatisierung.Tests/Services/ManagedToolArchiveExtractorTests.cs
MkvToolnixAutomatisierung.Tests/Services/ManagedToolPackageSourceTests.cs
MkvToolnixAutomatisierung.Tests/Services/ToolLocatorTests.cs
MkvToolnixAutomatisierung.Tests/Services/MediathekViewLauncherTests.cs
MkvToolnixAutomatisierung.Tests/ViewModels/AppSettingsWindowViewModelTests.cs
MkvToolnixAutomatisierung.Tests/ViewModels/StartupProgressWindowViewModelTests.cs
MkvToolnixAutomatisierung.Tests/ViewModels/DownloadViewModelTests.cs
MkvToolnixAutomatisierung.Tests/Windows/AppSettingsWindowTests.cs
MkvToolnixAutomatisierung.Tests/Windows/StartupBootstrapperTests.cs
docs/reviews/2026-09-22/tooling.md
```

## Zentrale Testfilter

Optionaler gezielter Regressionsfilter (kein offener Fehler):

```text
FullyQualifiedName~ManagedToolInstallerServiceTests.EnsureManagedToolsAsync_DoesNotTreatLegacyDownloadOverrideAsManualOverride
```

Filter für spätere gezielte Wiederholungen des betroffenen Bereichs:

```text
FullyQualifiedName~ManagedToolInstallerServiceTests|FullyQualifiedName~ManagedToolArchiveExtractorTests|FullyQualifiedName~ManagedToolPackageSourceTests|FullyQualifiedName~ToolLocatorTests|FullyQualifiedName~MediathekViewLauncherTests|FullyQualifiedName~AppSettingsWindowViewModelTests|FullyQualifiedName~StartupProgressWindowViewModelTests|FullyQualifiedName~DownloadViewModelTests|FullyQualifiedName~AppSettingsWindowTests|FullyQualifiedName~StartupProgressWindowTests|FullyQualifiedName~StartupBootstrapperTests|FullyQualifiedName~AppBootstrapperTests|FullyQualifiedName~ToolingCompositionModuleTests
```

Der zentrale vollständige Unit-/WPF-Gesamtlauf ist laut Parent mit 1081/1081 Tests erfolgreich abgeschlossen. Danach wurden ausschließlich dieser Bericht und neu hinzugefügte deutsche Kommentare sprachlich korrigiert; keine funktionale Erweiterung und kein eigener Build-/Testlauf.
