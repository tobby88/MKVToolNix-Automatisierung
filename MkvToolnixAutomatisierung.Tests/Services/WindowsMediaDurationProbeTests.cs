using System.IO;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Services;

public sealed class WindowsMediaDurationProbeTests
{
    [Fact]
    public void TryReadDuration_RetriesMissingResultAndCachesSuccess()
    {
        var path = Path.GetTempFileName();
        try
        {
            var reads = 0;
            var probe = new WindowsMediaDurationProbe(_ => ++reads == 1 ? null : TimeSpan.FromMinutes(42));

            Assert.Null(probe.TryReadDuration(path));
            Assert.Equal(TimeSpan.FromMinutes(42), probe.TryReadDuration(path));
            Assert.Equal(TimeSpan.FromMinutes(42), probe.TryReadDuration(path));
            Assert.Equal(2, reads);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
