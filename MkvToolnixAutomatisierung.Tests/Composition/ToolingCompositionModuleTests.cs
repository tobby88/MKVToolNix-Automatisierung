using System.Net.Http;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MkvToolnixAutomatisierung.Composition;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Composition;

[Collection("PortableStorage")]
public sealed class ToolingCompositionModuleTests
{
    public ToolingCompositionModuleTests(PortableStorageFixture storageFixture)
    {
        storageFixture.Reset();
    }

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

    [Fact]
    public void Register_UsesAssemblyVersionInHttpUserAgent()
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
        var product = Assert.Single(client.DefaultRequestHeaders.UserAgent).Product;
        var informationalVersion = typeof(ToolingCompositionModule).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+')[0];
        var expectedVersion = string.IsNullOrWhiteSpace(informationalVersion)
            ? typeof(ToolingCompositionModule).Assembly.GetName().Version?.ToString(3)
            : informationalVersion;

        Assert.Equal("MkvToolnixAutomatisierung", product?.Name);
        Assert.Equal(expectedVersion, product?.Version);
    }
}
