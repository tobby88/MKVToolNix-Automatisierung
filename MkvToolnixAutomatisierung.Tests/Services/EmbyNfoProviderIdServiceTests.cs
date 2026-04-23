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
            Assert.Contains("""<uniqueid type="tvdb" default="true">12345</uniqueid>""", updatedText);
            Assert.Contains("""<uniqueid type="imdb">tt9876543</uniqueid>""", updatedText);
            Assert.Contains("<tvdbid>12345</tvdbid>", updatedText);
            Assert.Contains("<imdbid>tt9876543</imdbid>", updatedText);
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
            Assert.Null(uniqueIds.Single(element => (string?)element.Attribute("type") == "imdb").Attribute("default"));
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

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
