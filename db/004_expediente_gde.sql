START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261116180727_ExpedienteGde') THEN
    ALTER TABLE presentaciones ADD expediente_gde character varying(80);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261116180727_ExpedienteGde') THEN
    INSERT INTO __ef_migrations_history (migration_id, product_version)
    VALUES ('20261116180727_ExpedienteGde', '10.0.15');
    END IF;
END $EF$;
COMMIT;

