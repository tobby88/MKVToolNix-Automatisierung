using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class BufferedTextStoreTests
{
    [Fact]
    public void AppendLine_UsesIncrementalFlushWhenAppendCallbackIsProvided()
    {
        Action? scheduledFlush = null;
        var replacedTexts = new List<string>();
        var appendedTexts = new List<string>();
        var store = new BufferedTextStore(
            flush => scheduledFlush = flush,
            text => replacedTexts.Add(text),
            text => appendedTexts.Add(text));

        store.Reset("Start");
        store.AppendLine("Erste Zeile");
        store.AppendLine("Zweite Zeile");
        Assert.NotNull(scheduledFlush);

        scheduledFlush!();

        Assert.Equal(["Start"], replacedTexts);
        Assert.Equal([$"Erste Zeile{Environment.NewLine}Zweite Zeile{Environment.NewLine}"], appendedTexts);
        Assert.Equal($"StartErste Zeile{Environment.NewLine}Zweite Zeile{Environment.NewLine}", store.GetTextSnapshot());
    }

    [Fact]
    public void Reset_DropsPendingAppendFromStaleScheduledFlush()
    {
        Action? scheduledFlush = null;
        var replacedTexts = new List<string>();
        var appendedTexts = new List<string>();
        var store = new BufferedTextStore(
            flush => scheduledFlush = flush,
            text => replacedTexts.Add(text),
            text => appendedTexts.Add(text));

        store.AppendLine("Veraltete Zeile");
        Assert.NotNull(scheduledFlush);

        store.Reset("Neu");
        scheduledFlush!();

        Assert.Equal(["Neu"], replacedTexts);
        Assert.Empty(appendedTexts);
        Assert.Equal("Neu", store.GetTextSnapshot());
    }
}
