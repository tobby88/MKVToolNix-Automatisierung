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

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mkv-auto-emby-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
