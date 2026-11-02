# Pedido: base de datos PostgreSQL para el sistema Plan Anual de Inversiones

**Para:** quien administra el PostgreSQL de `10.1.117.117`
**De:** Subsecretaría de Hidrocarburos, sistema Plan Anual de Inversiones

## Qué necesitamos

Una base **nueva y separada**, con un usuario propio que **solo** pueda trabajar en ella. Es para probar el sistema
antes de salir a producción (la de producción se pedirá aparte).

Ejecutar, conectado como un usuario con permiso para crear roles y bases:

```sql
CREATE ROLE plananual_app LOGIN PASSWORD '<una clave larga y única>';
CREATE DATABASE plananual_pruebas OWNER plananual_app ENCODING 'UTF8';
REVOKE ALL ON DATABASE plananual_pruebas FROM PUBLIC;
```

## Qué nos tienen que pasar

Solo el nombre del usuario y la clave, **por un canal privado** (no por mail grupal ni chat público):

- Usuario: `plananual_app`
- Base: `plananual_pruebas`
- Servidor y puerto: `10.1.117.117:5432` (ya confirmado que responde). El sitio va a correr en ese mismo servidor y se conectará por `127.0.0.1`, como lo hace Trámites (el `pg_hba.conf` no tiene regla para la IP propia del servidor). Para cargar el esquema desde otra PC hace falta además una línea en `pg_hba.conf` con esa IP.

## Por qué un usuario propio

- No queremos usar las credenciales de otros sistemas (Legajo de Áreas u otros): un error de las pruebas podría afectar sus datos.
- El usuario nuevo **no es superusuario ni puede crear roles o bases**, y es dueño solo de las tablas de `plananual_pruebas`. No tiene permisos sobre los datos de otras bases. (Por defecto PostgreSQL deja que cualquier usuario *se conecte* a la base `postgres`; si la política de la Provincia lo exige, se puede cerrar con `REVOKE CONNECT ON DATABASE postgres FROM PUBLIC;`, pero eso afecta a otros usuarios y conviene que lo decida el administrador.)

## Qué va a hacer el sistema con esa base

- Crear sus tablas con el script `db/001_creacion_inicial.sql` (solo tablas nuevas: `presentaciones`, `plan_valores`, `anexos`, `ciclos`, `envios_mail`, `bloques_tipo_actividad`).
- Guardar ahí datos de prueba. Cuando termine la prueba se puede eliminar la base completa sin afectar nada más.

## Nota de red

El PostgreSQL responde en el puerto 5432 **sin SSL** (se verificó desde la red interna). Si la política lo requiere, avisar para ajustar la conexión.
