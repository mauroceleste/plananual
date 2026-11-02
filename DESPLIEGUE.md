# Plan Anual de Inversiones: despliegue y operación

Sistema ASP.NET Core 10 MVC con PostgreSQL. Las empresas operadoras cargan el Plan de Desarrollo (CENCH o Convencionales) por área;
la Subsecretaría ve el seguimiento y el tablero. Esta guía es para quien lo instala y lo mantiene.

## 1. Qué hace falta

| Pieza | Detalle |
|---|---|
| Servidor | Windows con IIS y el **ASP.NET Core Hosting Bundle 10** (el mismo que usa Trámites en el 117) |
| Base de datos | PostgreSQL (una base nueva, por ejemplo `plananual`, y un usuario con permisos solo sobre ella) |
| Red | El servidor tiene que llegar a `10.1.117.56` (UsersAccess) y `10.1.117.66` (Legajos de Áreas), ambos por HTTP interno, y al servidor SMTP |
| Acceso público | Detrás del proxy `hidrocarburos.energianeuquen.gob.ar/plananual` |

## 2. Configuración

Los valores van en `appsettings.json` del sitio o, mejor, en **variables de entorno** (doble guion bajo para los niveles: `ConnectionStrings__Default`).
**Las contraseñas nunca se guardan en el repositorio.**

| Clave | Para qué | Valor en producción |
|---|---|---|
| `ConnectionStrings:Default` | Conexión a PostgreSQL | `Host=...;Database=plananual;Username=...;Password=...` |
| `Database:Provider` | Motor | `Postgres` (no tocar) |
| `Database:AplicarMigraciones` | Si la app crea el esquema al iniciar | `false` (lo aplica la persona que administra la base, ver 3) |
| `Acceso:AccesoDeDesarrollo` | Ingreso de prueba sin UsersAccess | **`false`**: con `true` el sistema no arranca en producción |
| `Acceso:IdSistema` / `ModuloOperacion` / `ModuloAdministracion` | Sistema en UsersAccess | `39` / `119` / `118` |
| `Acceso:VigenciaSesionHoras` | Antigüedad máxima de una sesión del portal (UsersAccess no las vence) | `720` (30 días; las sesiones reales del portal duran días: una de administrador tenía 9 días y era la vigente) |
| `Acceso:UsersAccessUrl` | Servicio de usuarios | `http://10.1.117.56/WSUserAccessV2/WebServiceAcces.asmx` |
| `Acceso:PortalUrl` | A dónde vuelve quien no tiene sesión | `https://hidrocarburos.energianeuquen.gob.ar/usersaccess/` |
| `Legajos:Url` | Servicio de áreas y operadoras | `http://10.1.117.66/WSLegajosCustom/LegajosWS.asmx` |
| `PathBase` | Prefijo detrás del proxy | `/plananual` |
| `PublicOrigin` | Origen público para los links de los mails | `https://hidrocarburos.energianeuquen.gob.ar` |
| `DataProtection:Carpeta` | Carpeta de las claves de la cookie de sesión | Una carpeta fija con escritura para el sitio (si no, cada reinicio cierra todas las sesiones) |
| `Anexos:Carpeta` / `Anexos:MaximoMB` | Dónde se guardan los anexos y su tope | Carpeta fuera del sitio y **con copia de seguridad** / `25` |
| `Mail:*` | Ver sección 5 | |

Los valores de producción que ya se conocen están en **`src/PlanAnual.Web/appsettings.Production.json`** (se usa solo cuando el sitio corre en producción, así el desarrollo local no manda mails ni cambia sus rutas): prefijo `/plananual`, origen público y correo.

Si la configuración es peligrosa, **el sistema no arranca** y dice por qué (ingreso de desarrollo activo, cadena de conexión de ejemplo, SQLite, mail habilitado sin servidor).

## 3. Base de datos

El esquema está en migraciones de Entity Framework (`src/PlanAnual.Infrastructure/Migrations`) y como script SQL listo para revisar:
**`db/001_creacion_inicial.sql`**. Es idempotente: se puede correr más de una vez.

```bash
psql -h <servidor> -U <usuario> -d plananual -f db/001_creacion_inicial.sql
```

La convocatoria (año, plazo y prórroga) se carga después desde **Administración → Convocatoria**.

### Cuando cambie el modelo

1. Cambiar la entidad o la configuración.
2. Generar la migración y el script nuevo (desde la carpeta `PlanAnual`):

```bash
dotnet dotnet-ef migrations add NombreDelCambio --project src/PlanAnual.Infrastructure --startup-project src/PlanAnual.Web --output-dir Migrations
dotnet dotnet-ef migrations script --idempotent --project src/PlanAnual.Infrastructure --startup-project src/PlanAnual.Web -o db/002_nombre_del_cambio.sql
```

**Ojo:** `dotnet ef migrations script` escribe el archivo con una marca BOM al principio, que `psql` en Windows lee como caracteres raros (`ï»¿CREATE`). Quitala antes de entregarlo (en PowerShell: `(Get-Content db_x.sql -Raw) | Set-Content db_x.sql -Encoding utf8NoBOM` en PowerShell 7, o guardalo "UTF-8 sin BOM" desde el editor).

Si te olvidás del paso 2, falla la prueba `El_modelo_no_tiene_cambios_sin_migracion`. `dotnet-ef` viene como herramienta local del proyecto (`dotnet tool restore`).

Las fechas se guardan como `timestamp without time zone` (hora local): Npgsql rechaza hora local en `timestamptz`. Hay una prueba que lo cuida.

## 4. Permisos en UsersAccess

| Módulo | Perfil | Qué ve y hace |
|---|---|---|
| `119` Operación | Empresa | Solo las empresas que tiene asignadas (`ExchangeDataCompanyGetByIdUsuario`). Carga, adjunta, envía y rectifica sus áreas |
| `118` Administración | Administrador | Todas las empresas. Puede **asistir** a una empresa en la carga (queda registrado a su nombre), ver el tablero y administrar tipo de actividad, convocatoria y correo |

- Si una persona tiene los dos módulos, **gana Operación** (ve solo su empresa), igual que en Registro de Empresas.
- Una persona con Operación y **sin empresa asignada no ingresa** (a propósito).
- **UsersAccess no vence las sesiones**: los `idSesion` son números chicos y consecutivos, y una sesión de 2018 sigue respondiendo. Por eso este sistema rechaza las sesiones **cerradas** y las abiertas hace más de `Acceso:VigenciaSesionHoras` (30 días por defecto). Aun así, un `idSesion` *vigente* de otra persona podría usarse si alguien lo adivina: conviene plantearlo a quien administra el portal.
- Todavía **no existe** un módulo de "solo consulta" para la Subsecretaría. Si se necesita, hay que darlo de alta en UsersAccess y mapearlo en `AutenticadorUsersAccess`.

## 5. Correo

| Clave | Valor |
|---|---|
| `Mail:Habilitado` | `true` para enviar de verdad |
| `Mail:Host` / `Mail:Puerto` / `Mail:Ssl` | Servidor SMTP |
| `Mail:Usuario` / `Mail:Clave` | Solo si el servidor pide autenticación (la clave va en variable de entorno) |
| `Mail:Desde` | Remitente |
| `Mail:Subsecretaria` | Casillas que reciben copia de cada presentación (separadas por `;`) |
| `Mail:ModoPrueba` / `Mail:ListaPrueba` | Con `true`, **todo** se desvía a la lista de prueba con el asunto `[PRUEBA]` |

**Estado actual (`appsettings.Production.json`):** servidor `smtp8.neuquen.gov.ar:25` sin autenticación, remitente `mceleste@neuquen.gov.ar`, **modo de prueba activo** con `mauroceleste@gmail.com` como única destinataria, y `Mail:Subsecretaria` **vacío** (pendiente: definir a quién va cada copia). Mientras el modo de prueba esté activo, ningún mail sale a las empresas. El remitente es una casilla personal: conviene cambiarlo por una institucional antes de salir.

Salida a producción: probar con `ModoPrueba=true`, y recién cuando esté todo bien pasarlo a `false`.
**Administración → Correo** muestra cómo quedó configurado (nunca la contraseña) y manda un mail de prueba.

Un mail que falla **no** impide el envío: la presentación queda registrada y el fallo se anota (se ve en el comprobante).

## 6. Publicar

```bash
dotnet publish src/PlanAnual.Web -c Release -o publish
```

1. Poner `app_offline.htm` en la carpeta del sitio.
2. Copiar el contenido de `publish`.
3. Borrar `app_offline.htm`.

El grupo de aplicaciones de IIS necesita **escritura** en la carpeta de anexos y en la de claves (`DataProtection:Carpeta`).

## 7. Antes de abrirlo a las empresas

- [ ] Esquema aplicado en PostgreSQL y convocatoria cargada.
- [ ] `Acceso:AccesoDeDesarrollo` en `false` (el sistema ya lo exige).
- [ ] Probar el ingreso real con un `idSesion` válido: un usuario de empresa y uno de administración.
- [ ] **Administración → Tipo de actividad:** definir Convencional o CENCH para cada área. Mientras Legajos no informe el campo `tipo_actividad` (ver `PEDIDO_WS_TIPO_ACTIVIDAD.md`), las empresas no pueden comenzar un área sin tipo. Cuando el servicio lo agregue, el sistema lo lee solo y lo fijado a mano sigue mandando.
- [ ] Mail: prueba de envío, `Mail:Subsecretaria` completo y `ModoPrueba=false`.
- [ ] `PublicOrigin` y `PathBase` correctos (el link del comprobante en el mail tiene que abrir).
- [ ] Copia de seguridad de la base **y** de la carpeta de anexos.

## 8. Qué se probó y qué falta

**Probado sobre un PostgreSQL 16.9 real** (una instancia temporal en esta PC de desarrollo, ya eliminada):

- `db/001_creacion_inicial.sql` se aplica sin errores y **se puede correr dos veces** (es idempotente).
- La migración de la aplicación (`Database:AplicarMigraciones=true`) produce un esquema **idéntico** al del script (se comparó con `pg_dump --schema-only`: 129 líneas iguales).
- Recorrido completo en **modo producción** y con una sesión **real** de UsersAccess: ingreso como Administrador, convocatoria, tipo de actividad, asistencia a una empresa, carga con decimales (`numeric(18,6)` exactos), anexo, envío con comprobante, Excel y tablero. Se hizo sobre la base creada con el script y sobre la creada con la migración.
- Las fechas con hora local entran sin error en `timestamp without time zone`, y borrar una convocatoria con presentaciones es rechazado por la base (`ON DELETE RESTRICT`).
- El mail sale por `smtp8.neuquen.gov.ar:25` y llega (en modo de prueba, a la casilla de prueba).

**Falta**, y solo se puede hacer en los ambientes reales:

- Crear la base y el usuario en el PostgreSQL de la Provincia (`10.1.117.117`) y habilitar la conexión en `pg_hba.conf`. El pedido listo para reenviar está en `db/PEDIDO_BASE_POSTGRESQL.md`.
- Repetir allí el recorrido de prueba.
- Cargar las casillas reales de `Mail:Subsecretaria` y apagar `Mail:ModoPrueba`.
- Que el servicio de Legajos informe `tipo_actividad` (ver `PEDIDO_WS_TIPO_ACTIVIDAD.md`).

## 9. Desarrollo local

```bash
cd src/PlanAnual.Web
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile --urls http://localhost:5213
```

Usa SQLite (`App_Data/plananual-dev.db`, se crea sola) y una pantalla de ingreso de prueba. Las pruebas: `dotnet test` (desde `PlanAnual`).
