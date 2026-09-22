using System.IO;
using System.Text;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class CompanionTextMetadataReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "companion-tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    public void Read_RecognizesFirstFieldAndUmlautsWithBom(string encodingName)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "source.txt");
        File.WriteAllText(path, "Sender: SRF\nThema: Eine Serie\nTitel: Mâcon und Zürich", Encoding.GetEncoding(encodingName));
        var result = CompanionTextMetadataReader.Read(path);
        Assert.Equal("SRF", result.Sender);
        Assert.Equal("Mâcon und Zürich", result.Title);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Read_EmptyFieldsDoNotConsumeFollowingLabels(string newline)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "source.txt");
        File.WriteAllText(path, $"Sender: {newline}Thema: Eine Serie{newline}Titel: {newline}Dauer: 00:45:00{newline}");
        var result = CompanionTextMetadataReader.Read(path);
        Assert.Null(result.Sender);
        Assert.Equal("Eine Serie", result.Topic);
        Assert.Null(result.Title);
        Assert.Equal(TimeSpan.FromMinutes(45), result.Duration);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
