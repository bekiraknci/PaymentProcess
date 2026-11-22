-- MySQL 8.0+ equivalent of the SQL Server schema subset and dbo.sp_GetDailyPaymentReport.
-- Timestamps are stored in UTC; DECIMAL(18,2) + CHAR(3) mirror the SQL Server money columns.

CREATE TABLE IF NOT EXISTS payment_providers (
    id                   CHAR(36)      NOT NULL,
    code                 VARCHAR(32)   NOT NULL,
    name                 VARCHAR(128)  NOT NULL,
    base_url             VARCHAR(512)  NULL,
    is_active            TINYINT(1)    NOT NULL,
    supported_currencies VARCHAR(64)   NOT NULL,
    created_at           DATETIME(6)   NOT NULL,
    created_by           VARCHAR(256)  NULL,
    updated_at           DATETIME(6)   NULL,
    updated_by           VARCHAR(256)  NULL,
    PRIMARY KEY (id),
    UNIQUE KEY ux_payment_providers_code (code)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS payments (
    id                      CHAR(36)       NOT NULL,
    user_id                 CHAR(36)       NOT NULL,
    provider_id             CHAR(36)       NOT NULL,
    amount                  DECIMAL(18, 2) NOT NULL,
    currency                CHAR(3)        NOT NULL,
    status                  VARCHAR(20)    NOT NULL,
    attempt_count           INT            NOT NULL DEFAULT 0,
    next_attempt_at         DATETIME(6)    NULL,
    locked_until            DATETIME(6)    NULL,
    idempotency_key         VARCHAR(128)   NULL,
    request_hash            CHAR(64)       NULL,
    provider_transaction_id VARCHAR(128)   NULL,
    failure_reason          VARCHAR(512)   NULL,
    created_at              DATETIME(6)    NOT NULL,
    created_by              VARCHAR(256)   NULL,
    updated_at              DATETIME(6)    NULL,
    updated_by              VARCHAR(256)   NULL,
    PRIMARY KEY (id),
    -- MySQL unique indexes allow many NULLs, which matches the filtered SQL Server index.
    UNIQUE KEY ux_payments_user_id_idempotency_key (user_id, idempotency_key),
    KEY ix_payments_user_id_status_created_at (user_id, status, created_at),
    KEY ix_payments_user_id_created_at (user_id, created_at),
    KEY ix_payments_status_next_attempt_at (status, next_attempt_at),
    KEY ix_payments_status_created_at (status, created_at),
    -- Covering index for the report: InnoDB secondary indexes cannot INCLUDE columns, so status,
    -- currency and amount are trailing key columns.
    KEY ix_payments_provider_id_created_at (provider_id, created_at, status, currency, amount),
    KEY ix_payments_created_at (created_at),
    CONSTRAINT fk_payments_provider FOREIGN KEY (provider_id) REFERENCES payment_providers (id),
    CONSTRAINT ck_payments_amount_positive CHECK (amount > 0),
    CONSTRAINT ck_payments_status CHECK (status IN ('Pending', 'Processing', 'Completed', 'Failed')),
    CONSTRAINT ck_payments_attempt_count CHECK (attempt_count >= 0)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_ai_ci;

DROP PROCEDURE IF EXISTS sp_get_daily_payment_report;

DELIMITER $$

CREATE PROCEDURE sp_get_daily_payment_report(
    IN p_from DATE,
    IN p_to DATE,
    IN p_provider_id CHAR(36)
)
BEGIN
    -- Sargable range on created_at; DATE() is applied only in the projection/grouping.
    SELECT DATE(p.created_at)  AS `date`,
           pr.id               AS provider_id,
           pr.name             AS provider_name,
           p.status            AS status,
           p.currency          AS currency,
           COUNT(*)            AS payment_count,
           CAST(SUM(p.amount) AS DECIMAL(18, 2)) AS total_amount
    FROM payments AS p
    JOIN payment_providers AS pr ON pr.id = p.provider_id
    WHERE p.created_at >= p_from
      AND p.created_at < DATE_ADD(p_to, INTERVAL 1 DAY)
      AND (p_provider_id IS NULL OR p.provider_id = p_provider_id)
    GROUP BY DATE(p.created_at), pr.id, pr.name, p.status, p.currency
    ORDER BY `date`, provider_name, status, currency;
END$$

DELIMITER ;

-- Usage:
-- CALL sp_get_daily_payment_report('2026-11-01', '2026-11-30', NULL);
-- CALL sp_get_daily_payment_report('2026-11-01', '2026-11-30', '0196a000-0000-7000-8000-000000000001');
