using System.IO;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class PathComparisonHelperTests
{
    [Fact]
    public void AreSamePath_IgnoresTrailingDirectorySeparators()
    {
        var left = @"C:\Archiv\Serie\Season 1\";
        var right = @"C:\Archiv\Serie\Season 1";

        Assert.True(PathComparisonHelper.AreSamePath(left, right));
    }

    [Fact]
    public void IsPathWithinRoot_RequiresDirectoryBoundary()
    {
        var root = @"C:\Archiv\Serie";

        Assert.True(PathComparisonHelper.IsPathWithinRoot(@"C:\Archiv\Serie\Season 1\Episode.mkv", root));
        Assert.False(PathComparisonHelper.IsPathWithinRoot(@"C:\Archiv\Serie-Test\Episode.mkv", root));
    }

    [Fact]
    public void TryGetRelativePathWithinRoot_ReturnsNullOutsideRoot()
    {
        var root = @"C:\Archiv\Serie";

        Assert.Null(PathComparisonHelper.TryGetRelativePathWithinRoot(@"C:\Archiv\Serie-Test\Episode.mkv", root));
        Assert.Equal(
            Path.Combine("Season 1", "Episode.mkv"),
            PathComparisonHelper.TryGetRelativePathWithinRoot(@"C:\Archiv\Serie\Season 1\Episode.mkv", root));
    }

    [Fact]
    public void FileExistsAsDifferentEntry_AllowsCaseOnlyRenameWhenOnlySourceEntryExists()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "path-case-rename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var sourcePath = Path.Combine(tempDirectory, "serie.mkv");
            var targetPath = Path.Combine(tempDirectory, "Serie.mkv");
            File.WriteAllText(sourcePath, "mkv");

            Assert.False(PathComparisonHelper.FileExistsAsDifferentEntry(sourcePath, targetPath));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void FileExistsAsDifferentEntry_BlocksExistingDifferentTarget()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "path-target-conflict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var sourcePath = Path.Combine(tempDirectory, "source.mkv");
            var targetPath = Path.Combine(tempDirectory, "target.mkv");
            File.WriteAllText(sourcePath, "source");
            File.WriteAllText(targetPath, "target");

            Assert.True(PathComparisonHelper.FileExistsAsDifferentEntry(sourcePath, targetPath));
            Assert.False(PathComparisonHelper.FileExistsAsDifferentEntry(sourcePath, sourcePath));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
