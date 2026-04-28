using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Input;

namespace ReadmeScreenshotGenerator;

/// <summary>
/// Liefert kompakte Demo-Daten, die exakt auf die README-Screenshots zugeschnitten sind.
/// </summary>
internal static class DemoData
{
    private static readonly ICommand NoOpCommand = new NoOpRelayCommand();

    public static SolidColorBrush Brush(string hex)
    {
        return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    }

    public static DemoDownloadViewModel CreateDownloadViewModel()
    {
        return new DemoDownloadViewModel
        {
            IsMediathekViewAvailable = true,
            MediathekViewStatusText = "MediathekView bereit (portable)",
            MediathekViewPathText = @"C:\Users\tobby\Downloads\MediathekView\MediathekView_Portable.exe",
            StatusText = "MediathekView kann gestartet werden. Nach dem Download geht es mit Einsortieren weiter.",
            StartMediathekViewCommand = NoOpCommand,
            OpenToolSettingsCommand = NoOpCommand,
            RefreshCommand = NoOpCommand
        };
    }

    public static DemoMuxModuleViewModel CreateMuxModuleViewModel(int selectedTabIndex)
    {
        return new DemoMuxModuleViewModel
        {
            SingleMux = CreateSingleViewModel(),
            BatchMux = CreateBatchViewModel(),
            SelectedTabIndex = selectedTabIndex
        };
    }

    public static DemoSingleViewModel CreateSingleViewModel()
    {
        var languageOptions = new ObservableCollection<DemoLanguageOption>
        {
            new("", "Automatisch"),
            new("de", "Deutsch"),
            new("en", "English")
        };

        return new DemoSingleViewModel
        {
            MainVideoPath = @"C:\Demo\Downloads\MediathekView\Neues aus Büttenwarder\Rififi.mp4",
            HasPlanSummary = true,
            OutputTargetBadgeBackground = Brush("#FFE9F8EE"),
            OutputTargetBadgeBorderBrush = Brush("#FF63A46C"),
            OutputTargetBadgeText = "Archiv gefunden",
            OutputTargetBadgeTooltip = "Eine passende Bibliotheksdatei wird als Arbeitskopie verwendet.",
            ManualCheckBadgeBackground = Brush("#FFFFF4D6"),
            ManualCheckBadgeBorderBrush = Brush("#FFD6A62A"),
            ManualCheckBadgeText = "Hinweis offen",
            ManualCheckBadgeTooltip = "Vor dem Muxen sollte die Doppelfolge kurz geprüft werden.",
            MetadataBadgeBackground = Brush("#FFE9F2FF"),
            MetadataBadgeBorderBrush = Brush("#FF4C84D3"),
            MetadataBadgeText = "TVDB bestätigt",
            MetadataBadgeTooltip = "Metadaten wurden aus der TVDB übernommen.",
            UsageSummary = new DemoEpisodeUsageSummary
            {
                ArchiveAction = "Arbeitskopie",
                ArchiveDetails = "Vorhandene Doppelfolge bleibt Basis, frische FHD-Spur ersetzt die alte HD-Spur.",
                MainVideo = new DemoUsageGroup("Deutsch - FHD - H.264 aus frischer Quelle", true, "Deutsch - HD - H.264 aus Bibliothek", "bessere Auflösung verfügbar"),
                AdditionalVideos = new DemoUsageGroup("Plattdüütsch - HD - H.264 bleibt erhalten"),
                Audio = new DemoUsageGroup("Deutsch - AAC aus frischer Quelle, Plattdüütsch - AAC aus Archiv"),
                AudioDescription = new DemoUsageGroup("Keine AD-Quelle vorhanden"),
                Subtitles = new DemoUsageGroup("Deutsch (hörgeschädigte) - SRT wird ergänzt"),
                Attachments = new DemoUsageGroup("Rififi.txt wird neu eingebettet")
            },
            HasPendingPlanReview = true,
            PrimaryActionablePlanNote = "Doppelfolge erkannt. Vor dem Muxen bitte kurz prüfen, ob die kombinierte Archivdatei als Ziel korrekt ist.",
            RequiresManualCheck = false,
            SeriesName = "Neues aus Büttenwarder",
            SeasonNumber = "2014",
            EpisodeNumber = "05-06",
            Title = "Rififi",
            HasMetadataStatus = true,
            MetadataStatusText = "S2014E05-E06 - Rififi wurde bestätigt. Originalsprache: Deutsch.",
            MetadataActionButtonText = "TVDB prüfen",
            MetadataActionButtonTooltip = "TVDB-Auswahl öffnen.",
            AudioDescriptionPath = "Keine separate AD-Datei ausgewählt.",
            AudioDescriptionButtonText = "AD korrigieren",
            SubtitlePaths =
            [
                @"C:\Demo\Downloads\MediathekView\Neues aus Büttenwarder\Rififi.de.srt",
                @"C:\Demo\Downloads\MediathekView\Neues aus Büttenwarder\Rififi.nds.ass"
            ],
            AttachmentPaths =
            [
                @"C:\Demo\Downloads\MediathekView\Neues aus Büttenwarder\Rififi.txt"
            ],
            OutputPath = @"Z:\Videos\Serien\Neues aus Büttenwarder\Season 2014\Neues aus Büttenwarder - S2014E05-E06 - Rififi.mkv",
            LanguageOverrideOptions = languageOptions,
            VideoLanguageOverride = string.Empty,
            AudioLanguageOverride = string.Empty,
            OriginalLanguageOverride = "de",
            HasOutputTargetStatus = true,
            OutputTargetStatusText = "Ausgabe liegt im Serienarchiv. Der vorhandene Zielcontainer wird als Arbeitskopie genutzt.",
            ExecutionStatusBadgeBackground = Brush("#FFE9F8EE"),
            ExecutionStatusBadgeBorderBrush = Brush("#FF63A46C"),
            ExecutionStatusBadgeText = "Bereit",
            ExecutionStatusTooltip = "Der Einzelmodus ist bereit für Vorschau oder Mux.",
            StatusText = "Vorschau bereit. Offenen Hinweis prüfen, dann muxen.",
            ProgressValue = 0,
            PreviewText = "mkvmerge --output \"...Rififi.mkv\" --title \"Rififi\" ...\r\nGeplante Änderung: Deutsch - FHD - H.264 ersetzt Deutsch - HD - H.264.",
            CreatePreviewButtonTooltip = "Geplanten mkvmerge-Aufruf aktualisieren.",
            CancelCurrentOperationTooltip = "Kein laufender Vorgang.",
            ExecuteMuxButtonTooltip = "Mux mit den geprüften Einstellungen starten."
        };
    }

    public static DemoBatchViewModel CreateBatchViewModel()
    {
        var filterModes = new ObservableCollection<DemoChoice>
        {
            new("Alle"),
            new("Prüfung offen"),
            new("Warnung")
        };
        var sortModes = new ObservableCollection<DemoChoice>
        {
            new("Serie / Titel"),
            new("S/E"),
            new("Status"),
            new("Quelle")
        };
        var languageOptions = new ObservableCollection<DemoLanguageOption>
        {
            new("", "Automatisch"),
            new("de", "Deutsch"),
            new("nds", "Plattdüütsch"),
            new("en", "English")
        };

        var items = new ObservableCollection<DemoBatchEpisodeItem>
        {
            new()
            {
                IsSelected = true,
                Title = "Rififi",
                EpisodeCodeDisplayText = "S2014E05-E06",
                ArchiveStateText = "vorhanden",
                ArchiveStateTooltip = "Passende Bibliotheksdatei gefunden.",
                ArchiveBadgeBackground = Brush("#FFE9F8EE"),
                ArchiveBadgeBorderBrush = Brush("#FF63A46C"),
                ReviewHint = "Mehrfachfolge",
                ReviewHintTooltip = "Länge und Archivvergleich deuten auf eine Doppelfolge hin.",
                ReviewBadgeBackground = Brush("#FFFFF4D6"),
                ReviewBadgeBorderBrush = Brush("#FFD6A62A"),
                Status = "Bereit",
                StatusTooltip = "Pflichtchecks erledigt, Batch kann starten.",
                StatusBadgeBackground = Brush("#FFE9F2FF"),
                StatusBadgeBorderBrush = Brush("#FF4C84D3"),
                MainVideoFileName = "Neues aus Büttenwarder - Rififi.mp4",
                SeriesName = "Neues aus Büttenwarder",
                SeasonNumber = "2014",
                EpisodeNumber = "05-06",
                TitleForMux = "Rififi",
                MetadataDisplayText = "S2014E05-E06 - Rififi",
                MetadataStatusText = "TVDB-Zuordnung bestätigt und Archivvergleich geprüft.",
                RequestedSourcePaths =
                [
                    @"C:\Demo\Downloads\MediathekView\Neues aus Büttenwarder\Rififi.mp4",
                    @"C:\Demo\Downloads\MediathekView\Neues aus Büttenwarder\Rififi op Platt.mp4"
                ],
                MainVideoDisplayText = "Deutsch - FHD - H.264 aus der frischen Mediathek-Quelle",
                VideoAndAudioDescriptionDisplayText = "Plattdüütsch - HD - H.264, Audiodeskription: keine",
                SubtitlePaths =
                [
                    @"C:\Demo\Downloads\MediathekView\Neues aus Büttenwarder\Rififi.de.srt",
                    @"C:\Demo\Downloads\MediathekView\Neues aus Büttenwarder\Rififi.nds.ass"
                ],
                AttachmentPaths =
                [
                    @"C:\Demo\Downloads\MediathekView\Neues aus Büttenwarder\Rififi.txt"
                ],
                OutputPath = @"Z:\Videos\Serien\Neues aus Büttenwarder\Season 2014\Neues aus Büttenwarder - S2014E05-E06 - Rififi.mkv",
                LanguageOverrideOptions = languageOptions,
                VideoLanguageOverride = string.Empty,
                AudioLanguageOverride = string.Empty,
                OriginalLanguageOverride = "de",
                Notes =
                [
                    "Doppelfolge erkannt: Archivvergleich gegen die kombinierte Bibliotheksdatei.",
                    "TXT-Anhang bleibt erhalten, weil die Zuordnung zur frischen Hauptquelle eindeutig ist."
                ],
                NotesDisplayText = "Doppelfolge erkannt; TXT bleibt an der verwendeten Videospur.",
                HasNotes = true,
                HasActionablePlanNotes = true,
                PrimaryActionablePlanNote = "Vor dem Muxen nochmals prüfen, ob die Doppelfolge wirklich gegen die kombinierte Archivdatei abgeglichen werden soll.",
                UsageSummary = new DemoEpisodeUsageSummary
                {
                    ArchiveAction = "Arbeitskopie",
                    ArchiveDetails = "Vorhandene Doppelfolge wird als Basis wiederverwendet.",
                    MainVideo = new DemoUsageGroup("Deutsch - FHD - H.264 aus frischer Quelle", true, "Deutsch - HD - H.264 aus Bibliothek", "wird durch bessere Auflösung ersetzt"),
                    AdditionalVideos = new DemoUsageGroup("Plattdüütsch - HD - H.264 bleibt erhalten"),
                    Audio = new DemoUsageGroup("Deutsch - AAC und Plattdüütsch - AAC"),
                    AudioDescription = new DemoUsageGroup("Keine AD-Quelle vorhanden"),
                    Subtitles = new DemoUsageGroup("Deutsch (hörgeschädigte) - SRT, Plattdüütsch - ASS"),
                    Attachments = new DemoUsageGroup("Rififi.txt wird mitgeführt")
                }
            },
            new()
            {
                IsSelected = true,
                Title = "Bildungsschock",
                EpisodeCodeDisplayText = "S2013E09",
                ArchiveStateText = "vorhanden",
                ArchiveStateTooltip = "Archivdatei gefunden.",
                ArchiveBadgeBackground = Brush("#FFE9F8EE"),
                ArchiveBadgeBorderBrush = Brush("#FF63A46C"),
                ReviewHint = "TVDB ok",
                ReviewHintTooltip = "Metadaten wurden bestätigt.",
                ReviewBadgeBackground = Brush("#FFE9F8EE"),
                ReviewBadgeBorderBrush = Brush("#FF63A46C"),
                Status = "Warnung",
                StatusTooltip = "Eine Plattdeutsch-Quelle ersetzt keine deutsche Hauptspur.",
                StatusBadgeBackground = Brush("#FFFFF4D6"),
                StatusBadgeBorderBrush = Brush("#FFD6A62A"),
                MainVideoFileName = "Neues aus Büttenwarder - Bildungsschock.mp4"
            },
            new()
            {
                IsSelected = false,
                Title = "Am Filmset mit den Krimi-Helden",
                EpisodeCodeDisplayText = "S00E14",
                ArchiveStateText = "fehlt",
                ArchiveStateTooltip = "Noch keine passende Bibliotheksdatei vorhanden.",
                ArchiveBadgeBackground = Brush("#FFFDEBEC"),
                ArchiveBadgeBorderBrush = Brush("#FFC94F57"),
                ReviewHint = "TVDB prüfen",
                ReviewHintTooltip = "Rubrik- oder Magazinfolge benötigt manuelle Prüfung.",
                ReviewBadgeBackground = Brush("#FFFFF4D6"),
                ReviewBadgeBorderBrush = Brush("#FFD6A62A"),
                Status = "Prüfung",
                StatusTooltip = "Noch nicht freigegeben.",
                StatusBadgeBackground = Brush("#FFFFF4D6"),
                StatusBadgeBorderBrush = Brush("#FFD6A62A"),
                MainVideoFileName = "SOKO Leipzig - Am Filmset mit den Krimi-Helden.mp4"
            }
        };

        return new DemoBatchViewModel
        {
            IsInteractive = true,
            SourceDirectory = @"C:\Demo\Downloads\MediathekView\Neues aus Büttenwarder",
            OutputDirectory = @"Z:\Videos\Serien",
            HasOutputDirectoryHint = true,
            OutputDirectoryHintText = "Die Serienbibliothek ist erreichbar. Ausgabepfade werden direkt in die passenden Serienordner geplant.",
            FilterModes = filterModes,
            SelectedFilterMode = filterModes[0],
            SortModes = sortModes,
            SelectedSortMode = sortModes[1],
            RefreshAllComparisonsCommand = NoOpCommand,
            RefreshAllComparisonsTooltip = "Erneut gegen die Bibliothek vergleichen.",
            EpisodeItemsView = items,
            SelectedEpisodeItem = items[0],
            ReviewSelectedMetadataTooltip = "TVDB-Zuordnung für den markierten Eintrag prüfen.",
            OpenSelectedSourcesTooltip = "Öffnet alle Hauptvideoquellen des markierten Eintrags.",
            RedetectSelectedEpisodeTooltip = "Erkennung und Archivvergleich für den markierten Eintrag neu starten.",
            BatchLogInfoText = "Das Batch-Protokoll zeigt Scan, Pflichtchecks und den aktuellen Mux-Lauf der Sitzung.",
            LogText = "[10:12:14] Scan gestartet\r\n[10:12:21] 3 Episoden erkannt\r\n[10:12:29] Rififi gegen Archiv-Doppelfolge abgeglichen",
            StatusText = "Bereit für den Batch-Lauf",
            ProgressValue = 0,
            CancelBatchOperationCommand = NoOpCommand,
            CancelBatchOperationTooltip = "Kein Batch läuft.",
            CancelBatchOperationText = "Abbrechen",
            CanCancelBatchOperation = false,
            ScanDirectoryCommand = NoOpCommand,
            ScanDirectoryTooltip = "Quellordner erneut scannen.",
            SelectAllEpisodesCommand = NoOpCommand,
            DeselectAllEpisodesCommand = NoOpCommand,
            ReviewPendingSourcesCommand = NoOpCommand,
            ReviewPendingSourcesTooltip = "Öffnet alle noch offenen Pflichtprüfungen.",
            RunBatchCommand = NoOpCommand,
            RunBatchTooltip = "Startet den geplanten Batch-Lauf."
        };
    }

    public static DemoDownloadSortViewModel CreateDownloadSortViewModel()
    {
        var options = new ObservableCollection<string>
        {
            "Der Alte",
            "Jenseits der Spree",
            "Marie Brand",
            "Neues aus Büttenwarder",
            "SOKO Leipzig"
        };

        var items = new ObservableCollection<DemoDownloadItem>
        {
            new()
            {
                IsSelected = true,
                DisplayName = "Jenseits der Spree - Im Land der toten Träume",
                TargetFolderName = "Jenseits der Spree",
                StatusText = "Bereit",
                StatusBadgeBackground = Brush("#FFE9F8EE"),
                StatusBadgeBorderBrush = Brush("#FF63A46C"),
                Note = "Serie erkannt, Zielordner passt."
            },
            new()
            {
                IsSelected = true,
                DisplayName = "Marie Brand - Das falsche Opfer",
                TargetFolderName = "Marie Brand",
                StatusText = "Ersetzen",
                StatusBadgeBackground = Brush("#FFE9F2FF"),
                StatusBadgeBorderBrush = Brush("#FF4C84D3"),
                Note = "Gleichnamige ältere Zieldatei wird ersetzt."
            },
            new()
            {
                IsSelected = false,
                DisplayName = "Der Alte - Wunschkind",
                TargetFolderName = "Der Alte",
                StatusText = "Defekt",
                StatusBadgeBackground = Brush("#FFFFF4D6"),
                StatusBadgeBorderBrush = Brush("#FFD6A62A"),
                Note = "MP4 wirkt unvollständig; Video geht nach 'defekt', Untertitel bleiben für den späteren Mux erhalten."
            }
        };

        return new DemoDownloadSortViewModel
        {
            IsInteractive = true,
            SourceDirectory = @"C:\Demo\Downloads\MediathekView",
            SummaryText = "3 lose Download-Pakete erkannt. 2 können direkt einsortiert werden, 1 ist als defekt markiert.",
            Items = items,
            SelectedItem = items[1],
            TargetFolderOptions = options,
            SelectSourceDirectoryCommand = NoOpCommand,
            ScanCommand = NoOpCommand,
            ScanTooltip = "Downloadordner erneut scannen.",
            SelectAllSortableCommand = NoOpCommand,
            DeselectAllCommand = NoOpCommand,
            ApplyTargetFolderToMatchingItemsCommand = NoOpCommand,
            RunSortCommand = NoOpCommand,
            RunSortTooltip = "Verschiebt die ausgewählten Einträge in ihre Zielordner.",
            LogText = "[09:44:02] Downloadordner gescannt\r\n[09:44:05] 1 defekte MP4 erkannt\r\n[09:44:06] Serienordner für 2 Einträge bestätigt",
            StatusText = "Einsortieren bereit",
            ProgressValue = 0
        };
    }

    public static DemoEmbySyncViewModel CreateEmbySyncViewModel()
    {
        var items = new ObservableCollection<DemoEmbyItem>
        {
            new()
            {
                IsSelected = true,
                MediaFileName = "Neues aus Büttenwarder - S2014E05-E06 - Rififi.mkv",
                TvdbId = "1043421",
                ImdbId = "tt4234567",
                CanEditProviderIds = true,
                ProviderIdEditTooltip = "Provider-IDs können direkt geändert oder über die Zeilenaktionen geprüft werden.",
                CanReviewTvdb = true,
                TvdbLookupTooltip = "TVDB-Auswahl für diese Zeile öffnen.",
                CanReviewImdb = true,
                ImdbLookupTooltip = "IMDb-Suchhilfe für diese Zeile öffnen.",
                StatusTone = "Done",
                StatusText = "Aktuell",
                StatusTooltip = "NFO und Emby stimmen bereits überein.",
                Note = "Provider-IDs passen bereits."
            },
            new()
            {
                IsSelected = true,
                MediaFileName = "SOKO Leipzig - S00E14 - Am Filmset mit den Krimi-Helden.mkv",
                TvdbId = "981223",
                ImdbId = string.Empty,
                CanEditProviderIds = true,
                ProviderIdEditTooltip = "IMDb fehlt noch. Die ID kann direkt ergänzt oder über die Suchhilfe nachgetragen werden.",
                CanReviewTvdb = true,
                TvdbLookupTooltip = "TVDB-Auswahl für diese Zeile öffnen.",
                CanReviewImdb = true,
                ImdbLookupTooltip = "IMDb-Suchhilfe für diese Zeile öffnen.",
                StatusTone = "Warning",
                StatusText = "Prüfen",
                StatusTooltip = "Mindestens eine Provider-ID fehlt.",
                Note = "IMDb-ID fehlt noch."
            },
            new()
            {
                IsSelected = true,
                MediaFileName = "Neues aus Büttenwarder - Der Vorspann der Kultserie.mkv",
                TvdbId = string.Empty,
                ImdbId = string.Empty,
                CanEditProviderIds = false,
                ProviderIdEditTooltip = "Für Emby-Assets wie Trailer oder Backdrops ist kein Episoden-NFO-Sync nötig.",
                CanReviewTvdb = false,
                TvdbLookupTooltip = "Für diesen Asset-Typ nicht nötig.",
                CanReviewImdb = false,
                ImdbLookupTooltip = "Für diesen Asset-Typ nicht nötig.",
                StatusTone = "Neutral",
                StatusText = "Nicht nötig",
                StatusTooltip = "Emby-Asset ohne Episoden-NFO.",
                Note = "Emby-Asset ohne Episoden-NFO."
            }
        };

        return new DemoEmbySyncViewModel
        {
            IsInteractive = true,
            ReportPath = string.Join(
                Environment.NewLine,
                @".\Logs\Neu erzeugte Ausgabedateien - 2026-04-21 10-12.metadata.json",
                @".\Logs\Neu erzeugte Ausgabedateien - 2026-04-21 10-26.metadata.json"),
            ReportSelectionSummaryText = "2 Reports ausgewählt",
            ReportSelectionDetailText = "3 neue MKV-Dateien aus den letzten Batch- und Einzel-Läufen.",
            ReportSelectionTooltip = "Die Reports werden beim Auswählen direkt geladen und geprüft.",
            Items = items,
            SelectedItem = items[1],
            RunScanCommand = NoOpCommand,
            RunScanTooltip = "Liest Emby nach einem Bibliotheksscan erneut ein.",
            RunSyncCommand = NoOpCommand,
            RunSyncTooltip = "Schreibt geänderte IDs in die NFO zurück.",
            ReviewPendingProviderIdsCommand = NoOpCommand,
            ReviewPendingProviderIdsTooltip = "Abarbeiten aller offenen TVDB-/IMDb-Pflichtprüfungen.",
            SelectReportCommand = NoOpCommand,
            SelectAllCommand = NoOpCommand,
            DeselectAllCommand = NoOpCommand,
            LogText = "[11:02:09] 2 Reports geladen\r\n[11:02:10] Emby-Serienbibliothek erkannt: Serien\r\n[11:02:16] 3 Einträge geprüft",
            SummaryText = "3 Datei(en), 3 ausgewählt, 1 ohne vollständige TVDB-/IMDB-ID.",
            StatusText = "Prüfung abgeschlossen",
            ProgressValue = 0
        };
    }

    public static DemoArchiveMaintenanceViewModel CreateArchiveMaintenanceViewModel()
    {
        var items = new ObservableCollection<DemoArchiveMaintenanceItem>
        {
            new()
            {
                IsSelected = true,
                FileName = "Neues aus Büttenwarder - S2014E05 - Rififi (1).mkv",
                DirectoryPath = @"Z:\Videos\Serien\Neues aus Büttenwarder\Season 2014",
                StatusText = "Ändern",
                StatusTone = "Ready",
                ChangeSummary = "MKV-Titel: Rififi alt -> Rififi (1); Deutsch - HD - H.264: Standard: nein -> ja",
                WritableChangeNotes =
                [
                    "MKV-Titel: Rififi alt -> Rififi (1)",
                    "Deutsch - HD - H.264: Standard: nein -> ja",
                    "Deutsch - AAC: Originalsprache: nein -> ja"
                ],
                TargetFileName = "Neues aus Büttenwarder - S2014E05 - Rififi (1).mkv",
                TargetContainerTitle = "Rififi (1)",
                HeaderCorrections =
                [
                    new DemoArchiveMaintenanceHeaderCorrection
                    {
                        DisplayLabel = "Deutsch - HD - H.264",
                        DisplayName = "Standard",
                        CurrentDisplayValue = "nein",
                        TargetValue = "ja",
                        IsFlag = true
                    },
                    new DemoArchiveMaintenanceHeaderCorrection
                    {
                        DisplayLabel = "Deutsch - AAC",
                        DisplayName = "Originalsprache",
                        CurrentDisplayValue = "nein",
                        TargetValue = "ja",
                        IsFlag = true
                    },
                    new DemoArchiveMaintenanceHeaderCorrection
                    {
                        DisplayLabel = "Alter Videoname",
                        DisplayName = "Name",
                        CurrentDisplayValue = "Alter Videoname",
                        TargetValue = "Deutsch - HD - H.264"
                    }
                ]
            },
            new()
            {
                IsSelected = false,
                FileName = "Die Heiland - S00E15 - Auf der Tastatur schreiben.mkv",
                DirectoryPath = @"Z:\Videos\Serien\Die Heiland\Specials",
                StatusText = "Remux nötig",
                StatusTone = "Warning",
                ChangeSummary = "Doppelte AD-Spuren für Deutsch: 2, 3.",
                IssueMessages =
                [
                    "Doppelte AD-Spuren für Deutsch: 2, 3."
                ],
                TargetFileName = "Die Heiland - S00E15 - Auf der Tastatur schreiben.mkv",
                TargetContainerTitle = "Auf der Tastatur schreiben"
            },
            new()
            {
                IsSelected = false,
                FileName = "Pettersson und Findus - S00E04 - Kleiner Quälgeist, große Freundschaft.mkv",
                DirectoryPath = @"Z:\Videos\Serien\Pettersson und Findus\Specials",
                StatusText = "OK",
                StatusTone = "Done",
                ChangeSummary = "Keine Änderung nötig.",
                TargetFileName = "Pettersson und Findus - S00E04 - Kleiner Quälgeist, große Freundschaft.mkv",
                TargetContainerTitle = "Kleiner Quälgeist, große Freundschaft"
            }
        };

        return new DemoArchiveMaintenanceViewModel
        {
            IsInteractive = true,
            RootDirectory = @"Z:\Videos\Serien",
            Items = items,
            SelectedItem = items[0],
            SelectedDetailText = string.Join(
                Environment.NewLine,
                @"Z:\Videos\Serien\Neues aus Büttenwarder\Season 2014\Neues aus Büttenwarder - S2014E05 - Rififi (1).mkv",
                "",
                "Schreibbare Änderungen:",
                "- MKV-Titel: Rififi alt -> Rififi (1)",
                "- Deutsch - HD - H.264: Standard: nein -> ja",
                "- Deutsch - AAC: Originalsprache: nein -> ja"),
            SummaryText = "3 Datei(en), 1 mit direkt schreibbaren Änderungen, 1 mit Remux-Hinweis, 1 ausgewählt.",
            LogText = "[12:31:02] Scan gestartet\r\n[12:31:16] 3 Archivdateien geprüft\r\n[12:31:16] 1 direkte Headerkorrektur gefunden",
            StatusText = "Scan abgeschlossen: 3 Datei(en).",
            ProgressValue = 0,
            SelectRootDirectoryCommand = NoOpCommand,
            ScanCommand = NoOpCommand,
            SelectAllWritableCommand = NoOpCommand,
            DeselectAllCommand = NoOpCommand,
            OpenSelectedFileCommand = NoOpCommand,
            ApplySelectedCommand = NoOpCommand
        };
    }

    public static IReadOnlyList<DemoChoice> GetModuleCards()
    {
        return
        [
            new DemoChoice("Download", "MediathekView starten und Sendungen laden."),
            new DemoChoice("Einsortieren", "Lose Mediathek-Downloads in Serienordner verschieben."),
            new DemoChoice("Muxen", "Erkennen, Archiv vergleichen und MKV-Dateien bauen."),
            new DemoChoice("Emby-Abgleich", "NFO-Provider-IDs gegen Emby und Reports abgleichen."),
            new DemoChoice("Archivpflege", "Vorhandene MKV-Dateien prüfen und Header korrigieren.")
        ];
    }
}
