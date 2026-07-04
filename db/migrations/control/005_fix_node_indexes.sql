-- Replace separate (enabled) and (status) indexes with a composite one.
-- Queries filter by enabled = true AND status = 'Healthy'.

DROP INDEX IF EXISTS control.ix_control_router_nodes_enabled;
DROP INDEX IF EXISTS control.ix_control_router_nodes_status;

CREATE INDEX IF NOT EXISTS ix_control_router_nodes_enabled_status
    ON control.router_nodes (enabled, status);
