using System.Net;
using System.Net.Http;

namespace MkvToolnixAutomatisierung.Tests.TestInfrastructure;

/// <summary>Offline-HEAD-Antworten für Einstellungsaktionen; ermöglicht einen gezielten Abbruch während der Abfrage.</summary>
internal sealed class ImdbCheckHttpHandler : HttpMessageHandler
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool WaitForCancellation { get; set; }
    public int RequestCount { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Head) throw new InvalidOperationException("A check must not download dataset contents.");
        RequestCount++;
        Started.TrySetResult();
        if (WaitForCancellation) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        response.Content.Headers.ContentLength = 0;
        response.Content.Headers.LastModified = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return response;
    }
}
