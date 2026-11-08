-- Apply after 001-initial-schema.sql using a database migration/admin account.
-- Runtime API credentials need SELECT/INSERT/UPDATE on these tables, not schema-admin privileges.

CREATE TABLE IF NOT EXISTS borne_event (
    event_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    borne_id VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    event_type VARCHAR(64) NOT NULL,
    payload_json JSON NOT NULL,
    payload_sha256 BINARY(32) NOT NULL,
    occurred_at_utc DATETIME(6) NOT NULL,
    received_at_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (event_id),
    KEY ix_borne_event_history (borne_id, received_at_utc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS borne_runtime_status (
    borne_id VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    last_heartbeat_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (borne_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
