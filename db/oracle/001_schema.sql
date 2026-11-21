-- Oracle owns master data and low-volume, strongly relational state:
-- the asset hierarchy, daily KPI snapshots and stored forecasts.
-- High-volume five-minute telemetry lives in SingleStore (see ../singlestore).

CREATE TABLE wells (
    well_id     VARCHAR2(32)   NOT NULL,
    name        VARCHAR2(120)  NOT NULL,
    basin       VARCHAR2(60)   NOT NULL,
    county      VARCHAR2(60),
    status      VARCHAR2(20)   DEFAULT 'producing' NOT NULL,
    spud_date   DATE,
    created_at  TIMESTAMP WITH TIME ZONE DEFAULT SYSTIMESTAMP NOT NULL,
    CONSTRAINT wells_pk PRIMARY KEY (well_id),
    CONSTRAINT wells_status_ck CHECK (status IN ('producing', 'shut-in', 'drilling', 'plugged'))
);

CREATE INDEX wells_basin_idx ON wells (basin);

CREATE TABLE well_kpi_daily (
    well_id              VARCHAR2(32) NOT NULL,
    production_day       DATE         NOT NULL,
    volume_bbl           NUMBER(14, 3) NOT NULL,
    avg_flow_bpd         NUMBER(12, 3) NOT NULL,
    coverage_ratio       NUMBER(5, 4)  NOT NULL,
    monthly_decline_pct  NUMBER(6, 2)  NOT NULL,
    computed_at          TIMESTAMP WITH TIME ZONE DEFAULT SYSTIMESTAMP NOT NULL,
    CONSTRAINT well_kpi_daily_pk PRIMARY KEY (well_id, production_day),
    CONSTRAINT well_kpi_daily_well_fk FOREIGN KEY (well_id) REFERENCES wells (well_id) ON DELETE CASCADE,
    CONSTRAINT well_kpi_daily_coverage_ck CHECK (coverage_ratio BETWEEN 0 AND 1)
);

CREATE TABLE well_forecast (
    well_id         VARCHAR2(32)  NOT NULL,
    forecast_date   DATE          NOT NULL,
    p10_bbl         NUMBER(12, 3) NOT NULL,
    p50_bbl         NUMBER(12, 3) NOT NULL,
    p90_bbl         NUMBER(12, 3) NOT NULL,
    model_version   VARCHAR2(40)  NOT NULL,
    qi_bpd          NUMBER(12, 3),
    di_per_day      NUMBER(10, 6),
    r_squared       NUMBER(6, 4),
    generated_at    TIMESTAMP WITH TIME ZONE DEFAULT SYSTIMESTAMP NOT NULL,
    CONSTRAINT well_forecast_pk PRIMARY KEY (well_id, forecast_date, model_version),
    CONSTRAINT well_forecast_well_fk FOREIGN KEY (well_id) REFERENCES wells (well_id) ON DELETE CASCADE,
    CONSTRAINT well_forecast_band_ck CHECK (p10_bbl <= p50_bbl AND p50_bbl <= p90_bbl)
);

-- Idempotent master-data upsert used by OracleWellRepository.
-- Bind variables are passed by name from Dapper (:wellId, :name, ...).
-- MERGE (rather than INSERT/UPDATE in application code) keeps a replayed load
-- from duplicating rows or racing another writer.

COMMENT ON TABLE wells IS 'Asset master data; system of record for the well hierarchy.';
COMMENT ON TABLE well_kpi_daily IS 'Daily KPI snapshot materialised from SingleStore windows.';
COMMENT ON TABLE well_forecast IS 'Stored Arps decline forecasts with an 80% prediction band.';
