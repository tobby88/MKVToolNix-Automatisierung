using System.IO;
using MkvToolnixAutomatisierung.Services.Emby;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class EmbyNfoProviderIdServiceTests
{
    [Fact]
    public void ReadProviderIds_ReadsUniqueIdAndLegacyFallbacks()
    {
        var directory = CreateTempDirectory();
        try
        {
            var mediaPath = Path.Combine(directory, "Episode.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(mediaPath, string.Empty);
            File.WriteAllText(
                nfoPath,
                """
                <episodedetails>
                  <uniqueid type="tvdb" default="true">12345</uniqueid>
                  <imdbid>tt9876543</imdbid>
                </episodedetails>
                """);

            var result = new EmbyNfoProviderIdService().ReadProviderIds(mediaPath);

            Assert.True(result.NfoExists);
            Assert.Equal("12345", result.ProviderIds.TvdbId);
            Assert.Equal("tt9876543", result.ProviderIds.ImdbId);
            Assert.Null(result.WarningMessage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReadProviderIds_PrefersDefaultUniqueId_WhenDuplicateProviderIdsExist()
    {
        var directory = CreateTempDirectory();
        try
        {
            var mediaPath = Path.Combine(directory, "Episode.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(mediaPath, string.Empty);
            File.WriteAllText(
                nfoPath,
                """
                <episodedetails>
                  <uniqueid type="tvdb">11111</uniqueid>
                  <uniqueid type="tvdb" default="true">22222</uniqueid>
                </episodedetails>
                """);

            var result = new EmbyNfoProviderIdService().ReadProviderIds(mediaPath);

            Assert.Equal("22222", result.ProviderIds.TvdbId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReadEpisodeMetadata_ReadsTitleAndSortTitle()
    {
        var directory = CreateTempDirectory();
        try
        {
            var mediaPath = Path.Combine(directory, "Episode.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(mediaPath, string.Empty);
            File.WriteAllText(
                nfoPath,
                """
                <episodedetails>
                  <title>Episode Titel</title>
                  <sorttitle>Sortierter Titel</sorttitle>
                  <lockedfields>Name|SortName</lockedfields>
                  <uniqueid type="tvdb" default="true">12345</uniqueid>
                </episodedetails>
                """);

            var result = new EmbyNfoProviderIdService().ReadEpisodeMetadata(mediaPath);

            Assert.True(result.NfoExists);
            Assert.Equal("Episode Titel", result.Title);
            Assert.Equal("Sortierter Titel", result.SortTitle);
            Assert.True(result.IsTitleLocked);
            Assert.True(result.IsSortTitleLocked);
            Assert.Equal("12345", result.ProviderIds.TvdbId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UpdateProviderIds_UpdatesUniqueIdsAndLegacyElements()
    {
        var directory = CreateTempDirectory();
        try
        {
            var mediaPath = Path.Combine(directory, "Episode.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(mediaPath, string.Empty);
            File.WriteAllText(
                nfoPath,
                """
                <episodedetails>
                  <title>Episode</title>
                  <uniqueid type="tvdb">old</uniqueid>
                </episodedetails>
                """);

            var result = new EmbyNfoProviderIdService().UpdateProviderIds(
                mediaPath,
                new EmbyProviderIds("12345", "tt9876543"));

            Assert.True(result.Success);
            Assert.True(result.NfoChanged);

            var updatedText = File.ReadAllText(nfoPath);
            Assert.Contains("""<uniqueid type="tvdb">12345</uniqueid>""", updatedText);
            Assert.Contains("""<uniqueid type="imdb">tt9876543</uniqueid>""", updatedText);
            Assert.Contains("<tvdbid>12345</tvdbid>", updatedText);
            Assert.Contains("<imdbid>tt9876543</imdbid>", updatedText);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UpdateProviderIds_DoesNotRewriteNfo_WhenUniqueIdsAlreadyMatch()
    {
        var directory = CreateTempDirectory();
        try
        {
            var mediaPath = Path.Combine(directory, "Episode.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(mediaPath, string.Empty);
            const string originalNfo = """
                <episodedetails>
                  <title>Episode</title>
                  <uniqueid type="tvdb">12345</uniqueid>
                  <uniqueid type="imdb" default="true">tt9876543</uniqueid>
                </episodedetails>
                """;
            File.WriteAllText(nfoPath, originalNfo);
            var lastWriteTimeUtc = File.GetLastWriteTimeUtc(nfoPath);

            var result = new EmbyNfoProviderIdService().UpdateProviderIds(
                mediaPath,
                new EmbyProviderIds("12345", "tt9876543"));

            Assert.True(result.Success);
            Assert.False(result.NfoChanged);
            Assert.Equal("NFO-Provider-IDs waren bereits aktuell.", result.Message);
            Assert.Equal(originalNfo, File.ReadAllText(nfoPath));
            Assert.Equal(lastWriteTimeUtc, File.GetLastWriteTimeUtc(nfoPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UpdateProviderIds_DoesNotCreateMissingNfo()
    {
        var directory = CreateTempDirectory();
        try
        {
            var mediaPath = Path.Combine(directory, "Episode.mkv");
            File.WriteAllText(mediaPath, string.Empty);

            var result = new EmbyNfoProviderIdService().UpdateProviderIds(
                mediaPath,
                new EmbyProviderIds("12345", null));

            Assert.False(result.Success);
            Assert.False(result.NfoChanged);
            Assert.False(File.Exists(Path.ChangeExtension(mediaPath, ".nfo")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UpdateProviderIds_RemovesDuplicateProviderElements_AndKeepsSingleCanonicalEntry()
    {
        var directory = CreateTempDirectory();
        try
        {
            var mediaPath = Path.Combine(directory, "Episode.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(mediaPath, string.Empty);
            File.WriteAllText(
                nfoPath,
                """
                <episodedetails>
                  <uniqueid type="tvdb" default="true">111</uniqueid>
                  <uniqueid type="tvdb">222</uniqueid>
                  <uniqueid type="imdb" default="true">tt0000001</uniqueid>
                  <tvdbid>111</tvdbid>
                  <tvdbid>222</tvdbid>
                  <imdbid>tt0000001</imdbid>
                  <imdbid>tt0000002</imdbid>
                </episodedetails>
                """);

            var result = new EmbyNfoProviderIdService().UpdateProviderIds(
                mediaPath,
                new EmbyProviderIds("12345", "tt9876543"));

            Assert.True(result.Success);
            Assert.True(result.NfoChanged);

            var updatedDocument = System.Xml.Linq.XDocument.Load(nfoPath);
            var uniqueIds = updatedDocument.Root!.Elements("uniqueid").ToList();
            Assert.Single(uniqueIds, element => (string?)element.Attribute("type") == "tvdb");
            Assert.Single(uniqueIds, element => (string?)element.Attribute("type") == "imdb");
            Assert.Equal("true", uniqueIds.Single(element => (string?)element.Attribute("type") == "tvdb").Attribute("default")?.Value);
            Assert.Equal("true", uniqueIds.Single(element => (string?)element.Attribute("type") == "imdb").Attribute("default")?.Value);
            Assert.Single(updatedDocument.Root.Elements("tvdbid"));
            Assert.Single(updatedDocument.Root.Elements("imdbid"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UpdateProviderIds_RemoveImdbId_RemovesUniqueAndLegacyImdbEntries()
    {
        var directory = CreateTempDirectory();
        try
        {
            var mediaPath = Path.Combine(directory, "Episode.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(mediaPath, string.Empty);
            File.WriteAllText(
                nfoPath,
                """
                <episodedetails>
                  <title>Episode</title>
                  <uniqueid type="tvdb" default="true">12345</uniqueid>
                  <uniqueid type="imdb">tt0000001</uniqueid>
                  <imdbid>tt0000001</imdbid>
                </episodedetails>
                """);

            var result = new EmbyNfoProviderIdService().UpdateProviderIds(
                mediaPath,
                new EmbyProviderIds("12345", null),
                removeImdbId: true);

            Assert.True(result.Success);
            Assert.True(result.NfoChanged);

            var updatedDocument = System.Xml.Linq.XDocument.Load(nfoPath);
            Assert.DoesNotContain(updatedDocument.Root!.Elements("uniqueid"), element => (string?)element.Attribute("type") == "imdb");
            Assert.Null(updatedDocument.Root.Element("imdbid"));
            Assert.Equal("12345", updatedDocument.Root.Elements("uniqueid").Single(element => (string?)element.Attribute("type") == "tvdb").Value);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UpdateTextFields_UpdatesTitleSortTitleAndLocksChangedFields()
    {
        var directory = CreateTempDirectory();
        try
        {
            var mediaPath = Path.Combine(directory, "Episode.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(mediaPath, string.Empty);
            File.WriteAllText(
                nfoPath,
                """
                <episodedetails>
                  <title>Alt</title>
                  <sorttitle>Alt Sort</sorttitle>
                  <lockdata>false</lockdata>
                  <dateadded>2026-04-28</dateadded>
                </episodedetails>
                """);

            var result = new EmbyNfoProviderIdService().UpdateTextFields(
                mediaPath,
                new EmbyNfoTextFields("Neu", "Neu Sort"));

            Assert.True(result.Success);
            Assert.True(result.NfoChanged);
            var updatedDocument = System.Xml.Linq.XDocument.Load(nfoPath);
            var root = updatedDocument.Root!;
            Assert.Equal("Neu", root.Element("title")?.Value);
            Assert.Equal("Neu Sort", root.Element("sorttitle")?.Value);
            Assert.Equal("Name|SortName", root.Element("lockedfields")?.Value);
            Assert.True(
                root.Elements().ToList().IndexOf(root.Element("lockdata")!)
                < root.Elements().ToList().IndexOf(root.Element("lockedfields")!));
            Assert.True(
                root.Elements().ToList().IndexOf(root.Element("lockedfields")!)
                < root.Elements().ToList().IndexOf(root.Element("dateadded")!));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UpdateTextFields_PreservesExistingLockedFields()
    {
        var directory = CreateTempDirectory();
        try
        {
            var mediaPath = Path.Combine(directory, "Episode.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(mediaPath, string.Empty);
            File.WriteAllText(
                nfoPath,
                """
                <episodedetails>
                  <title>Alt</title>
                  <sorttitle>Alt Sort</sorttitle>
                  <lockedfields>Genres</lockedfields>
                </episodedetails>
                """);

            var result = new EmbyNfoProviderIdService().UpdateTextFields(
                mediaPath,
                new EmbyNfoTextFields("Neu", "Alt Sort"));

            Assert.True(result.Success);
            var updatedDocument = System.Xml.Linq.XDocument.Load(nfoPath);
            Assert.Equal("Genres|Name", updatedDocument.Root!.Element("lockedfields")?.Value);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UpdateTextFields_TogglesLockedFieldsWithoutTextChanges()
    {
        var directory = CreateTempDirectory();
        try
        {
            var mediaPath = Path.Combine(directory, "Episode.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(mediaPath, string.Empty);
            File.WriteAllText(
                nfoPath,
                """
                <episodedetails>
                  <title>Alt</title>
                  <sorttitle>Alt Sort</sorttitle>
                  <lockedfields>Name|SortName|Genres</lockedfields>
                </episodedetails>
                """);

            var result = new EmbyNfoProviderIdService().UpdateTextFields(
                mediaPath,
                new EmbyNfoTextFields(
                    "Alt",
                    "Alt Sort",
                    LockTitle: false,
                    LockSortTitle: true));

            Assert.True(result.Success);
            Assert.True(result.NfoChanged);
            var updatedDocument = System.Xml.Linq.XDocument.Load(nfoPath);
            Assert.Equal("SortName|Genres", updatedDocument.Root!.Element("lockedfields")?.Value);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
