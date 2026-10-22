using MkvToolnixAutomatisierung.Services.Metadata;
using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class EpisodeEditModelEpisodeCodeTests
{
    [Theory]
    [InlineData("01", "02")]
    [InlineData("00", "01")]
    [InlineData("2014", "05-E06")]
    public void KnownEpisodeCode_DoesNotRequireUnnumberedApproval(string season, string episode)
    {
        var item = new TestEpisode(season, episode);

        Assert.False(item.HasUnresolvedEpisodeCode);
    }

    [Theory]
    [InlineData("xx", "xx")]
    [InlineData("00", "xx")]
    [InlineData("xx", "01")]
    public void MissingCode_IsNotApprovedByGeneralMetadataApproval(string season, string episode)
    {
        var item = new TestEpisode(season, episode);
        item.ApproveMetadataReview("Metadaten manuell angepasst.");

        Assert.True(item.HasUnresolvedEpisodeCode);
    }

    [Fact]
    public void KeptLocalDetection_RequiresFinalMetadataApproval()
    {
        var item = new TestEpisode();
        item.ApplyLocalMetadataGuess();

        Assert.True(item.HasUnresolvedEpisodeCode);

        item.ApproveMetadataReview("Lokale Erkennung beibehalten.");

        Assert.False(item.HasUnresolvedEpisodeCode);
        Assert.Equal("xx", item.SeasonNumber);
        Assert.Equal("xx", item.EpisodeNumber);
    }

    [Theory]
    [InlineData("series")]
    [InlineData("season")]
    [InlineData("episode")]
    public void KeptLocalDetection_DoesNotApproveChangedEpisodeIdentity(string changedField)
    {
        var item = CreateApprovedTrailer();

        switch (changedField)
        {
            case "series": item.SeriesName = "Andere Serie"; break;
            case "season": item.SeasonNumber = "01"; break;
            case "episode": item.EpisodeNumber = "02"; break;
        }

        Assert.True(item.HasUnresolvedEpisodeCode);
    }

    [Fact]
    public void KeptLocalDetection_AllowsManualTitleCorrection()
    {
        var item = CreateApprovedTrailer();

        item.Title = "Trailer zur nächsten Staffel";

        Assert.False(item.HasUnresolvedEpisodeCode);
    }

    [Fact]
    public void KeptNumberedLocalDetection_DoesNotApproveRemovingItsCode()
    {
        var item = new TestEpisode("01", "02");
        item.ApplyLocalMetadataGuess();
        item.ApproveMetadataReview("Lokale Erkennung beibehalten.");

        item.SeasonNumber = "xx";
        item.EpisodeNumber = "xx";

        Assert.True(item.HasUnresolvedEpisodeCode);
    }

    [Fact]
    public void RedetectionOfSameMetadata_DoesNotInheritUnnumberedApproval()
    {
        var item = CreateApprovedTrailer();

        item.ResetDetection();
        item.ApproveMetadataReview("Automatik bestätigt.");

        Assert.True(item.HasUnresolvedEpisodeCode);
    }

    [Fact]
    public void TvdbSelection_DiscardsPreviousUnnumberedApproval()
    {
        var item = CreateApprovedTrailer();

        item.ApplyTvdbSelection(new TvdbEpisodeSelection(42, "Beispielserie", 123, "Pilot", "01", "02"));
        item.SeasonNumber = "xx";
        item.EpisodeNumber = "xx";

        Assert.True(item.HasUnresolvedEpisodeCode);
    }

    private static TestEpisode CreateApprovedTrailer()
    {
        var item = new TestEpisode();
        item.ApplyLocalMetadataGuess();
        item.ApproveMetadataReview("Lokale Erkennung beibehalten.");
        Assert.False(item.HasUnresolvedEpisodeCode);
        return item;
    }

    private sealed class TestEpisode : EpisodeEditModel
    {
        private readonly EpisodeMetadataGuess _guess;

        public TestEpisode(string season = "xx", string episode = "xx")
        {
            _guess = new EpisodeMetadataGuess("Beispielserie", "Trailer", season, episode);
            ResetDetection();
        }

        public void ResetDetection()
        {
            SetLocalMetadataGuess(_guess);
            SeriesName = _guess.SeriesName;
            SeasonNumber = _guess.SeasonNumber;
            EpisodeNumber = _guess.EpisodeNumber;
            Title = _guess.EpisodeTitle;
            SetMetadataResolutionState(new EpisodeMetadataResolutionResult(_guess, null,
                "TVDB nicht geprüft.", 0, RequiresReview: false, QueryWasAttempted: false, QuerySucceeded: false));
        }
    }
}
