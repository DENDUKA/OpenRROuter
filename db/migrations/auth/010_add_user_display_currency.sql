alter table auth.users add column if not exists display_currency varchar(8) not null default 'RUB';
