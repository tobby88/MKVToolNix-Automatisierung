# MKVToolNix-Automatisierung

Diese DocFX-Seite ergänzt die portable App um strukturierte Entwicklerdokumentation. Sie kombiniert konzeptionelle Artikel mit API-Dokumentation aus den XML-Kommentaren des C#-Codes.

## Einstieg

- [Architekturüberblick](articles/architecture.md)
- [Teststrategie](articles/testing.md)
- [Portable Daten und Logs](articles/portable-storage.md)

Die API-Referenz wird beim DocFX-Build automatisch erzeugt und ist in der fertigen Site über den Navigationspunkt `API` erreichbar.

## Lokal erzeugen

```powershell
dotnet tool restore
.\scripts\build-docs.ps1
```

Für eine lokale Vorschau mit eingebautem Webserver:

```powershell
.\scripts\build-docs.ps1 -Serve
```

## Umfang

Die DocFX-API-Referenz konzentriert sich bewusst auf die fachlichen Module und Services. WPF-Views, Window-Code-behind und reine Binding-ViewModels bleiben in der API-Doku ausgeblendet, weil sie für die externe Wartungsdokumentation wenig Mehrwert liefern und in den Quellkommentaren bereits ausführlicher kontextualisiert sind.
