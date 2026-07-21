# Metadaten- und Provider-Datenfluss

Die Anwendung trennt lokale Erkennung, TVDB, IMDb, NFO und Emby bewusst voneinander. Keine einzelne Quelle wird pauschal als immer richtig behandelt; eindeutige IDs dürfen automatisiert fließen, Widersprüche bleiben dagegen sichtbar und müssen bestätigt werden.

## Beim Muxen

1. Dateiname und MediathekView-TXT liefern zunächst Serie, Titel, Staffel und Folge.
2. TVDB kann diese Erkennung bestätigen oder korrigieren. Die gewählte TVDB-Episoden-ID wird zusammen mit der neuen MKV in den strukturierten Metadatenreport geschrieben.
3. Einzel- und Batch-Mux verwenden dafür dieselbe Metadaten- und Planungslogik. Der Emby-Schritt muss die Episode deshalb nicht erneut anhand des Dateinamens erraten.

## Im Emby-Abgleich

1. Ein oder mehrere Metadatenreports werden geladen; lokale NFOs und bereits bekannte Emby-Items werden danach automatisch geprüft.
2. Fehlt ein Emby-Item, kann ein auf die erkannte Serienbibliothek begrenzter Scan gestartet werden. Erst wenn Emby den serverseitigen Task wirklich beendet hat, wird erneut geprüft.
3. TVDB wird nur bei widersprüchlichen lokalen, NFO- oder Emby-Werten als Pflichtcheck geöffnet.
4. Für IMDb gilt die Reihenfolge:
   - Remote-ID der bereits bestätigten TVDB-Episode
   - optionaler lokaler Index der offiziellen IMDb-Datensätze
   - browsergestützte manuelle Suche
5. Eine bewusst bestätigte Entscheidung `Keine IMDb-ID` ist ebenfalls ein abgeschlossenes Prüfergebnis.
6. `NFO speichern + Emby aktualisieren` schreibt ausschließlich tatsächlich geänderte Provider-IDs. Unveränderte NFOs werden nicht neu gespeichert und ihre Emby-Items nicht unnötig aktualisiert.

## Lokaler IMDb-Index

Der optionale Index wird aus `title.basics.tsv.gz`, `title.episode.tsv.gz` und `title.akas.tsv.gz` aufgebaut. Die App lädt die großen Rohdaten nur nach ausdrücklicher Zustimmung, importiert sie streamend in eine temporäre SQLite-Datenbank und ersetzt den aktiven Index erst nach einem vollständig erfolgreichen Lauf.

Titelähnlichkeit ist das wichtigste Suchsignal. Staffel und Folge beeinflussen die Rangfolge, sind aber kein Ausschlusskriterium, weil IMDb und TVDB größere Serien häufig unterschiedlich nummerieren. Nur ein eindeutiger exakter Serien- und Episodentitel darf ohne Benutzerentscheidung übernommen werden.

Der Index ist ein Fallback, keine zusätzliche Online-API. Er liegt portabel unter `Data/IMDb/imdb-episodes.sqlite`; die heruntergeladenen GZip-Dateien werden nach dem Aufbau wieder entfernt. Die IMDb-Datensätze dürfen nur entsprechend ihrer Bedingungen für persönliche, nichtkommerzielle Zwecke verwendet werden.

## NFO-Schreibgrenzen

Emby bleibt für die erste Erzeugung einer Episoden-NFO zuständig. Die Anwendung ergänzt oder korrigiert danach nur die von ihr geprüften Provider-IDs und verändert keine Beschreibungen, Personenlisten oder Bilder.

Asset-Ordner wie `trailers` und `backdrops` erhalten normalerweise keine Episoden-NFO. Solche Einträge werden als nicht erforderlich abgeschlossen, statt dauerhaft als fehlende ID gemeldet zu werden.
