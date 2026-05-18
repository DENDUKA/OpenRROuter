insert into auth.users (id, email, role, status, password_hash, created_at_utc)
select
    'user-admin-default',
    'admin@example.com',
    'Admin',
    'Active',
    'pbkdf2-sha256.210000.T3BlblJ1c3NpYUFkbWluIQ==.VmZIzhJ+iIemWRUCr8NvIaTH5owirvHAm0R4Ip3Yrhk=',
    now()
where not exists (
    select 1
    from auth.users
    where lower(email) = 'admin@example.com'
);
