-- SingleStore (MemSQL) owns the high-volume time series.
-- Columnstore + shard-by-well keeps a well's history co-located, and the sort
-- key makes "latest N windows for a well" a range scan instead of a table scan.

CREATE DATABASE IF NOT EXISTS wellhead;
USE wellhead;

CREATE TABLE IF NOT EXISTS production_windows (
    well_id          VARCHAR(32) NOT NULL,
    window_start     DATETIME(6) NOT NULL,
    window_end       DATETIME(6) NOT NULL,
    sample_count     INT NOT NULL,
    expected_samples INT NOT NULL,
    avg_flow_bbl     DOUBLE NOT NULL,
    total_volume_bbl DOUBLE NOT NULL,
    flow_cv          DOUBLE NOT NULL,
    avg_pressure_psi DOUBLE NOT NULL,
    max_pressure_psi DOUBLE NOT NULL,
    avg_temp_f       DOUBLE NOT NULL,
    SHARD KEY (well_id),
    SORT KEY (window_start),
    PRIMARY KEY (well_id, window_start)
);

CREATE TABLE IF NOT EXISTS anomaly_signals (
    well_id       VARCHAR(32) NOT NULL,
    window_start  DATETIME(6) NOT NULL,
    score         DOUBLE NOT NULL,
    decision      VARCHAR(10) NOT NULL,
    model_version VARCHAR(40) NOT NULL,
    reasons       JSON NOT NULL,
    created_at    TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    SHARD KEY (well_id),
    SORT KEY (window_start),
    PRIMARY KEY (well_id, window_start, model_version)
);

-- KPI queries hit this view instead of scanning raw windows repeatedly.
CREATE OR REPLACE VIEW well_daily_rollup AS
SELECT
    well_id,
    DATE(window_start)            AS production_day,
    SUM(total_volume_bbl)         AS volume_bbl,
    AVG(avg_flow_bbl)             AS avg_flow_bpd,
    MAX(max_pressure_psi)         AS peak_pressure_psi,
    COUNT(*)                      AS windows
FROM production_windows
GROUP BY well_id, DATE(window_start);
