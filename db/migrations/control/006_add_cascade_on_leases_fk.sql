-- Add ON DELETE CASCADE to router_leases FK so that node deletion
-- automatically removes associated leases. This is a safety net:
-- RemoveNodeAsync already deletes leases explicitly before the node.
alter table control.router_leases
    drop constraint if exists router_leases_node_id_fkey;

alter table control.router_leases
    add constraint router_leases_node_id_fkey
    foreign key (node_id) references control.router_nodes(id) on delete cascade;
