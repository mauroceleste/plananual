START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261113181236_PeriodosMensualesYRestricciones') THEN
    ALTER TABLE plan_valores DROP CONSTRAINT pk_plan_valores;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261113181236_PeriodosMensualesYRestricciones') THEN
    ALTER TABLE presentaciones ADD conflictos_superficiarios character varying(4000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261113181236_PeriodosMensualesYRestricciones') THEN
    ALTER TABLE presentaciones ADD equipos_contratos_firmes character varying(4000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261113181236_PeriodosMensualesYRestricciones') THEN
    ALTER TABLE presentaciones ADD equipos_costos character varying(4000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261113181236_PeriodosMensualesYRestricciones') THEN
    ALTER TABLE presentaciones ADD equipos_plazos character varying(4000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261113181236_PeriodosMensualesYRestricciones') THEN
    ALTER TABLE plan_valores ADD mes integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261113181236_PeriodosMensualesYRestricciones') THEN
    ALTER TABLE plan_valores ADD CONSTRAINT pk_plan_valores PRIMARY KEY (presentacion_id, anio, mes, campo);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261113181236_PeriodosMensualesYRestricciones') THEN
    INSERT INTO __ef_migrations_history (migration_id, product_version)
    VALUES ('20261113181236_PeriodosMensualesYRestricciones', '10.0.15');
    END IF;
END $EF$;
COMMIT;


