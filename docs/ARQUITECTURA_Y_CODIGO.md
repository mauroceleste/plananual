# Plan Anual de Inversiones — Arquitectura, código y operación

> Este documento **no contiene contraseñas ni claves**. Las credenciales y conexiones están en `docs/CONEXIONES_Y_CREDENCIALES.md.local`
> (archivo ignorado por git: vive solo en la PC de desarrollo).

## 1. Vista general

Aplicación web **ASP.NET Core 10 MVC** (Razor), con **EF Core 10 + PostgreSQL 16**, hospedada en **IIS** (in-process) en el servidor `10.1.117.117`.
No tiene usuarios propios: la identidad viene del **Portal / UsersAccess**; los datos de áreas y empresas, de **Legajos de Áreas**; el seguimiento de
expedientes, de la **API de GDE**.

```
 Navegador ──HTTPS──► Proxy hidrocarburos.energianeuquen.gob.ar/plananual
                          │
                          ▼
                IIS 10.1.117.117  (sitio "plananual", pool "PlanAnual", in-process)
                  PlanAnual.Web  ──► PlanAnual.Infrastructure ──► PlanAnual.Domain
                    │   │   │              │  │  │  │
                    │   │   │              │  │  │  └─► SMTP smtp8.neuquen.gov.ar:25   (mails)
                    │   │   │              │  │  └────► GDE API   http://10.1.117.69:8090   (REST, X-Api-Key)
                    │   │   │              │  └───────► Legajos   http://10.1.117.66/WSLegajosCustom (SOAP)
                    │   │   │              └──────────► UsersAccess http://10.1.117.56/WSUserAccessV2 (SOAP)
                    │   │   └── Anexos:   C:\ProgramData\PlanAnual\anexos
                    │   └────── Claves de cookie: C:\ProgramData\PlanAnual\keys
                    └────────── PostgreSQL 16 (mismo servidor, 127.0.0.1:5432, base plananual_pruebas)
```

### Capas (solución `PlanAnual.sln`)

| Proyecto | Qué contiene | Depende de |
|---|---|---|
| `PlanAnual.Domain` | Entidades, **catálogo de campos**, cálculos, validación, agregación y indicadores del tablero. **Sin acceso a datos ni a la red** (puro, fácil de probar) | — |
| `PlanAnual.Infrastructure` | `DbContext` (EF Core), **servicios de negocio**, clientes externos (UsersAccess, Legajos, GDE), mail, Excel, migraciones, inyección de dependencias | Domain |
| `PlanAnual.Web` | Controladores MVC, vistas Razor, JS/CSS, `Program.cs`, autenticación por cookie | Infrastructure, Domain |
| `tests/PlanAnual.Tests` | xUnit: **264 pruebas** (SQLite en memoria para datos; clientes externos simulados) | todos |

Paquetes principales: Npgsql.EntityFrameworkCore.PostgreSQL, EF Core SQLite (solo pruebas/desarrollo), **ClosedXML 0.104.2** (generar/leer Excel),
Bootstrap 5.3 (UI), Chart en JS propio (`charts.js`).

## 2. Flujo de una solicitud

1. **Cookie** `plananual.auth` (HttpOnly, SameSite=Lax, 30 min deslizantes). Claims: id, nombre, email, `cuits`, rol.
2. `Sesion.ToSesion()` arma un `UsuarioSesion(Id, Nombre, Email, Cuits, Rol)`; los **servicios reciben siempre ese usuario** y aplican el permiso
   (`PuedeVer`, `PuedeEditar`, `EsAsistencia`); los controladores solo mapean y devuelven vistas.
3. **Políticas**: `Subsecretaria` = rol Subsecretaria o Administrador; `Administrador` = solo Administrador. `[Authorize]` solo = cualquier rol (la empresa, además, solo ve sus CUIT).
4. Antiforgery automático en todos los POST (`AutoValidateAntiforgeryToken`); los `fetch` mandan el header `RequestVerificationToken`.
5. `PathBase=/plananual` detrás del proxy; `PublicOrigin` arma los links de los mails.

### Autenticación (`AccountController` + `AutenticadorUsersAccess`)
`GET /login?idSesion=N` → `AutenticadorUsersAccess.IngresarAsync(idSesion)`:
1. `UsersAccessClient.UsuarioPorSesionAsync` → datos del usuario y fechas de la sesión (rechaza sesiones **cerradas** o **abiertas hace más de `Acceso:VigenciaSesionHoras`**, hoy 2160 h = 90 días).
2. `PermisosAsync(idSesion, idSistema=39)` → módulos **119 (Operación → Empresa)** y **118 (Administración → Administrador)**; si tiene ambos, gana Operación.
3. Empresa: `CuitsDeEmpresasAsync(idUsuario)` → CUIT asignados (sin ninguno no ingresa).
4. Se emite la cookie. Sin sesión, se redirige al portal (`Acceso:PortalUrl`).
En desarrollo existe `POST /login/desarrollo` (solo si `Acceso:AccesoDeDesarrollo=true`; en producción el sistema **no arranca** con eso activo).

## 3. Modelo de datos

Entidades en `PlanAnual.Domain/Entities/Entidades.cs`; mapeo (nombres snake_case, tipos, índices) en `Infrastructure/Data/PlanAnualDbContext.cs`.

| Tabla | Entidad | Para qué |
|---|---|---|
| `ciclos` | `Ciclo` | Convocatoria anual: año, nombre, `fecha_limite`, `fecha_prorroga`, `abierto`, `nota_gde`. `LimiteVigente = prórroga ?? límite` |
| `bloques_tipo_actividad` | `BloqueTipoActividad` | Tipo de actividad fijado a mano por área (provisorio hasta que Legajos lo informe) |
| `presentaciones` | `Presentacion` | Una planilla por **área × ciclo × versión**: empresa, área, rango de años, estado (Borrador/Enviada/Reemplazada), comprobante, huella SHA-256, textos del bloque 4, expediente GDE, quién la cargó/envió/asistió |
| `plan_valores` | `PlanValor` | Valores: `(presentacion, actividad, año, mes, campo) → numeric(18,6)`. `mes=0` es anual; 1–12 solo en el primer año. `actividad` distingue Convencional (1) y CENCH (2) |
| `anexos` | `Anexo` | Archivos adjuntos (tipo, nombre, ruta, tamaño, SHA-256) |
| `solicitudes_rectificacion` | `SolicitudRectificacion` | Pedido de rectificar un área enviada; estados Pendiente/Habilitada/Rechazada/Usada; habilitación de 7 días |
| `observaciones_internas` | `ObservacionInterna` | Notas internas de la Subsecretaría (por área y ciclo, no por versión) |
| `historial_areas` | `HistorialArea` | Auditoría de cambios por área (campo, valor anterior/nuevo, usuario, versión) |
| `comunicaciones` | `Comunicacion` | Ida y vuelta con la empresa: tipo, asunto, detalle, número GDE, plazo, `responde_a`, visibilidad, `por_la_empresa`, adjunto, datos del mail |
| `envios_mail` | `EnvioMail` | Registro de mails de presentaciones (enviado/error) |
| `__ef_migrations_history` | — | Control de migraciones EF |

Reglas del modelo: **una presentación por área** con los dos planes dentro (`PlanValor.Actividad`); `Presentacion.ParaTipo(tipo)` devuelve una **copia de solo lectura**
con un solo plan (sirve para calcular/validar/mostrar cada plan con la misma lógica; nunca se guarda). `TiposCargados()` indica qué planes tienen valores (un plan en 0 también es una declaración).
Las fechas son `timestamp without time zone` (hora local): Npgsql no acepta hora local en `timestamptz`; hay una prueba que lo cuida.

### Esquema SQL
Migraciones EF en `Infrastructure/Migrations` **y** scripts idempotentes revisables en `db/` (se aplican con `psql`, ver sección 7):
`001_creacion_inicial`, `002_solicitudes_rectificacion`, `003_periodos_mensuales_y_restricciones`, `004_expediente_gde`, `005_observaciones_internas`,
`006_actividad_por_valor`, `007_historial_areas`, `008_comunicaciones`, `009_comunicacion_por_la_empresa`.
Si se cambia el modelo sin migración, falla la prueba `El_modelo_no_tiene_cambios_sin_migracion`.

## 4. Dominio (`PlanAnual.Domain/Catalogo`) — la lógica de planilla

El **catálogo** define las columnas; formularios, validación, Excel y tablero se generan de él (agregar un campo no requiere migración).

| Archivo | Responsabilidad | Métodos principales |
|---|---|---|
| `CatalogoCampos.cs` | `CampoDef` (código, grupo, nombre, unidad, planes que aplica, `Suma`/`Resta` para calculados, decimales, máximo, agregación) y el listado `Todos` | `Para(plan)`, `Buscar(plan, codigo)`, `DescripcionUnidad(unidad)`; `Agregacion` (Suma, Promedio, Máximo, Último) |
| `Calculadora.cs` | Calcula los campos derivados (totales, flujos) | `CalcularAnio(plan, ingresados)`, `CalcularPeriodos(plan, valores)` (mes a mes), `CalcularPlan(plan, valores)` (año a año, agregando los meses), `AgregarMeses(campo, porMes)`, `Clave(anio, mes)` |
| `Validador.cs` | Errores y requisitos de una presentación (por plan, anexos obligatorios, textos) | `Validar(presentacion, anexos)` → `List<Observacion>` (`EsError`, `EsRequisito`); `NombreActividad(tipo)`; `TextosBloque4` |
| `ReglasDeDatos.cs` | Plausibilidad de un dato (máximos, enteros) | `Motivo(campo, valor)`, `Descripcion(campo)` |
| `AnexosRequeridos.cs` | Tipos de anexo (fundamento del flujo, flujograma, paradas de planta, presentación del plan, otros) | `Buscar(codigo)`, `AnexoTipo.Titulo(anio)` |
| `Agregador.cs` | Suma de presentaciones enviadas para el tablero (inversión, pozos, producción, por empresa) | `Calcular(enviadas, anioFirme, cantidadAnios)` → `DatosTablero` |
| `Indicadores.cs` | Indicadores del tablero: mix, escenarios de precio, alertas, atípicos, cambios entre versiones, mapa | `Calcular(...)` → `IndicadoresTablero` |

## 5. Servicios (`PlanAnual.Infrastructure/Servicios`)

### `PresentacionService` (núcleo, ~720 líneas)
Todos los métodos reciben `UsuarioSesion` y validan permisos; los errores de negocio son `OperacionInvalida` (mensaje en español para mostrar).

| Grupo | Métodos |
|---|---|
| Convocatorias | `CicloAbiertoAsync`, `CiclosAsync`, `CicloParaConsultaAsync(anio)` |
| Consulta | `UltimasPorBloqueAsync(cicloId, cuits, conAnexos)`, `ObtenerAsync(id, u, conDetalle)`, `ActividadesAsync(ids)`, `PendientesAsync(ids)` |
| Carga | `IniciarAsync(u, bloque, tipo)`, `GuardarCeldasAsync(id, u, celdas)` (devuelve los calculados), `MarcarSinActividadAsync(id, u, tipo)`, `CambiarRangoAsync`, `GuardarRestriccionesAsync` (bloque 4), `GuardarExpedienteAsync` |
| Anexos | `AgregarAnexoAsync`, `QuitarAnexoAsync` |
| Envío | `RevisarAsync(id, u)`, `EnviarAsync(id, u, urlComprobante)` (valida plazo con la prórroga de la empresa, calcula la huella y manda mail), `EnviarFinalAsync(u, bloques, urlComprobante)` (todas las listas), `Huella(presentacion)` |
| Excel | `PlantillaAsync(u, bloques, conLoCargado)`, `ImportarAsync(u, bloques, archivo)` (atómico: todo o nada, con lista de errores) |
| Rectificación | `RectificarAsync(id, u)`, `SolicitarRectificacionAsync`, `HabilitarAsync`, `RechazarAsync`, `HabilitacionVigenteAsync`, `UltimasSolicitudesAsync`, `ListarSolicitudesAsync` |
| Observaciones y auditoría | `ObservacionesAsync`, `ConteoObservacionesAsync`, `AgregarObservacionAsync`, `EliminarObservacionAsync`, `HistorialAsync` (+ registro interno `Registrar…`; las ediciones de celdas seguidas se agrupan 30 min) |

### `ComunicacionService`
`LineaDeTiempoAsync(u, ciclo, cuit)` (mezcla lo registrado con lo que el sistema ya sabe: envíos y rectificaciones; calcula estado de la empresa y de cada nota),
`RegistrarAsync` (administración), `RechazarPlanAsync` (rechazo + habilitación masiva a rectificar), `ResponderAsync` y `SolicitarProrrogaAsync` (las hace la **empresa**),
`LimiteDeLaEmpresaAsync(ciclo, cuit)` (general o prórroga otorgada más lejana), `PendientesDeRespuestaAsync`, `PendientesPorEmpresaAsync`, `ActividadPorEmpresaAsync`
(alertas del Seguimiento), `ObtenerAsync` (adjunto), `DestinatariosDeLaEmpresaAsync`. Mail opcional por `IMailSender`; el registro se hace siempre aunque el mail falle.

### Excel
- `PlanillaExcel` — **planilla unificada de carga**: hoja "Plan" protegida, fila 1 oculta con códigos, columnas Área / código / Año / Mes / Tipo de actividad (fija) / ¿Tiene actividad? / campos. `Generar(ciclo, razon, áreas, años, cargadas)` y `Leer(archivo, áreasPermitidas, anioCiclo)` (lectura **perezosa** celda a celda; usa solo valores guardados, nunca recalcula fórmulas).
- `PlanillasModelo` — planillas modelo de anexos (Instalaciones, Paradas, Restricciones): `Existe`, `Nombre`, `DeAnexo`, `Generar`.
- `ExportadorExcel` — Excel de una presentación (una hoja por plan): `Planilla(p)`, `Columnas(plan)`, `ColumnasUnificadas()`.

### Otros
- `TableroService.ObtenerAsync(ciclo, filtro)` arma los datos del tablero; `EmpresasAsync`.
- `TipoActividadProvider` — resuelve el tipo de actividad: lo fijado a mano manda; si no, el que informe Legajos (`TodosAsync`, `DeAsync`, `Interpretar`).

## 6. Clientes externos (`Infrastructure/…`)

| Cliente | Para qué | Notas |
|---|---|---|
| `UsersAccessClient` (SOAP) | Sesión, permisos y empresas de un usuario | `Acceso:UsersAccessUrl` |
| `LegajosClient` (SOAP, con caché en memoria) | Áreas/bloques y operadoras (`BloquesPorOperadorAsync`, `TodosLosBloquesAsync`) | `Legajos:Url`; pendientes de Legajos: `tipo_actividad` y `fecha_vencimiento` (ver `PEDIDO_WS_*.md`) |
| `GdeClient` (REST) | `HistorialAsync(expediente)` y `DocumentoPdfAsync(documento)` | API interna `Gde:BaseUrl`, clave `Gde:ApiKey` en header `X-Api-Key`; **la clave nunca llega al navegador**; lenta (5–10 s), una consulta a la vez; el historial se guarda 30 min. Los `EX-` no tienen PDF propio |
| `SmtpMailSender` | Mails (comprobantes, avisos, comunicaciones) | `Mail:*`; con `ModoPrueba=true` todo se desvía a `ListaPrueba` con asunto `[PRUEBA]`. Un mail fallido **no** impide registrar |

## 7. Controladores y rutas (`PlanAnual.Web/Controllers`)

| Controlador | Política | Rutas principales |
|---|---|---|
| `AccountController` | pública | `GET /login`, `POST /login/desarrollo`, `POST /logout`, `/sin-permiso`, `/error` |
| `HomeController` | login | `/` → Tablero (si ve todo) o Mis áreas |
| `PlanController` | login | `Plan` (Mis áreas / Presentaciones), `Iniciar`, `Rectificar`, `SolicitarRectificacion`, `EnviarFinal`, `Editar/{id}?paso&plan`, `Guardar/{id}` (JSON de celdas), `GuardarTexto`, `SinActividad`, `Rango`, `AnexoSubir/Quitar/Descargar`, `Exportar/{id}`, `PlanillaModelo/{id}`, `PlanillaExcel?cargado`, `Importar`, `Comprobante/{id}`, **solo Administración:** `AdjuntosArea`, `HistorialArea`, `HistorialExpediente`, `ObservacionesArea`, `AgregarObservacion`, `EliminarObservacion`, `GuardarExpediente`; **Administración y Subsecretaría:** `DocumentoGde?numero=` (PDF de GDE) |
| `ComunicacionesController` | login | `Comunicaciones` (línea de tiempo), `Registrar`, `Rechazar` (admin), `Responder`, `SolicitarProrroga` (empresa), `Adjunto/{id}` |
| `AdminController` | Administrador | `Admin/Rectificaciones` (+ Habilitar/Rechazar), `Admin/Bloques` (tipo de actividad), `Admin/Ciclo`, `Admin/Correo` |
| `SeguimientoController` | Subsecretaría | `Seguimiento?anio` |
| `TableroController` | Subsecretaría | `Tablero?tipo&cuit&anios&anio` |

Vistas en `Views/<Controlador>/`; parciales de los modales en `Views/Plan/_*.cshtml`. Front: `wwwroot/js` (`plan-form.js` formulario, `admin-areas.js` modales de administración,
`comunicaciones.js`, `tablero.js`/`charts.js`, `tabla-datos.js`, `confirmar.js`, `texto-form.js`), `wwwroot/css` (`plan.css`, `marca.css`, `nqn-ui.css`…), íconos SVG en `wwwroot/img/iconos.svg`.

## 8. Configuración

Orden de precedencia: **variables de entorno del sitio (`web.config`) > `appsettings.Production.json` > `appsettings.json`**. Valores secretos: ver el archivo `.local`.

| Clave | Función | Valor actual en el servidor |
|---|---|---|
| `ConnectionStrings:Default` | PostgreSQL (se pisa desde `web.config`) | `Host=127.0.0.1;Port=5432;Database=plananual_pruebas;Username=plananual_app;Password=***` |
| `Database:Provider` / `AplicarMigraciones` | Motor / migrar al iniciar | `Postgres` / `false` (el esquema se aplica con los scripts de `db/`) |
| `PathBase` / `PublicOrigin` | Prefijo y origen público | `/plananual` / `https://hidrocarburos.energianeuquen.gob.ar` |
| `Acceso:IdSistema` / `ModuloOperacion` / `ModuloAdministracion` | Sistema y módulos en UsersAccess | `39` / `119` / `118` |
| `Acceso:VigenciaSesionHoras` | Antigüedad máxima de la sesión del portal | `2160` (90 días) |
| `Acceso:UsersAccessUrl` / `PortalUrl` | Servicios | `http://10.1.117.56/WSUserAccessV2/WebServiceAcces.asmx` / `https://hidrocarburos.energianeuquen.gob.ar/usersaccess/` |
| `Acceso:AccesoDeDesarrollo` | Ingreso sin portal | `false` (obligatorio en producción) |
| `Legajos:Url` | Áreas y operadoras | `http://10.1.117.66/WSLegajosCustom/LegajosWS.asmx` |
| `Gde:BaseUrl` / `ApiKey` | API de GDE | `http://10.1.117.69:8090` / *(clave en el archivo `.local`)* |
| `Anexos:Carpeta` / `MaximoMB` | Anexos del plan y de comunicaciones | `C:\ProgramData\PlanAnual\anexos` / `25` |
| `DataProtection:Carpeta` | Claves de la cookie | `C:\ProgramData\PlanAnual\keys` |
| `Mail:*` | SMTP | `Habilitado=true`, `smtp8.neuquen.gov.ar:25`, sin auth, `Desde=mceleste@neuquen.gov.ar`, **`ModoPrueba=true`**, `ListaPrueba=mauroceleste@gmail.com`, `Subsecretaria` vacío |

**Validaciones al arrancar** (`Program.cs`): en producción el sistema no inicia si hay ingreso de desarrollo activo, cadena de conexión de ejemplo (`CAMBIAR`), SQLite, o mail habilitado sin servidor.

## 9. Dónde está cada cosa

| Qué | Dónde |
|---|---|
| Código fuente | `C:\Users\mauro.celeste\Documents\GitHub\plananual` · GitHub **privado** `mauroceleste/plananual` (rama `main`) |
| Sitio publicado | `10.1.117.117` → `C:\inetpub\wwwroot\plananual` (sitio IIS "plananual", pool "PlanAnual", in-process) |
| Base de datos | PostgreSQL 16 en `10.1.117.117`, servicio `postgresql-x64-16`, datos en `C:\Program Files\PostgreSQL\16\data`, base `plananual_pruebas` (≈39 MB) |
| Anexos (archivos) | `C:\ProgramData\PlanAnual\anexos` (≈10 MB; comunicaciones en `…\anexos\comunicaciones\<cicloId>\`) |
| Claves de cookie | `C:\ProgramData\PlanAnual\keys` |
| Configuración del servidor | `C:\inetpub\wwwroot\plananual\{web.config, appsettings.json, appsettings.Production.json}` |
| Acceso remoto a la base | `pg_hba.conf` tiene una regla **temporal** para `10.1.35.81` → `plananual_pruebas` / `plananual_app` (usada para cargas desde la PC de desarrollo; conviene quitarla al terminar) |
| Pedidos a otros equipos | `PEDIDO_WS_TIPO_ACTIVIDAD.md`, `PEDIDO_WS_VENCIMIENTO_CONCESION.md`, `db/PEDIDO_BASE_POSTGRESQL.md` |

## 10. Despliegue

Sin acceso remoto por WinRM, se publica por la carpeta compartida `\\10.1.117.117\C$`:

1. `dotnet publish src/PlanAnual.Web -c Release -o publish`
2. **Borrar de `publish`:** `web.config`, `appsettings.json`, `appsettings.Production.json`, `appsettings.Development.json` (la configuración del servidor no se pisa).
3. Poner `app_offline.htm` en el sitio (la app se detiene sola).
4. Copiar el **contenido** de `publish` sobre el sitio, **reintentando** hasta que no haya archivos bloqueados.
5. **Verificar por hash (MD5)** que todos los archivos del sitio son iguales a los de `publish`.
6. Quitar `app_offline.htm`; si hace falta reiniciar, tocar `web.config`.
7. Si hay cambios de modelo: antes, correr el script `db/00N_*.sql` en la base (es idempotente).

Detalle completo, requisitos del servidor (Hosting Bundle 10) y checklist previo a abrir a las empresas: `DESPLIEGUE.md`.

## 11. Pruebas y desarrollo local

- `dotnet test` (desde la raíz): 264 pruebas — dominio (cálculo/validación/agregación), servicios con SQLite en memoria, clientes externos con respuestas simuladas, Excel (que abra, que no tenga validaciones inválidas, que rechace fórmulas sin valor), migraciones y tipos de datos.
- Desarrollo: `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/PlanAnual.Web --no-launch-profile --urls http://localhost:5213` (SQLite en `App_Data/plananual-dev.db`, ingreso de prueba).

## 12. Seguridad y pendientes operativos

- **Contraseña de la base y clave de GDE** circularon fuera de lugar seguro: conviene **rotarlas** (cambiar `plananual_app` en PostgreSQL y actualizar `web.config` + `appsettings.json`; pedir una nueva `Gde:ApiKey`).
- Quitar la regla temporal de `pg_hba.conf` para `10.1.35.81` cuando termine el trabajo remoto.
- **No hay copia de seguridad programada** de la base ni de los anexos documentada: definir `pg_dump` periódico y copia de `C:\ProgramData\PlanAnual`.
- Mail: cargar `Mail:Subsecretaria`, usar un remitente institucional y pasar `Mail:ModoPrueba=false` antes de abrir a las empresas reales.
- UsersAccess no vence las sesiones: un `idSesion` vigente adivinado podría usarse (planteado a quien administra el portal). Falta el módulo de "solo consulta".
- El rol Subsecretaría existe en el código pero no hay módulo en UsersAccess que lo asigne.
- Pendientes de Legajos (Pablo): `tipo_actividad` y `fecha_vencimiento` de concesión.
- Limpieza en el servidor: `C:\Temp\plananual-paquete`, `C:\Temp\plananual-nuevo`.
- La base actual se llama `plananual_pruebas` y contiene datos de prueba (YPF, Pan American): al salir a producción real conviene una base limpia con `db/001…009`.
