-- Admin Dashboard Identity Schema
-- PostgreSQL-native implementation for ASP.NET Core Identity
-- Separate database from Orleans game data

-- ============================================================
-- ASP.NET Core Identity Tables
-- ============================================================

CREATE TABLE IF NOT EXISTS admin_users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_name varchar(256),
    normalized_user_name varchar(256),
    email varchar(256),
    normalized_email varchar(256),
    email_confirmed boolean NOT NULL DEFAULT false,
    password_hash text,
    security_stamp text,
    concurrency_stamp text,
    phone_number text,
    phone_number_confirmed boolean NOT NULL DEFAULT false,
    two_factor_enabled boolean NOT NULL DEFAULT false,
    lockout_end timestamptz,
    lockout_enabled boolean NOT NULL DEFAULT false,
    access_failed_count integer NOT NULL DEFAULT 0,
    display_name varchar(256),
    created_at timestamptz NOT NULL DEFAULT now(),
    last_login_at timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_admin_users_normalized_user_name 
    ON admin_users (normalized_user_name);
CREATE UNIQUE INDEX IF NOT EXISTS ix_admin_users_normalized_email 
    ON admin_users (normalized_email);

CREATE TABLE IF NOT EXISTS admin_roles (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name varchar(256),
    normalized_name varchar(256),
    concurrency_stamp text,
    description text
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_admin_roles_normalized_name 
    ON admin_roles (normalized_name);

CREATE TABLE IF NOT EXISTS admin_user_roles (
    user_id UUID NOT NULL,
    role_id UUID NOT NULL,
    CONSTRAINT pk_admin_user_roles PRIMARY KEY (user_id, role_id),
    CONSTRAINT fk_admin_user_roles_users FOREIGN KEY (user_id) REFERENCES admin_users(id) ON DELETE CASCADE,
    CONSTRAINT fk_admin_user_roles_roles FOREIGN KEY (role_id) REFERENCES admin_roles(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_admin_user_roles_role_id ON admin_user_roles (role_id);

CREATE TABLE IF NOT EXISTS admin_user_claims (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL,
    claim_type text,
    claim_value text,
    CONSTRAINT fk_admin_user_claims_users FOREIGN KEY (user_id) REFERENCES admin_users(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_admin_user_claims_user_id ON admin_user_claims (user_id);

CREATE TABLE IF NOT EXISTS admin_user_logins (
    login_provider varchar(128) NOT NULL,
    provider_key varchar(128) NOT NULL,
    provider_display_name text,
    user_id UUID NOT NULL,
    CONSTRAINT pk_admin_user_logins PRIMARY KEY (login_provider, provider_key),
    CONSTRAINT fk_admin_user_logins_users FOREIGN KEY (user_id) REFERENCES admin_users(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_admin_user_logins_user_id ON admin_user_logins (user_id);

CREATE TABLE IF NOT EXISTS admin_role_claims (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    role_id UUID NOT NULL,
    claim_type text,
    claim_value text,
    CONSTRAINT fk_admin_role_claims_roles FOREIGN KEY (role_id) REFERENCES admin_roles(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_admin_role_claims_role_id ON admin_role_claims (role_id);

CREATE TABLE IF NOT EXISTS admin_user_tokens (
    user_id UUID NOT NULL,
    login_provider varchar(128) NOT NULL,
    name varchar(128) NOT NULL,
    value text,
    CONSTRAINT pk_admin_user_tokens PRIMARY KEY (user_id, login_provider, name),
    CONSTRAINT fk_admin_user_tokens_users FOREIGN KEY (user_id) REFERENCES admin_users(id) ON DELETE CASCADE
);
