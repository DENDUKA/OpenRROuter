# ApiGateway Retry & Failover Design

## Context
The ApiGateway currently routes requests to router nodes via the ControlPlane. If a node returns a 5xx error, or in the case of streaming, fails before the first chunk is sent, there is currently limited or no retry logic that explicitly excludes nodes that have already failed for the current request.

We want to add a retry mechanism that:
1. Retries requests on 5xx + 4xx errors.
2. Continues retrying as long as there are untried nodes capable of serving the model.
3. Excludes previously failed nodes from subsequent selection.

## Approach: Modify ControlPlane API

To achieve this, we need the ControlPlane to be aware of the nodes we've already tried and failed to reach during the current request.

1. **Update `control-plane.proto`:**
   Add `repeated string excluded_node_ids = 5;` to `SelectRouterNodeRequest`.

2. **Update `IControlPlaneProvider` and `GrpcControlPlaneProvider` in ApiGateway:**
   Modify `SelectRouterNodeAsync` to accept an `IReadOnlyCollection<string>? excludedNodeIds` parameter and pass it in the gRPC request.

3. **Update `GatewayPipeline` and `ChatCompletionHandler`:**
   - In `ChatCompletionHandler.HandleAsync` (non-streaming):
     - Wrap the lease acquisition and proxying in a `while` loop.
     - Keep a `HashSet<string> excludedNodes`.
     - On failure (5xx or 4xx), add the `auth.Lease.NodeId` to `excludedNodes`.
     - Request a new lease, passing `excludedNodes`.
     - If `SelectRouterNodeAsync` returns null (meaning no more nodes available), return the last error response to the client or a generic "No nodes available" if it fails immediately.
   - In `ChatCompletionHandler.ExecuteStreamingAsync`:
     - Update the existing fallback logic to use a similar loop, accumulating `excludedNodes`.
     - Stream retries are only possible *before* the first bytes are sent to the client.

4. **Update ControlPlane implementation:**
   - Ensure the ControlPlane (`RouterControlPlaneService` or its underlying selector) respects `excluded_node_ids` and filters out those nodes when finding an available node.

## Changes

1. **Contracts (`src/OpenRussiaRouter.Contracts/Protos/control-plane.proto`)**
   - `SelectRouterNodeRequest`: Add `repeated string excluded_node_ids = 5;`

2. **ApiGateway (`src/OpenRussiaRouter.ApiGateway/`)**
   - `ControlPlane/IControlPlaneProvider.cs`: Update method signature.
   - `ControlPlane/GrpcControlPlaneProvider.cs`: Pass `excludedNodeIds`.
   - `Application/Services/GatewayPipeline.cs`: Update `AuthorizeAndAcquireLeaseAsync` signature to accept `excludedNodeIds`.
   - `Application/Endpoints/ChatCompletionHandler.cs`: Implement the `while` loops for retrying.

3. **ControlPlane (`src/OpenRussiaRouter.ControlPlane/`)**
   - Needs investigation to see where `SelectRouterNodeRequest` is handled and update the selection logic to filter out `excluded_node_ids`.