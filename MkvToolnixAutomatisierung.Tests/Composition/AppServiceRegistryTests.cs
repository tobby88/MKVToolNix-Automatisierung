using MkvToolnixAutomatisierung.Composition;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.Composition;

public sealed class AppServiceRegistryTests
{
    [Fact]
    public void AddSingleton_ResolvesFactoryOnlyOnce()
    {
        var registry = new AppServiceRegistry();
        var callCount = 0;

        registry.AddSingleton<object>(_ =>
        {
            callCount++;
            return new object();
        });

        var first = registry.GetRequired<object>();
        var second = registry.GetRequired<object>();

        Assert.Same(first, second);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void GetRequired_ThrowsForMissingRegistration()
    {
        var registry = new AppServiceRegistry();

        var exception = Assert.Throws<InvalidOperationException>(() => registry.GetRequired<object>());

        Assert.Contains("wurde nicht registriert", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddSingleton_ThrowsForDuplicateRegistration()
    {
        var registry = new AppServiceRegistry();
        registry.AddSingleton<object>(_ => new object());

        var exception = Assert.Throws<InvalidOperationException>(() => registry.AddSingleton<object>(_ => new object()));

        Assert.Contains("bereits registriert", exception.Message, StringComparison.Ordinal);
    }
}
