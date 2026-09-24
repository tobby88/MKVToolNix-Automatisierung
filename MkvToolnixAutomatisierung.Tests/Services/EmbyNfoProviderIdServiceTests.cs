using System.IO;
using MkvToolnixAutomatisierung.Services.Emby;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class EmbyNfoProviderIdServiceTests
{
    [Fact]
    public void Utf16Nfo_NoOpPreservesBytes_RealEditPreservesXmlMeaning()
    {
        var directory = CreateTempDirectory();
        try
        {
            var media = Path.Combine(directory, "Episode.mkv");
            var nfo = Path.ChangeExtension(media, ".nfo");
            File.WriteAllText(nfo, "<?xml version=\"1.0\" encoding=\"utf-16\"?><episodedetails><!--retained--><title>Lippmann wird vermißt</title><uniqueid type=\"tvdb\">7</uniqueid><custom source=\"local\"> A &amp; B </custom></episodedetails>", System.Text.Encoding.Unicode);
            var original = File.ReadAllBytes(nfo);
            var service = new EmbyNfoProviderIdService();
            Assert.False(service.UpdateProviderIds(media, new("7", null)).NfoChanged);
            Assert.Equal(original, File.ReadAllBytes(nfo));
            Assert.True(service.UpdateProviderIds(media, new("8", null)).NfoChanged);
            var root = System.Xml.Linq.XDocument.Load(nfo).Root!;
            Assert.Equal("Lippmann wird vermißt", root.Element("title")!.Value);
            Assert.Equal(" A & B ", root.Element("custom")!.Value);
            Assert.Equal("local", root.Element("custom")!.Attribute("source")!.Value);
            Assert.Single(root.Nodes().OfType<System.Xml.Linq.XComment>());
            Assert.Equal("8", service.ReadProviderIds(media).ProviderIds.TvdbId);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void UpdateProviderIds_RemovesOnlyExplicitlyRejectedProviders_AndIsIdempotent(bool removeTvdb, bool removeImdb)
    {
        var directory = CreateTempDirectory();
        try
        {
            var mediaPath = Path.Combine(directory, "Bonus.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(nfoPath, """
                <episodedetails>
                  <title>Bonus</title><lockedfields>Name</lockedfields>
                  <uniqueid type="tvdb">100</uniqueid><tvdbid>100</tvdbid>
                  <uniqueid type="imdb">tt1234567</uniqueid><imdbid>tt1234567</imdbid>
                  <uniqueid type="tmdb">900</uniqueid>
                </episodedetails>
                """);
            var service = new EmbyNfoProviderIdService();
            var result = service.UpdateProviderIds(mediaPath, EmbyProviderIds.Empty,
                removeImdbId: removeImdb, removeTvdbId: removeTvdb);

            Assert.True(result.Success);
            Assert.True(result.NfoChanged);
            var content = File.ReadAllText(nfoPath);
            Assert.Equal(!removeTvdb, content.Contains("tvdb", StringComparison.Ordinal));
            Assert.Equal(!removeImdb, content.Contains("imdb", StringComparison.Ordinal));
            Assert.Contains("<title>Bonus</title>", content);
            Assert.Contains("<lockedfields>Name</lockedfields>", content);
            Assert.Contains("tmdb", content);
            var repeated = service.UpdateProviderIds(mediaPath, EmbyProviderIds.Empty,
                removeImdbId: removeImdb, removeTvdbId: removeTvdb);
            Assert.True(repeated.Success);
            Assert.False(repeated.NfoChanged);
            Assert.Equal(content, File.ReadAllText(nfoPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Updates_PreserveUnrelatedXmlIncludingCarriageReturns(bool updateText)
    {
        var directory = CreateTempDirectory();
        try
        {
            var mediaPath = Path.Combine(directory, "Episode.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(nfoPath, """
                <?xml version="1.0" encoding="utf-8"?>
                <episodedetails custom="a&#x9;b">
                  <!--keep this comment-->
                  <title>Old</title>
                  <plot>First&#xD;Second &amp; Third<![CDATA[<literal>]]></plot>
                  <x:extension xmlns:x="urn:custom" attr="a&#xD;b"><x:value>kept</x:value></x:extension>
                  <uniqueid type="tmdb">777</uniqueid>
                </episodedetails>
                """);
            var before = System.Xml.Linq.XDocument.Load(nfoPath);
            var service = new EmbyNfoProviderIdService();

            var result = updateText
                ? service.UpdateTextFields(mediaPath, new EmbyNfoTextFields("New", null))
                : service.UpdateProviderIds(mediaPath, new EmbyProviderIds("123", null));

            Assert.True(result.Success, result.Message);
            var after = System.Xml.Linq.XDocument.Load(nfoPath);
            Assert.Equal("First\rSecond & Third<literal>", after.Root!.Element("plot")!.Value);
            Assert.True(System.Xml.Linq.XNode.DeepEquals(before.Root!.Element("plot"), after.Root.Element("plot")));
            Assert.True(System.Xml.Linq.XNode.DeepEquals(
                before.Root.Element(System.Xml.Linq.XName.Get("extension", "urn:custom")),
                after.Root.Element(System.Xml.Linq.XName.Get("extension", "urn:custom"))));
            Assert.Equal(before.Root.Attribute("custom")!.Value, after.Root.Attribute("custom")!.Value);
            Assert.Contains("<!--keep this comment-->", File.ReadAllText(nfoPath), StringComparison.Ordinal);
            Assert.Equal("777", after.Root.Elements("uniqueid").Single(element => (string?)element.Attribute("type") == "tmdb").Value);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("<movie><title>Movie</title></movie>")]
    [InlineData("<episodedetails xmlns=\"urn:unknown\"><title>Episode</title></episodedetails>")]
    [InlineData("<!DOCTYPE episodedetails [<!ENTITY title 'Entity'>]><episodedetails><title>&title;</title></episodedetails>")]
    [InlineData("<episodedetails><title>broken")]
    public void UnsupportedOrMalformedNfo_IsReportedAndNeverChanged(string xml)
    {
        var directory = CreateTempDirectory();
        try
        {
            var mediaPath = Path.Combine(directory, "Episode.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(nfoPath, xml);
            var before = File.ReadAllBytes(nfoPath);
            var service = new EmbyNfoProviderIdService();

            Assert.NotNull(service.ReadEpisodeMetadata(mediaPath).WarningMessage);
            Assert.False(service.UpdateProviderIds(mediaPath, new EmbyProviderIds("123", null)).Success);
            Assert.False(service.UpdateTextFields(mediaPath, new EmbyNfoTextFields("New", null)).Success);
            Assert.Equal(before, File.ReadAllBytes(nfoPath));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ProviderIds_IgnoreEmptyDefaultsAndPreserveCanonicalAttributes()
    {
        var directory = CreateTempDirectory();
        try
        {
            var mediaPath = Path.Combine(directory, "Episode.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(nfoPath, """
                <episodedetails><uniqueid type="tvdb">123</uniqueid><uniqueid type="tvdb" default="true" source="keep" /><imdbid /><imdbid>tt1234567</imdbid></episodedetails>
                """);
            var service = new EmbyNfoProviderIdService();
            var ids = service.ReadProviderIds(mediaPath).ProviderIds;
            Assert.Equal("123", ids.TvdbId);
            Assert.Equal("tt1234567", ids.ImdbId);

            Assert.True(service.UpdateProviderIds(mediaPath, new EmbyProviderIds(" 123 ", null)).Success);
            var canonical = Assert.Single(System.Xml.Linq.XDocument.Load(nfoPath).Root!.Elements("uniqueid"));
            Assert.Equal("true", canonical.Attribute("default")!.Value);
            Assert.Equal("keep", canonical.Attribute("source")!.Value);
            Assert.Equal("123", canonical.Value);
            var bytes = File.ReadAllBytes(nfoPath);
            Assert.False(service.UpdateProviderIds(mediaPath, new EmbyProviderIds(" 123 ", null)).NfoChanged);
            Assert.Equal(bytes, File.ReadAllBytes(nfoPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UpdateTextFields_MergesDuplicateLocksWithoutDroppingUnrelatedFields()
    {
        var directory = CreateTempDirectory();
        try
        {
            var mediaPath = Path.Combine(directory, "Episode.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(nfoPath, "<episodedetails><lockedfields>Name|Actors</lockedfields><lockedfields>SortName|Studios</lockedfields></episodedetails>");
            var service = new EmbyNfoProviderIdService();
            Assert.True(service.ReadEpisodeMetadata(mediaPath).IsSortTitleLocked);

            var result = service.UpdateTextFields(mediaPath, new EmbyNfoTextFields(null, null, LockTitle: false));

            Assert.True(result.Success, result.Message);
            var locks = Assert.Single(System.Xml.Linq.XDocument.Load(nfoPath).Root!.Elements("lockedfields"));
            Assert.Equal("Actors|SortName|Studios", locks.Value);
            Assert.False(service.UpdateTextFields(mediaPath, new EmbyNfoTextFields(null, null, LockTitle: false)).NfoChanged);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UpdateTextFields_WithUnchangedSignificantWhitespace_IsANoOp()
    {
        var directory = CreateTempDirectory();
        try
        {
            var mediaPath = Path.Combine(directory, "Episode.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            const string xml = "<episodedetails><title> Title </title><sorttitle> Sort </sorttitle></episodedetails>";
            File.WriteAllText(nfoPath, xml);

            var result = new EmbyNfoProviderIdService().UpdateTextFields(mediaPath, new EmbyNfoTextFields(" Title ", " Sort "));

            Assert.True(result.Success);
            Assert.False(result.NfoChanged);
            Assert.Equal(xml, File.ReadAllText(nfoPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("true", "Genres", true, true)]
    [InlineData("TRUE", "", true, true)]
    [InlineData(" true ", "Name", true, true)]
    [InlineData("false", "Name|Genres", true, false)]
    [InlineData("false", "SortName", false, true)]
    [InlineData("", "Genres", false, false)]
    public void ReadEpisodeMetadata_GlobalLockAlsoLocksBothTitleFields(
        string lockData, string lockedFields, bool titleLocked, bool sortTitleLocked)
    {
        var directory = CreateTempDirectory();
        try
        {
            var mediaPath = Path.Combine(directory, "Episode.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(nfoPath, $"<episodedetails><lockdata>{lockData}</lockdata><lockedfields>{lockedFields}</lockedfields></episodedetails>");

            var metadata = new EmbyNfoProviderIdService().ReadEpisodeMetadata(mediaPath);

            Assert.Null(metadata.WarningMessage);
            Assert.Equal(titleLocked, metadata.IsTitleLocked);
            Assert.Equal(sortTitleLocked, metadata.IsSortTitleLocked);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(null, false)]
    [InlineData(false, false)]
    public void UpdateTextFields_RejectsIndividualUnlockUnderGlobalLockWithoutAnyWrite(bool? lockTitle, bool? lockSortTitle)
    {
        var directory = CreateTempDirectory();
        try
        {
            var mediaPath = Path.Combine(directory, "Episode.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(nfoPath, "<episodedetails><title>Alt</title><lockdata>true</lockdata><lockedfields>Name|Actors</lockedfields><lockedfields>SortName|Studios</lockedfields></episodedetails>");
            var before = File.ReadAllBytes(nfoPath);
            var service = new EmbyNfoProviderIdService();

            var result = service.UpdateTextFields(mediaPath, new EmbyNfoTextFields("Neu", null, lockTitle, lockSortTitle));

            Assert.False(result.Success);
            Assert.False(result.NfoChanged);
            Assert.Contains("lockdata=true", result.Message, StringComparison.Ordinal);
            Assert.Equal(before, File.ReadAllBytes(nfoPath));
            Assert.True(service.ReadEpisodeMetadata(mediaPath).IsTitleLocked);
            Assert.True(service.ReadEpisodeMetadata(mediaPath).IsSortTitleLocked);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UpdateTextFields_PreservesGlobalAndUnrelatedLocksDuringExplicitTextEdit(bool explicitLocks)
    {
        var directory = CreateTempDirectory();
        try
        {
            var mediaPath = Path.Combine(directory, "Episode.mkv");
            var nfoPath = Path.ChangeExtension(mediaPath, ".nfo");
            File.WriteAllText(nfoPath, "<episodedetails><title>Alt</title><lockdata>true</lockdata><lockedfields>Actors|Studios</lockedfields></episodedetails>");
            var service = new EmbyNfoProviderIdService();
            var fields = new EmbyNfoTextFields("Neu", null,
                LockTitle: explicitLocks ? true : null, LockSortTitle: explicitLocks ? true : null);

            var result = service.UpdateTextFields(mediaPath, fields);

            Assert.True(result.Success, result.Message);
            Assert.True(result.NfoChanged);
            var root = System.Xml.Linq.XDocument.Load(nfoPath).Root!;
            Assert.Equal("Neu", root.Element("title")!.Value);
            Assert.Equal("true", root.Element("lockdata")!.Value);
            Assert.Equal("Actors|Studios", Assert.Single(root.Elements("lockedfields")).Value);
            var beforeRepeat = File.ReadAllBytes(nfoPath);
            var repeated = service.UpdateTextFields(mediaPath, fields);
            Assert.True(repeated.Success);
            Assert.False(repeated.NfoChanged);
            Assert.Equal(beforeRepeat, File.ReadAllBytes(nfoPath));
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
