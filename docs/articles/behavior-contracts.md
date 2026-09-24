# Bewusste Regeln und Grenzen

Diese Regeln gelten gemeinsam für Einzel-/Batch-Mux und, soweit anwendbar, Archivpflege.
Sie sind keine Zusage, dass zwei heuristisch zugeordnete Dateien dieselbe Schnittfassung enthalten.

## Medienerkennung

- Unbekannte oder fehlende Mux-Sprachangaben fallen in dieser deutschzentrierten Sammlung
  auf Deutsch zurück. Beim Vergleich einer externen Originalsprache bleibt ein unbekannter
  Code dagegen unbekannt und bestätigt nicht versehentlich das Originalsprache-Flag.
- Externe Untertitel ohne genauere Einordnung verwenden den bisherigen Hörgeschädigt-Fallback.
  Explizit erkannte normale/Forced-Untertitel und manuelle Korrekturen gehen vor.
- Der Codec-Tiebreak bevorzugt H.264 vor H.265. Das ist keine allgemeine Qualitätsaussage;
  Auflösung, Framerate und weitere Planerregeln werden separat berücksichtigt.
- AD-only-Quellen dürfen mit einer passenden Archiv-Hauptspur kombiniert werden. Ohne
  passende Hauptquelle gibt es keinen erfundenen normalen Ton. Ein bereits AD-only vorliegendes
  Archiv-Special bleibt ein expliziter Sonderfall.
- Namen, Folgenummern, Sender und Laufzeiten liefern Heuristiken. Ähnliche Laufzeiten beweisen
  weder Synchronität noch identische Schnitte. Quellenprüfung und manuelle Freigabe bleiben nötig.
- Eine einzelne TVDB-ID überschreibt bei Mehrfachfolgen nicht automatisch den Titel und die
  Staffel der gesamten Datei. Dateititel und Container-Titel bleiben dabei getrennt erhalten.

## NFO und Dateischreiben

Unterstützt wird eine einzelne namespacefreie `episodedetails`-Wurzel ohne DTD. Nicht
unterstützte Dokumente werden gemeldet statt durch eine neu erzeugte NFO ersetzt.
Unveränderte Inhalte bleiben bytegenau bestehen. Bei echten Änderungen werden fremde
XML-Werte, Attribute und Kommentare semantisch erhalten; BOM, Encoding und Formatierung
sind keine Erhaltungsgarantie. Gleichzeitige externe Änderungen führen zu einem Konflikt,
nicht zum stillen Überschreiben. Ein kompletter Archivauftrag mit in-place-Headeränderung
ist trotzdem keine atomare Transaktion: nach einem Fehler neu scannen und Reständerungen prüfen.

## Providerdialoge

IMDb-Browserrückkehr erlaubt einmalig die Übernahme eines neuen gültigen Zwischenablagewerts
nach einem ausdrücklich gestarteten Browseraufruf. Alte Inhalte und inzwischen manuell
geänderte IDs werden nicht übernommen. Auch eine erfolglose Rückkehr verbraucht diese
Freigabe; der explizite Zwischenablage-Button bleibt verfügbar. Die App kann die Herkunft
eines Texts in der Windows-Zwischenablage nicht beweisen.

TVDB-Aufrufer teilen laufende Requests. Ein geschlossener Dialog wartet nicht weiter und
bekommt keine späten Ergebnisse, beendet aber nicht die Anfrage anderer Nutzer.
Cachegrenzen beziehen sich auf Einträge, nicht auf harte RAM-Grenzen. Vorbereitete
Ordnerkontexte sind Scan-Snapshots und keine Dateisystem-Watcher.

## Physische Pfade

Schreibende Vorgänge weisen erkannte Reparse-Points, Mehrfach-Hardlinks und unterstützte
case-sensitive Windows-Unterbäume konservativ ab. UNC-Aliase werden nicht automatisch
als identische zulässige Quellordner behandelt. Das ist kein Schutzversprechen gegen
einen lokalen Angreifer mit Pfadaustauschrechten oder einen nicht kooperierenden SMB-Server.
