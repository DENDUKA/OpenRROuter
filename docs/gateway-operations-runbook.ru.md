# Gateway operations runbook

## GatewayDb dependency

`OpenRussiaRouter.ApiGateway` uses `ConnectionStrings:GatewayDb` for durable `gateway.usage_outbox` storage. For production public API instances this database is a required dependency: if Gateway cannot enqueue a non-zero usage event, the request fails with HTTP 500 and the upstream response body is not returned to the client.

Operational requirements:

- monitor GatewayDb availability on the same on-call page as ApiGateway availability;
- place GatewayDb in the same VM or VPC as ApiGateway; do not use cross-region PostgreSQL for this dependency;
- treat `GatewayDb down => public API down` as expected behavior that protects billing consistency;
- local development and unit tests may run without `ConnectionStrings:GatewayDb`, in which case ApiGateway logs that the outbox is non-durable and uses in-memory storage.
