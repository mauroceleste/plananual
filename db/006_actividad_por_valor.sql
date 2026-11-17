START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261117172017_ActividadPorValor') THEN
    ALTER TABLE plan_valores DROP CONSTRAINT pk_plan_valores;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261117172017_ActividadPorValor') THEN
    ALTER TABLE plan_valores ADD actividad integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261117172017_ActividadPorValor') THEN
    UPDATE plan_valores SET actividad = (SELECT tipo_plan FROM presentaciones WHERE presentaciones.id = plan_valores.presentacion_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261117172017_ActividadPorValor') THEN
    ALTER TABLE plan_valores ADD CONSTRAINT pk_plan_valores PRIMARY KEY (presentacion_id, actividad, anio, mes, campo);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261117172017_ActividadPorValor') THEN
    INSERT INTO __ef_migrations_history (migration_id, product_version)
    VALUES ('20261117172017_ActividadPorValor', '10.0.15');
    END IF;
END $EF$;
COMMIT;

