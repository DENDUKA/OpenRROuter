# Combo catalog in model routing

## Goal

Make 9Router Combo aliases the routable values shown in the OpenRussiaRouter
model-admin interface. A card such as `deepseek-v4-flash` must route through
the Combo, rather than directly to `ocg/deepseek-v4-flash`.

## Current behaviour

The Collector builds the node catalog from
`/api/integrations/providers/with-models`, with `/v1/models` as a fallback.
Neither endpoint lists Combo aliases. The 9Router dashboard loads Combos from
its separate authenticated endpoint, `/api/combos`.

Consequently `ListAvailableNodeModelsAsync` exposes provider model IDs only,
and the admin page cannot select a Combo alias for `Router Model`.

## Design

1. Extend `INodeAdminClient` and `NodeAdminClient` with an authenticated
   `GET /api/combos` request using the existing node control-token headers.
2. Add compact DTOs for the endpoint response: each Combo contributes its
   `name` and its member model IDs.
3. After collecting provider models, merge every valid Combo into the same
   node snapshot as a `ModelObservation` whose `UpstreamId` and `PublicId` are
   both the Combo name. This makes the Combo a capability advertised by that
   node and visible to the WebApp dropdown.
4. If the Combo endpoint is absent or temporarily unavailable, retain the
   successfully collected provider catalog and log a warning. A failure to
   read Combos must not make a healthy node unreachable.
5. The catalog does not infer multimodal or reasoning capabilities for a
   Combo. Its members remain the source of those details; Combo cards retain
   their explicitly curated catalog metadata.
6. Once a node reports the aliases, edit the existing billing model cards so
   `Router Model` equals the Combo name. `Model ID` remains the existing
   public card ID because the current admin UI deliberately makes it
   immutable on edit.

## Scope

The target aliases are the live Combo names except the excluded `GLM-5.2`:
`deepseek-v4-flash`, `deepseek-v4-pro`, `glm-5.1`, `glm-5.2`, `kimi-k2.6`,
`kimi-k2.7-code`, `mimo-v2.5`, `mimo-v2.5-pro`, `minimax-m2.5`,
`minimax-m2.7`, `minimax-m3`, `qwen3.6-plus`, `qwen3.7-max`,
`qwen3.7-plus`, `glm-5`, `gemini-3.1-pro`, `gemini-3.5-flash`, and
`gemini-3-flash`.

## Validation

- Unit-test parsing and catalog projection of Combo observations.
- Unit-test that a Combo API failure preserves provider-model observations.
- Build and run the affected Collector/WebApp test projects.
- After deployment and the next collector poll, verify every alias appears in
  the admin `Router Model` dropdown and update the 18 model cards.

## Alternatives considered

- Allow free-form values in the admin form: quick but does not prove that a
  Combo exists or make ControlPlane capability-aware of it.
- Maintain a separate static Combo list in WebApp: duplicates node state and
  drifts when the 9Router dashboard changes.

The Collector integration is preferred because it makes the node's live
configuration the single source of truth.
