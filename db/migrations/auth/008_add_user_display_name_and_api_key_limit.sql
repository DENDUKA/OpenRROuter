alter table auth.users
add column if not exists display_name varchar(128) null,
add column if not exists max_api_keys int not null default 10;