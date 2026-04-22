using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using MkvToolnixAutomatisierung.Composition;
using MkvToolnixAutomatisierung.Services;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Composition;

public sealed class ToolingCompositionModuleTests
{
    [Fact]
    public void Register_ConfiguresShortHttpClientTimeoutForStartup()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new AppToolPathStore(new AppSettingsStore()));
        ToolingCompositionModule.Register(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var client = provider.GetRequiredService<HttpClient>();

        Assert.Equal(TimeSpan.FromSeconds(15), client.Timeout);
    }
}
