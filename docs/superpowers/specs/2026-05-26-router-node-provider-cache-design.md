# Router Node Provider Cache Design

## Goal

Reduce repeated HTTP calls from ControlPlane to router nodes when admin pages refresh or several views request the same node data close together.

## Scope

Add a small in-memory cache around `IRouterNodeProvider`. The cache lives in the ControlPlane process and stores successful read responses for a configurable TTL, defaulting to 30 seconds.

## Cached Operations

The cache applies to read-only methods:

- `GetProviderQuotaAsync`
- `ListProviderModelsAsync`
- `ListIntegrationKeysAsync`
- `ListModelsAsync`
- `ListProvidersAsync`

The cache does not store exceptions or failed requests. If a node request fails, the next caller can retry immediately.

## Mutations

Mutation methods pass through to the HTTP provider:

- `UpdateProviderStatusAsync`
- `CreateIntegrationKeyAsync`
- `UpdateIntegrationKeyStatusAsync`

After a successful mutation, cached entries for that node are invalidated so admin pages do not keep showing stale data.

## Configuration

Add a `RouterNodeProviderCache` appsettings section:

```json
{
  "RouterNodeProviderCache": {
    "Enabled": true,
    "Ttl": "00:00:30"
  }
}
```

## Implementation Shape

Register the concrete HTTP client as `RouterNodeProvider`, then register `IRouterNodeProvider` as a `CachedRouterNodeProvider` decorator. The decorator uses a shared in-memory cache keyed by node id plus method arguments.

## Testing

Unit tests cover repeated reads, disabled cache behavior, per-node invalidation after mutations, and not caching failures.
