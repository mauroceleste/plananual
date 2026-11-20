START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261120123609_ComunicacionPorLaEmpresa') THEN
    ALTER TABLE comunicaciones ADD por_la_empresa boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261120123609_ComunicacionPorLaEmpresa') THEN
    INSERT INTO __ef_migrations_history (migration_id, product_version)
    VALUES ('20261120123609_ComunicacionPorLaEmpresa', '10.0.15');
    END IF;
END $EF$;
COMMIT;

