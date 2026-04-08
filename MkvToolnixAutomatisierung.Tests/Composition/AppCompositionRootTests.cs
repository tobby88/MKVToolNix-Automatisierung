using Microsoft.Extensions.DependencyInjection;
using MkvToolnixAutomatisierung.Composition;
using MkvToolnixAutomatisierung.Services;
using MkvToolnixAutomatisierung.Tests.TestInfrastructure;
using MkvToolnixAutomatisierung.ViewModels;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Composition;

[Collection("PortableStorage")]
public sealed class AppCompositionRootTests
{
    private readonly PortableStorageFixture _storageFixture;

    public AppCompositionRootTests(PortableStorageFixture storageFixture)
    {
        _storageFixture = storageFixture;
        _storageFixture.Reset();
        ViewModelTestContext.EnsureApplication();
    }

    [Fact]
    public void RegisterAll_ResolvesSingletonServicesViaServiceCollection()
    {
        var services = new ServiceCollection();
        AppCompositionModuleCatalog.RegisterAll(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var first = provider.GetRequiredService<MainWindowViewModel>();
        var second = provider.GetRequiredService<MainWindowViewModel>();
        var archive = provider.GetRequiredService<SeriesArchiveService>();

        Assert.Same(first, second);
        Assert.NotNull(archive);
    }

    [Fact]
    public void Create_ReturnsDisposableAppComposition()
    {
        using var composition = new AppCompositionRoot().Create();

        Assert.NotNull(composition.DialogService);
        Assert.NotNull(composition.SettingsLoadResult);
        Assert.NotNull(composition.MainWindowViewModel);
    }
}
