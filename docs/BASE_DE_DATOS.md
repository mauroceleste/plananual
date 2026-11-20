# Plan Anual de Inversiones — Base de datos

Esquema real de la base PostgreSQL del sistema, leído del servidor el 20/11/2026: tablas, columnas, claves, relaciones e índices, más consultas y tareas de mantenimiento. **No contiene contraseñas**: las conexiones están en el documento confidencial aparte.

## 1. Dónde está y cómo se llega

| Dato | Valor |
|---|---|
| Motor | PostgreSQL 16.15 (Windows) |
| Servidor | `10.1.117.117`, servicio `postgresql-x64-16` |
| Puerto | `5432` |
| Base | `plananual_pruebas` (≈39 MB) |
| Usuario de la aplicación | `plananual_app` (la contraseña está en el documento de conexiones confidencial) |
| Archivos de datos | `C:\Program Files\PostgreSQL\16\data` |
| Autenticación | `scram-sha-256`; el acceso desde otras PC se habilita línea por línea en `pg_hba.conf` |
| Codificación y fechas | Las fechas son `timestamp without time zone` (hora local); los valores numéricos del plan son `numeric(18,6)` |

La aplicación se conecta con la variable de entorno `ConnectionStrings__Default` del `web.config` del sitio. El esquema se crea y actualiza con los scripts de la carpeta `db/` del repositorio (`001` a `009`), que son **idempotentes** (se pueden correr más de una vez). El modelo también está en migraciones de Entity Framework.

## 2. Relaciones entre tablas

```
                       ciclos (convocatoria)
                            | 1
      +---------------+-------+---------------+------------------+
      | N             | N             | N                | N
 presentaciones  comunicaciones  observaciones_internas  historial_areas
      | 1
      +----------------+----------------+-----------------+
      | N              | N              | N               | N
 plan_valores       anexos         envios_mail   solicitudes_rectificacion

 bloques_tipo_actividad: independiente (se une por id_bloque)
```

| Relación | Al borrar el padre |
|---|---|
| `ciclos` 1—N `presentaciones`, `comunicaciones`, `observaciones_internas`, `historial_areas` | **RESTRICT**: no se puede borrar una convocatoria que tenga datos |
| `presentaciones` 1—N `plan_valores`, `anexos`, `envios_mail` | CASCADE: se borran junto con la presentación |
| `presentaciones` 1—N `solicitudes_rectificacion` | **RESTRICT** |

El **área** no es una tabla: sus datos (nombre, empresa, CUIT) vienen del servicio Legajos de Áreas; acá se identifica con `cuit` + `id_bloque`.

## 3. Las tablas

| Tabla | Para qué | Filas aprox. |
|---|---|---|
| `ciclos` | Convocatoria anual | 2 |
| `presentaciones` | Planilla de un área | 177 |
| `plan_valores` | Valores de la planilla | 164.500 |
| `anexos` | Archivos adjuntos | 206 |
| `envios_mail` | Registro de mails | 43 |
| `solicitudes_rectificacion` | Pedidos de rectificación | 1 |
| `observaciones_internas` | Observaciones internas | 60 |
| `historial_areas` | Auditoría de cambios | 479 |
| `comunicaciones` | Ida y vuelta con la empresa | 14 |
| `bloques_tipo_actividad` | Tipo de actividad por área | 128 |
| `__ef_migrations_history` | Control de migraciones de EF Core | 9 |

### 3.1. `ciclos` — Convocatoria anual

Una fila por convocatoria (2026, 2027…): año, nombre, `fecha_limite`, `fecha_prorroga`, `abierto`, `nota_gde`. La vigente es la de mayor año.

| Columna | Tipo | Nulo | Por defecto | |
|---|---|---|---|---|
| `id` | integer | no |  | **PK** |
| `anio` | integer | no |  |  |
| `nombre` | varchar(150) | no |  |  |
| `fecha_limite` | date | no |  |  |
| `fecha_prorroga` | date | sí |  |  |
| `abierto` | boolean | no |  |  |
| `nota_gde` | text | sí |  |  |

**Índices:**

- `ix_ciclos_anio` (único) sobre `anio`

### 3.2. `presentaciones` — Planilla de un área

Una fila por **área × convocatoria × versión**. Guarda empresa (CUIT, razón social), área, rango de años, estado (1 Borrador, 2 Enviada, 3 Reemplazada), quién la cargó/envió/asistió, comprobante, huella SHA-256, textos del bloque 4 y expediente GDE.

| Columna | Tipo | Nulo | Por defecto | |
|---|---|---|---|---|
| `id` | bigint | no |  | **PK** |
| `ciclo_id` | integer | no |  |  |
| `cuit` | varchar(11) | no |  |  |
| `razon_social` | varchar(150) | no |  |  |
| `id_bloque` | integer | no |  |  |
| `nombre_area` | varchar(150) | no |  |  |
| `nombre_bloque` | varchar(150) | no |  |  |
| `tipo_plan` | integer | no |  |  |
| `anio_desde` | integer | no |  |  |
| `anio_hasta` | integer | no |  |  |
| `version` | integer | no |  |  |
| `estado` | integer | no |  |  |
| `usuario_id` | integer | no |  |  |
| `usuario_nombre` | varchar(150) | no |  |  |
| `usuario_email` | text | no |  |  |
| `enviada_por` | varchar(150) | sí |  |  |
| `asistida_por` | varchar(150) | sí |  |  |
| `creada_en` | timestamp | no |  |  |
| `modificada_en` | timestamp | no |  |  |
| `enviada_en` | timestamp | sí |  |  |
| `numero_comprobante` | varchar(40) | sí |  |  |
| `huella_envio` | varchar(64) | sí |  |  |
| `observaciones` | text | sí |  |  |
| `conflictos_superficiarios` | varchar(4000) | sí |  |  |
| `equipos_contratos_firmes` | varchar(4000) | sí |  |  |
| `equipos_costos` | varchar(4000) | sí |  |  |
| `equipos_plazos` | varchar(4000) | sí |  |  |
| `expediente_gde` | varchar(80) | sí |  |  |

**Restricciones:**

- `fk_presentaciones_ciclos_ciclo_id`: FOREIGN KEY (ciclo_id) REFERENCES ciclos(id) ON DELETE RESTRICT

**Índices:**

- `ix_presentaciones_ciclo_id_cuit_id_bloque_version` (único) sobre `ciclo_id, cuit, id_bloque, version`
- `ix_presentaciones_numero_comprobante` (único) sobre `numero_comprobante`

### 3.3. `plan_valores` — Valores de la planilla

La tabla más grande. Una fila por **presentación × actividad × año × mes × campo**. `actividad`: 1 Convencional, 2 CENCH. `mes = 0` es valor anual; 1–12 solo en el primer año. Los campos calculados no se guardan: se recalculan.

| Columna | Tipo | Nulo | Por defecto | |
|---|---|---|---|---|
| `presentacion_id` | bigint | no |  | **PK** |
| `anio` | integer | no |  | **PK** |
| `campo` | varchar(40) | no |  | **PK** |
| `valor` | numeric(18,6) | sí |  |  |
| `mes` | integer | no | `0` | **PK** |
| `actividad` | integer | no | `0` | **PK** |

**Restricciones:**

- `fk_plan_valores_presentaciones_presentacion_id`: FOREIGN KEY (presentacion_id) REFERENCES presentaciones(id) ON DELETE CASCADE

**Índices:**

- `ix_plan_valores_campo` (normal) sobre `campo`

### 3.4. `anexos` — Archivos adjuntos

Metadatos de cada adjunto de una presentación (el archivo está en `C:\ProgramData\PlanAnual\anexos`): tipo, nombre, ruta, tamaño y SHA-256.

| Columna | Tipo | Nulo | Por defecto | |
|---|---|---|---|---|
| `id` | bigint | no |  | **PK** |
| `presentacion_id` | bigint | no |  |  |
| `tipo` | varchar(60) | no |  |  |
| `nombre_archivo` | varchar(255) | no |  |  |
| `ruta_almacenada` | varchar(500) | no |  |  |
| `tamano` | bigint | no |  |  |
| `sha256` | varchar(64) | no |  |  |
| `subido_en` | timestamp | no |  |  |

**Restricciones:**

- `fk_anexos_presentaciones_presentacion_id`: FOREIGN KEY (presentacion_id) REFERENCES presentaciones(id) ON DELETE CASCADE

**Índices:**

- `ix_anexos_presentacion_id` (normal) sobre `presentacion_id`

### 3.5. `envios_mail` — Registro de mails

Un renglón por mail de una presentación: destinatarios, asunto, si salió y el error si falló.

| Columna | Tipo | Nulo | Por defecto | |
|---|---|---|---|---|
| `id` | bigint | no |  | **PK** |
| `presentacion_id` | bigint | no |  |  |
| `destinatarios` | text | no |  |  |
| `asunto` | varchar(250) | no |  |  |
| `enviado` | boolean | no |  |  |
| `error` | text | sí |  |  |
| `fecha` | timestamp | no |  |  |

**Restricciones:**

- `fk_envios_mail_presentaciones_presentacion_id`: FOREIGN KEY (presentacion_id) REFERENCES presentaciones(id) ON DELETE CASCADE

**Índices:**

- `ix_envios_mail_presentacion_id` (normal) sobre `presentacion_id`

### 3.6. `solicitudes_rectificacion` — Pedidos de rectificación

La empresa pide corregir un área enviada. Estado 1 Pendiente, 2 Habilitada, 3 Rechazada, 4 Usada. `habilitada_hasta` = 7 días desde que se habilita.

| Columna | Tipo | Nulo | Por defecto | |
|---|---|---|---|---|
| `id` | bigint | no |  | **PK** |
| `ciclo_id` | integer | no |  |  |
| `cuit` | varchar(11) | no |  |  |
| `razon_social` | varchar(150) | no |  |  |
| `id_bloque` | integer | no |  |  |
| `nombre_area` | varchar(150) | no |  |  |
| `presentacion_id` | bigint | no |  |  |
| `motivo` | varchar(1000) | no |  |  |
| `solicitada_por` | varchar(150) | no |  |  |
| `solicitada_email` | varchar(150) | no |  |  |
| `solicitada_en` | timestamp | no |  |  |
| `estado` | integer | no |  |  |
| `resuelta_por` | varchar(150) | sí |  |  |
| `resuelta_en` | timestamp | sí |  |  |
| `habilitada_hasta` | timestamp | sí |  |  |

**Restricciones:**

- `fk_solicitudes_rectificacion_presentaciones_presentacion_id`: FOREIGN KEY (presentacion_id) REFERENCES presentaciones(id) ON DELETE RESTRICT

**Índices:**

- `ix_solicitudes_rectificacion_ciclo_id_cuit_id_bloque` (normal) sobre `ciclo_id, cuit, id_bloque`
- `ix_solicitudes_rectificacion_estado` (normal) sobre `estado`
- `ix_solicitudes_rectificacion_presentacion_id` (normal) sobre `presentacion_id`

### 3.7. `observaciones_internas` — Observaciones internas

Notas de la Subsecretaría sobre un área (no las ve la empresa). Se ligan a convocatoria + CUIT + bloque, no a una versión.

| Columna | Tipo | Nulo | Por defecto | |
|---|---|---|---|---|
| `id` | bigint | no |  | **PK** |
| `ciclo_id` | integer | no |  |  |
| `cuit` | varchar(11) | no |  |  |
| `id_bloque` | integer | no |  |  |
| `texto` | varchar(2000) | no |  |  |
| `usuario_id` | integer | no |  |  |
| `usuario_nombre` | varchar(150) | no |  |  |
| `creada_en` | timestamp | no |  |  |

**Restricciones:**

- `fk_observaciones_internas_ciclos_ciclo_id`: FOREIGN KEY (ciclo_id) REFERENCES ciclos(id) ON DELETE RESTRICT

**Índices:**

- `ix_observaciones_internas_ciclo_id_cuit_id_bloque` (normal) sobre `ciclo_id, cuit, id_bloque`

### 3.8. `historial_areas` — Auditoría de cambios

Quién cambió qué y cuándo en un área: campo, valor anterior y nuevo, usuario, versión. Las ediciones de celdas seguidas se agrupan en 30 minutos.

| Columna | Tipo | Nulo | Por defecto | |
|---|---|---|---|---|
| `id` | bigint | no |  | **PK** |
| `ciclo_id` | integer | no |  |  |
| `cuit` | varchar(11) | no |  |  |
| `id_bloque` | integer | no |  |  |
| `fecha` | timestamp | no |  |  |
| `usuario` | varchar(150) | no |  |  |
| `campo` | varchar(200) | no |  |  |
| `anterior` | varchar(1000) | sí |  |  |
| `nuevo` | varchar(1000) | sí |  |  |
| `version` | integer | sí |  |  |

**Restricciones:**

- `fk_historial_areas_ciclos_ciclo_id`: FOREIGN KEY (ciclo_id) REFERENCES ciclos(id) ON DELETE RESTRICT

**Índices:**

- `ix_historial_areas_ciclo_id_cuit_id_bloque_fecha` (normal) sobre `ciclo_id, cuit, id_bloque, fecha`

### 3.9. `comunicaciones` — Ida y vuelta con la empresa

Notas, respuestas, prórrogas y decisiones. `tipo`: 1 Nota enviada, 2 Respuesta de la empresa, 3 Prórroga solicitada, 4 Prórroga otorgada, 5 Observado, 6 Rechazado, 7 Aprobado. `responde_a` enlaza una respuesta con su nota o pedido. `id_bloque` nulo = de toda la empresa.

| Columna | Tipo | Nulo | Por defecto | |
|---|---|---|---|---|
| `id` | bigint | no |  | **PK** |
| `ciclo_id` | integer | no |  |  |
| `cuit` | varchar(11) | no |  |  |
| `razon_social` | varchar(150) | no |  |  |
| `id_bloque` | integer | sí |  |  |
| `nombre_area` | varchar(150) | sí |  |  |
| `fecha` | timestamp | no |  |  |
| `tipo` | integer | no |  |  |
| `asunto` | varchar(200) | no |  |  |
| `detalle` | varchar(4000) | sí |  |  |
| `numero_gde` | varchar(80) | sí |  |  |
| `plazo_hasta` | date | sí |  |  |
| `responde_a` | bigint | sí |  |  |
| `visible_para_empresa` | boolean | no |  |  |
| `usuario_id` | integer | no |  |  |
| `usuario` | varchar(150) | no |  |  |
| `adjunto_nombre` | varchar(250) | sí |  |  |
| `adjunto_ruta` | varchar(300) | sí |  |  |
| `adjunto_tamano` | bigint | sí |  |  |
| `adjunto_sha256` | varchar(64) | sí |  |  |
| `mail_enviado` | boolean | no |  |  |
| `mail_destinatarios` | varchar(1000) | sí |  |  |
| `mail_error` | varchar(500) | sí |  |  |
| `por_la_empresa` | boolean | no | `false` |  |

**Restricciones:**

- `fk_comunicaciones_ciclos_ciclo_id`: FOREIGN KEY (ciclo_id) REFERENCES ciclos(id) ON DELETE RESTRICT

**Índices:**

- `ix_comunicaciones_ciclo_id_cuit_fecha` (normal) sobre `ciclo_id, cuit, fecha`

### 3.10. `bloques_tipo_actividad` — Tipo de actividad por área

Tipo (1 Convencional, 2 CENCH) fijado a mano por Administración mientras Legajos no lo informe. Clave: `id_bloque`.

| Columna | Tipo | Nulo | Por defecto | |
|---|---|---|---|---|
| `id_bloque` | integer | no |  | **PK** |
| `nombre_area` | varchar(150) | no |  |  |
| `nombre_bloque` | varchar(150) | no |  |  |
| `tipo_plan` | integer | no |  |  |

### 3.11. `__ef_migrations_history` — Control de migraciones de EF Core

La mantiene Entity Framework: lista las migraciones aplicadas. No tocar a mano.

| Columna | Tipo | Nulo | Por defecto | |
|---|---|---|---|---|
| `migration_id` | varchar(150) | no |  | **PK** |
| `product_version` | varchar(32) | no |  |  |

## 4. Consultas útiles

```sql
-- Áreas enviadas por empresa en la convocatoria 2027
select cuit, razon_social, count(*) as enviadas
from presentaciones
where ciclo_id = (select id from ciclos where anio = 2027) and estado = 2
group by cuit, razon_social order by 3 desc;

-- Última versión de cada área de una empresa
select distinct on (id_bloque) id_bloque, nombre_area, version, estado, enviada_en
from presentaciones
where cuit = '30695542476' and ciclo_id = 2
order by id_bloque, version desc;

-- Valores de una presentación (Convencional = 1, CENCH = 2)
select anio, mes, campo, valor from plan_valores
where presentacion_id = 123 and actividad = 1 order by anio, mes, campo;

-- Notas de la Subsecretaría sin respuesta
select c.cuit, c.razon_social, c.asunto, c.plazo_hasta
from comunicaciones c
where c.tipo = 1 and c.plazo_hasta is not null
  and not exists (select 1 from comunicaciones r where r.responde_a = c.id);

-- Tamaño de las tablas
select relname, pg_size_pretty(pg_total_relation_size(relid))
from pg_stat_user_tables order by pg_total_relation_size(relid) desc;
```

## 5. Mantenimiento

### Copia de seguridad (recomendada: hoy no hay una programada)

```powershell
# Respaldo completo de la base (formato comprimido). Pide la contraseña de plananual_app.
pg_dump -h 10.1.117.117 -U plananual_app -d plananual_pruebas -F c -f plananual_AAAAMMDD.dump

# Restaurar en una base vacía
pg_restore -h <servidor> -U <usuario> -d <base_nueva> --no-owner plananual_AAAAMMDD.dump
```

Junto con la base hay que respaldar la carpeta de anexos `C:\ProgramData\PlanAnual` (archivos adjuntos y claves de la cookie).

### Cambios de esquema

1. Cambiar la entidad en `PlanAnual.Domain` y su mapeo en `PlanAnualDbContext`.
2. Generar la migración y el script idempotente en `db/` (`dotnet ef migrations add` y `migrations script --idempotent`).
3. Quitar la marca BOM del script (si no, `psql` en Windows muestra caracteres raros).
4. Aplicar el script en el servidor **antes** de publicar la versión nueva: `psql -h <servidor> -U <usuario> -d plananual_pruebas -f db/00N_nombre.sql`.

Si se olvida la migración, falla la prueba automática `El_modelo_no_tiene_cambios_sin_migracion`.

### Cosas a tener en cuenta

- La base se llama `plananual_pruebas` y contiene **datos de prueba** (YPF y Pan American). Para producción real conviene crear una base limpia y aplicar `db/001` a `009`.
- No se borran convocatorias con datos: la base lo impide (`RESTRICT`).
- Para ver o modificar datos conviene usar la aplicación; los cambios directos en la base no quedan en `historial_areas`.
- Se sacó el 20/11/2026 la tabla de respaldo `plan_valores_respaldo_20261117` (ya no existe).
