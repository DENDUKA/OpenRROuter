using Microsoft.Extensions.Options;

namespace OpenRussiaRouter.WebApp.Services.Payments;

/// <summary>
/// Resolves an <see cref="IPaymentProvider"/> by its <see cref="IPaymentProvider.Name"/>.
/// The active provider is selected via <c>Payments:ActiveProvider</c> in config; webhook
/// routing uses the URL segment (<c>/webhook/{provider}</c>) to look up the right implementation.
/// </summary>
public sealed class PaymentProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IPaymentProvider> _byName;
    private readonly PaymentSettings _settings;

    public PaymentProviderRegistry(
        IEnumerable<IPaymentProvider> providers,
        IOptions<PaymentSettings> options)
    {
        _settings = options.Value;

        var byName = new Dictionary<string, IPaymentProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in providers)
        {
            if (string.IsNullOrWhiteSpace(p.Name))
            {
                throw new InvalidOperationException(
                    $"IPaymentProvider implementation {p.GetType().FullName} returned an empty Name.");
            }
            // Last registration wins — same semantics as DI container for the default scheme.
            byName[p.Name.ToLowerInvariant()] = p;
        }
        _byName = byName;
    }

    /// <summary>Enumerate registered provider names (for diagnostics / startup guard).</summary>
    public IReadOnlyCollection<string> RegisteredNames => _byName.Keys.ToList();

    /// <summary>The provider selected by <c>Payments:ActiveProvider</c>. Null if unset.</summary>
    public IPaymentProvider? Active => string.IsNullOrWhiteSpace(_settings.ActiveProvider)
        ? null
        : TryGet(_settings.ActiveProvider!);

    /// <summary>Lookup by name (URL segment). Returns null when not registered.</summary>
    public IPaymentProvider? TryGet(string name) =>
        string.IsNullOrWhiteSpace(name) ? null : _byName.TryGetValue(name.ToLowerInvariant(), out var p) ? p : null;

    /// <summary>Lookup by name. Throws if not registered.</summary>
    public IPaymentProvider Get(string name) =>
        TryGet(name) ?? throw new InvalidOperationException(
            $"Payment provider '{name}' is not registered. Known: {string.Join(", ", _byName.Keys)}.");
}
