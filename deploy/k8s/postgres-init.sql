CREATE TABLE vehicles (
    id          text        PRIMARY KEY,
    state       text        NOT NULL
                CHECK (state IN ('REGISTERED','ACTIVE','IDLE','MAINTENANCE','DECOMMISSIONED')),
    updated_at  timestamptz NOT NULL
);

CREATE TABLE positions (
    id           bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    vehicle_id   text             NOT NULL,
    lat          double precision NOT NULL,
    lng          double precision NOT NULL,
    recorded_at  timestamptz      NOT NULL
);

-- Delete e update trazem a linha antiga completa no "before"
ALTER TABLE vehicles REPLICA IDENTITY FULL;

-- Publication explícita: o Debezium não precisa de permissão para criá-la
CREATE PUBLICATION fleet_pub FOR TABLE vehicles, positions;