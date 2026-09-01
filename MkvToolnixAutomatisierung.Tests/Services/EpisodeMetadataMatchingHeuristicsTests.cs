using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MkvToolnixAutomatisierung.Services.Metadata;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class EpisodeMetadataMatchingHeuristicsTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("SOKO Leipzig", "soko leipzig")]
    [InlineData("A&B", "a und b")]
    [InlineData("A&&B", "a und und b")]
    [InlineData("  Neues---aus Büttenwarder! ", "neues aus buettenwarder")]
    [InlineData("Crème brûlée", "creme brulee")]
    [InlineData("2. Teil", "teil 2")]
    [InlineData("Teil2", "teil 2")]
    [InlineData("Pippi auf der Walze", "pippi auf der walz")]
    [InlineData("Walzer auf der Walze", "walzer auf der walz")]
    [InlineData("Rock & Roll: Teil 2", "rock und roll teil 2")]
    [InlineData("A😀B", "a b")]
    [InlineData("Café", "cafe")]
    public void NormalizeText_ReturnsExpectedCanonicalTitle(string source, string expected)
    {
        Assert.Equal(expected, EpisodeMetadataMatchingHeuristics.NormalizeText(source));
    }

    [Fact]
    public void NormalizeText_MatchesPreviousImplementationAcrossRepresentativeCorpus()
    {
        var stems = new[]
        {
            "Der Alte",
            "Die Wahrheit im Dunkeln",
            "München Mord",
            "Lippmann wird vermißt",
            "Pippi auf der Walze",
            "Crème brûlée",
            "SOKO Leipzig",
            "Rock & Roll",
            "Auf der Tastatur schreiben",
            "第2話",
            "Café",
            "A😀B"
        };
        var templates = new[]
        {
            "{0}",
            "  {0}  ",
            "({0})",
            "{0} - 2. Teil",
            "Teil2: {0}",
            "{0}!!!",
            "{0} & Extra",
            "{0} / Folge 7"
        };

        foreach (var stem in stems)
        {
            foreach (var template in templates)
            {
                var source = string.Format(CultureInfo.InvariantCulture, template, stem);
                Assert.Equal(LegacyNormalizeText(source), EpisodeMetadataMatchingHeuristics.NormalizeText(source));
            }
        }
    }

    [Fact]
    public void NormalizeText_AsciiFastPathAllocatesSubstantiallyLessThanPreviousImplementation()
    {
        var titles = new[]
        {
            "Der Alte - Die Wahrheit im Dunkeln",
            "SOKO Leipzig: Dunkle Wahrheit",
            "Auf der Tastatur schreiben",
            "Rock & Roll - Folge 7",
            "Pettersson und Findus"
        };
        MeasureAllocations(EpisodeMetadataMatchingHeuristics.NormalizeText, titles, iterations: 10);
        MeasureAllocations(LegacyNormalizeText, titles, iterations: 10);

        var optimizedBytes = MeasureAllocations(
            EpisodeMetadataMatchingHeuristics.NormalizeText,
            titles,
            iterations: 1_000);
        var legacyBytes = MeasureAllocations(LegacyNormalizeText, titles, iterations: 1_000);

        Assert.True(
            optimizedBytes * 2 < legacyBytes,
            $"Optimiert: {optimizedBytes:N0} Bytes; bisheriger Pfad: {legacyBytes:N0} Bytes.");
    }

    private static long MeasureAllocations(Func<string, string> normalize, IReadOnlyList<string> titles, int iterations)
    {
        var checksum = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            foreach (var title in titles)
            {
                checksum += normalize(title).Length;
            }
        }

        GC.KeepAlive(checksum);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static string LegacyNormalizeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.ToLowerInvariant()
            .Replace("ä", "ae")
            .Replace("ö", "oe")
            .Replace("ü", "ue")
            .Replace("ß", "ss");
        normalized = Regex.Replace(
            normalized,
            @"\b(?<number>\d+)\s*\.?\s*teil\b",
            "teil ${number}",
            RegexOptions.IgnoreCase);
        normalized = Regex.Replace(
            normalized,
            @"\bteil\s*(?<number>\d+)\b",
            "teil ${number}",
            RegexOptions.IgnoreCase);
        normalized = RemoveDiacritics(normalized);
        normalized = Regex.Replace(normalized, @"\bwalze\b", "walz", RegexOptions.IgnoreCase);
        normalized = normalized.Replace("&", " und ");
        normalized = new string(normalized.Select(character => char.IsLetterOrDigit(character) ? character : ' ').ToArray());
        normalized = string.Join(" ", normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return normalized.Trim();
    }

    private static string RemoveDiacritics(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
