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
5. `Kein Eintrag` bestätigt je Anbieter ausdrücklich, dass keine TVDB- beziehungsweise IMDb-ID vergeben werden soll. Die IMDb-Absage bestätigt nicht zugleich eine fehlende TVDB-Zuordnung; leere Felder allein bleiben offen.
6. `NFO speichern + Emby aktualisieren` schreibt ausschließlich tatsächlich geänderte Provider-IDs. Unveränderte NFOs werden nicht neu gespeichert und ihre Emby-Items nicht unnötig aktualisiert.

Der lokale Archivpfad kann in den Einstellungen ausdrücklich auf einen Emby-Serverpfad
abgebildet werden; optional legt eine Bibliotheks-ID die Serienbibliothek fest. Ohne
eindeutige Zuordnung wird kein Scan gestartet. Ein globaler Fallback ist ausgeschlossen.
Ohne explizite Zuordnung verlangt die automatische Suffixerkennung mindestens zwei
gemeinsame Pfadsegmente, nicht nur den generischen Ordnernamen `Serien`.

Abbruch gilt für Import, Prüfung, Scan-Warten und Schreiben. Bereits geschriebene NFOs
und bestätigte Providerentscheidungen bleiben erhalten. Ein danach abgebrochener Refresh
bleibt offen; der Server-Scan selbst wird durch das Abbrechen des Wartens nicht beendet.

Der abschließende Schreibschritt speichert die aktuelle Provider-Auswahl und manuelle Freigaben im optionalen `embyReview`-Block jedes Reporteintrags. Die ursprünglichen Mux-Metadaten bleiben unverändert. `embySyncDone` und die Abschlusszeitpunkte dokumentieren die erfolgreiche Bearbeitung. Unvollständige Reports werden nach `partial`, vollständig erledigte nach `done` verschoben; Wiederimporte verwenden diese Geschwisterordner ohne weitere Verschachtelung. Erneute fehlgeschlagene Bearbeitungen nehmen einen alten Abschluss zurück. Ohne konfigurierte Emby-Zugangsdaten gilt der lokale NFO-Abgleich als Abschluss; mit Zugangsdaten bleibt ein erforderlicher, aber nicht erfolgreicher Refresh offen.

Beim Wiederimport werden gespeicherte manuelle Entscheidungen übernommen. Automatische Übereinstimmungen werden dagegen erneut anhand der aktuellen Quellen geprüft. Bewusst fehlende IDs werden nicht aus Report, NFO oder Emby wieder aufgefüllt. Das Entfernen des Hakens oder eine neue ID-Eingabe erlaubt eine erneute Zuordnung.

## Lokaler IMDb-Index

Der optionale Index wird aus `title.basics.tsv.gz`, `title.episode.tsv.gz` und `title.akas.tsv.gz` aufgebaut. Die App lädt die großen Rohdaten nur nach ausdrücklicher Zustimmung und ersetzt den aktiven Index erst nach einem vollständig erfolgreichen Lauf. Beim Import hält sie die numerischen Episodenzuordnungen kurzzeitig in einer kompakten Wertstruktur, sodass jede Serie und Episode nur einmal vollständig in die temporäre SQLite-Datenbank geschrieben werden muss. Deutsche Aliase werden vor SQLite über einen platzsparenden Titel-ID-Filter begrenzt; der abschließende Indexbau darf einen größeren temporären Seitencache und mehrere SQLite-Hilfsthreads verwenden.

Der sichtbare Importfortschritt kombiniert den exakten Zeilenzähler mit dem Anteil bereits gelesener komprimierter Bytes. Dadurch können sowohl Datei- als auch Gesamtprozent über alle drei Archive angezeigt werden, ohne die großen TSV-Dateien vor dem eigentlichen Import ein zweites Mal vollständig zu dekomprimieren.

Titelähnlichkeit ist das wichtigste Suchsignal. Staffel und Folge beeinflussen die Rangfolge, sind aber kein Ausschlusskriterium, weil IMDb und TVDB größere Serien häufig unterschiedlich nummerieren. Nur ein eindeutiger exakter Serien- und Episodentitel darf ohne Benutzerentscheidung übernommen werden.

Der manuelle Dialog lädt lokale Kandidaten asynchron und hält bereits gelesene Serien- und Episodenkataloge für weitere Folgen derselben Serie im Speicher. Korrigierte Serien- oder Episodentexte lösen nach einer kurzen Entprellzeit automatisch eine neue Hintergrundsuche aus. Passende Serien werden einschließlich lokalisierter Aliasnamen angeboten; nach der Serienwahl lässt sich der Episodenkatalog auf die tatsächlich vorhandenen IMDb-Staffeln begrenzen.

Ein einzelner Tippfehler am Wortanfang wird durch zusätzliche indexgestützte Präfixbereiche
berücksichtigt. Ergebnislimits gelten konsistent auch bei warmem Cache; ein internes
256-Zeilen-/12-Serien-Limit schneidet passende Kandidaten nicht mehr ab.

Vor Aktivierung prüft der Update-Worker SQLite-Integrität und Importmengen. Ein Rückgang
um mehr als 20 Prozent gegenüber mindestens 100 vorhandenen Serien, Episoden oder Aliasnamen
verhindert den Austausch und verlangt Prüfung. Ein zuvor fehlgeschlagener Settings-Save
wird beim nächsten Start mit Version/Schema/Aufbauzeit aus dem aktiven Index abgeglichen.
Diese Plausibilitätsgrenze ist keine Vollständigkeits- oder kryptographische Herkunftsgarantie.

Der Index ist ein Fallback, keine zusätzliche Online-API. Er liegt portabel unter `Data/IMDb/imdb-episodes.sqlite`; die heruntergeladenen GZip-Dateien werden nach dem Aufbau wieder entfernt. Stand September 2026 beanspruchen die Archive rund 750 MiB und der fertige Index rund 1,3 GiB. Während des atomaren Neuaufbaus sollten 4 bis 5 GiB frei sein, weil alter und neuer Index vorübergehend neben den Archiven liegen. Die IMDb-Datensätze dürfen nur entsprechend ihrer Bedingungen für persönliche, nichtkommerzielle Zwecke verwendet werden.

## NFO-Schreibgrenzen

Emby bleibt für die erste Erzeugung einer Episoden-NFO zuständig. Die Anwendung ergänzt oder korrigiert danach nur die von ihr geprüften Provider-IDs und verändert keine Beschreibungen, Personenlisten oder Bilder.

Asset-Ordner wie `trailers` und `backdrops` erhalten normalerweise keine Episoden-NFO. Solche Einträge werden als nicht erforderlich abgeschlossen, statt dauerhaft als fehlende ID gemeldet zu werden.
