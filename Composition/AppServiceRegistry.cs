namespace MkvToolnixAutomatisierung.Composition;

/// <summary>
/// Sehr kleine Singleton-Registry für den Composition Root.
/// </summary>
/// <remarks>
/// Die API ist bewusst an spätere DI-Registrierungen angelehnt: Module registrieren ihre Abhängigkeiten zentral,
/// damit ein zukünftiger Wechsel auf <c>Microsoft.Extensions.DependencyInjection</c> nur die technische Hülle, nicht
/// mehr die fachliche Modulaufteilung betrifft.
/// </remarks>
internal sealed class AppServiceRegistry
{
    private readonly Dictionary<Type, Func<AppServiceRegistry, object>> _factories = [];
    private readonly Dictionary<Type, object> _singletons = [];

    /// <summary>
    /// Registriert einen Singleton-Fabrikdelegaten für einen Servicetyp.
    /// </summary>
    public AppServiceRegistry AddSingleton<TService>(Func<AppServiceRegistry, TService> factory)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(factory);

        var serviceType = typeof(TService);
        if (_factories.ContainsKey(serviceType))
        {
            throw new InvalidOperationException($"Der Servicetyp {serviceType.FullName} wurde bereits registriert.");
        }

        _factories[serviceType] = services => factory(services)
            ?? throw new InvalidOperationException($"Die Factory für {serviceType.FullName} hat null zurückgegeben.");
        return this;
    }

    /// <summary>
    /// Löst einen zuvor registrierten Singleton-Dienst auf.
    /// </summary>
    public TService GetRequired<TService>()
        where TService : class
    {
        return (TService)GetRequired(typeof(TService));
    }

    private object GetRequired(Type serviceType)
    {
        if (_singletons.TryGetValue(serviceType, out var cached))
        {
            return cached;
        }

        if (!_factories.TryGetValue(serviceType, out var factory))
        {
            throw new InvalidOperationException($"Der Servicetyp {serviceType.FullName} wurde nicht registriert.");
        }

        var instance = factory(this);
        _singletons[serviceType] = instance;
        return instance;
    }
}
