# Plan Anual de Inversiones — Instructivo de uso

Sistema de la Subsecretaría de Hidrocarburos (Neuquén) para que las empresas operadoras presenten el **Plan Anual de Inversiones** de cada área
y para que la Subsecretaría lo revise, haga el seguimiento y se comunique con las empresas.

- Dirección: `https://hidrocarburos.energianeuquen.gob.ar/plananual` (red interna de pruebas: `http://10.1.117.117/plananual`).
- No tiene usuario y contraseña propios: se entra desde el **Portal de Hidrocarburos** (UsersAccess).

---

## 1. Quién es quién

| Rol | Quién es | Qué ve y qué hace |
|---|---|---|
| **Empresa** | Persona de una operadora (módulo 119 "Operación" en UsersAccess) | Solo **sus** áreas. Carga, adjunta, envía, pide rectificar, responde notas y pide prórroga |
| **Administrador** | Personal de la Subsecretaría (módulo 118 "Administración") | Todas las empresas. Revisa, carga **en nombre de** una empresa, anota observaciones internas, se comunica, habilita rectificaciones, configura la convocatoria |
| **Subsecretaría (consulta)** | Quien solo mira | Tablero, Seguimiento y Comunicaciones, sin modificar nada (hoy no hay módulo propio en UsersAccess: ver `ARQUITECTURA_Y_CODIGO.md`) |

Si una persona tiene los dos módulos, **gana Operación** (ve solo su empresa). Una persona de Operación **sin empresa asignada no puede ingresar**.

## 2. Cómo se entra

1. Entrá al Portal de Hidrocarburos con tu usuario y contraseña.
2. Elegí el sistema **Plan Anual**; el portal te manda al sistema ya identificado.
3. La sesión del sistema se cierra sola tras **30 minutos sin actividad** (se renueva mientras trabajás). Si te la cierra, volvés a entrar desde el portal.

Si aparece "La sesión del portal es muy antigua": cerrá sesión en el portal y volvé a entrar. El sistema solo acepta sesiones del portal de hasta 90 días.

## 3. Conceptos básicos

- **Convocatoria (ciclo):** el plan de un año (ej. "Plan de Inversiones 2027") con su **fecha límite** y, si existe, una **prórroga general**. En la parte superior hay un **selector de convocatoria** para ver años anteriores (solo lectura).
- **Área:** cada bloque/área de la empresa que figura en Legajos de Áreas. **Cada área tiene su propia planilla.**
- **Planes de actividad:** cada área puede declarar **Plan Convencional** y/o **Plan No Convencional (CENCH)**. La empresa **carga solo los que tiene**; el que no tiene se marca "sin actividad" (queda en ceros).
- **Períodos:** el primer año del plan se carga **mes a mes**; los años siguientes son **anuales**. Se puede cambiar el rango de años (hasta el vencimiento de la concesión).
- **Versiones:** cada envío es una versión (v1, v2…). Una rectificación crea una versión nueva; las anteriores quedan guardadas.
- **Comprobante:** al enviar, el sistema da un número (ej. `PAI-2027-000123-v1`) y una huella (SHA-256) que prueba que los valores no cambiaron.

---

## 4. Instructivo para la EMPRESA

### 4.1. Pantalla "Mis áreas"
Lista las áreas de tu empresa con su estado: **Sin iniciar**, **Borrador**, **Enviada**. Arriba ves:
- El **plazo de tu empresa** (el general de la convocatoria o la prórroga que te hayan otorgado).
- El panel de **Novedades**: notas de la Subsecretaría que esperan tu respuesta, pedidos tuyos pendientes, etc.
- Botones para bajar la **planilla Excel** y **cargar con Excel**.

### 4.2. Cargar un área (formulario)
1. En "Mis áreas" apretá **Iniciar** en el área. Se crea un borrador.
2. El formulario tiene **pasos** (solapas). En el paso de valores aparece una tarjeta por plan (**Convencional** / **No Convencional**):
   - Completá los valores de cada mes del primer año y de cada año siguiente.
   - Si el área **no tiene** ese tipo de actividad, apretá **"Sin actividad"**: el plan se completa con ceros y queda declarado.
   - Los campos **calculados** (totales, flujos) se completan solos; no se editan.
   - Cada campo tiene su unidad (MMUSD, bbl/d, MMm3…) y un máximo de decimales; si el valor no es razonable el sistema lo avisa.
3. Los cambios se **guardan solos** al salir de cada celda. Podés cerrar y seguir después.
4. **Rango de años:** podés ampliar o reducir los años que cubre el plan.
5. **Restricciones a la actividad (Bloque 4):** completá los textos (conflictos superficiarios, equipos y contratos firmes, plazos y costos).
6. **Anexos:** adjuntá la documentación pedida (fundamentación del flujo de fondos, flujograma de instalaciones, paradas de planta, presentación del plan; "Otros" es opcional). Formatos permitidos: PDF, Excel, Word, imágenes, ZIP; hasta 25 MB cada uno. Hay **planillas modelo** descargables para Instalaciones, Paradas y Restricciones.

### 4.3. Cargar con Excel
1. En "Mis áreas" bajá **la planilla** (una hoja "Plan" con todas tus áreas). Trae las columnas: Área, Año, Mes, **Tipo de actividad** (fija, no se puede cambiar), **¿Tiene actividad?** (Sí/No) y los campos.
2. Completá **solo valores** (no fórmulas: el sistema lee el valor guardado). Las columnas que no corresponden al tipo están en gris. Si ponés **"No"** en "¿Tiene actividad?", ese bloque se carga en ceros.
3. Para **corregir** o **rectificar**, bajá la planilla **"con lo cargado"**: viene con los valores actuales; los cambiás y la volvés a subir.
4. Subila con **Cargar con Excel**. El sistema valida todo y te lista los errores (hoja, fila, columna). Si hay errores no se carga nada: corregís y volvés a subir.

### 4.4. Revisar y enviar
- El sistema **revisa** el área: marca **errores** (impiden enviar) y **requisitos** (falta cargar o marcar "sin actividad" un plan, falta un anexo obligatorio, etc.).
- Cuando todo está en orden, **Enviar**. Si estás fuera de plazo (según tu límite), no te deja, salvo que tengas prórroga.
- **Enviar final:** envía de una vez todas las áreas que estén listas.
- Te llega el **comprobante** (pantalla y mail). Se puede descargar el **Excel** del área.

### 4.5. Corregir un área ya enviada (rectificación)
1. En el área enviada apretá **Pedir rectificación** y contá el motivo.
2. La Subsecretaría la **habilita por 7 días** (o la rechaza); te llega un mail.
3. Mientras esté habilitada, creás la versión rectificatoria, la editás y la enviás. Pasados los 7 días, se vence.

### 4.6. Comunicaciones con la Subsecretaría
En **Comunicaciones** ves la **línea de tiempo** de tu empresa: notas recibidas, tus respuestas, prórrogas y decisiones sobre el plan, más los envíos y rectificaciones que el sistema registra solo.
- **Responder una nota:** en la nota pendiente apretá **Responder**, escribí la respuesta, podés indicar el número de GDE y adjuntar un archivo.
- **Pedir prórroga:** botón **Solicitar prórroga**; contá el motivo y, si querés, hasta qué fecha. Solo se puede tener **un pedido pendiente a la vez**. Cuando se otorga, **tu plazo cambia** (se ve arriba en "Mis áreas").
- Estados de una nota con plazo: **Pendiente**, **Vencida** (pasó el plazo sin respuesta), **Respondida**.
- Estado general de tu plan: Sin presentar, En carga, Presentado, Información solicitada, Observado, Rechazado, Aprobado.
- Tu empresa **no ve** los movimientos marcados como internos.

---

## 5. Instructivo para ADMINISTRACIÓN

### 5.1. Elegir empresa y trabajar "en nombre de"
- En **Presentaciones** (Mis áreas) elegís la empresa. Podés **cargar, importar, adjuntar o enviar por la empresa** (queda registrado "asistida por <tu nombre>").
- Podés elegir la convocatoria a mirar (selector).

### 5.2. Herramientas por área (iconos de la lista)
| Icono / columna | Qué hace |
|---|---|
| **Adjuntos** | Abre un modal con los archivos del área (por tipo) y los descarga |
| **Obs.** | **Observaciones internas**: notas de la Subsecretaría sobre el área. **La empresa no las ve.** Cada una queda con autor y fecha y sigue ahí si la empresa rectifica. Se pueden eliminar |
| **Historial** | Quién cambió qué y cuándo (valores, textos, adjuntos, envíos, expediente). Las ediciones de celdas seguidas se agrupan (30 min) |
| **Expediente GDE** | Número de expediente de GDE del área (dato interno). Con el número cargado, el modal muestra los **movimientos** y **documentos** del expediente en GDE |
| **Número de documento (NO-, IF-…)** | En el historial del expediente y en Comunicaciones, **al hacer clic en el número se abre el PDF** desde GDE (tarda unos segundos) |

### 5.3. Comunicaciones con la empresa
Pantalla **Comunicaciones** (se elige la empresa desde Presentaciones o Seguimiento). Se **registra siempre** lo que pasa, y el **mail es opcional**.
- **Registrar movimiento:** Nota enviada (con **plazo de respuesta** opcional), Respuesta de la empresa (recibida por GDE o mail), Prórroga solicitada, **Prórroga otorgada** (el nuevo plazo tiene que ser posterior al límite actual de esa empresa), Plan observado, Plan aprobado.
- Cada movimiento puede ser **de toda la empresa** o de **un área puntual**, tener **número de GDE**, adjunto, y marcarse **interno** (la empresa no lo ve).
- **Rechazar el plan:** registra el motivo y, si se tilda, **habilita a la empresa a rectificar todas sus áreas enviadas** hasta la fecha indicada.
- **Enviar por mail** (opcional): sale desde el sistema a los contactos de la empresa más los que agregues; queda anotado si salió o falló.
- Una nota solo puede contestar un pedido de prórroga de la empresa.

### 5.4. Rectificaciones
**Rectificaciones** lista los pedidos de las empresas: **Habilitar** (7 días) o **Rechazar**. A la empresa le llega un mail.

### 5.5. Configuración (solo Administrador)
- **Convocatoria:** año, nombre, fecha límite, prórroga general, abierta/cerrada, nota de GDE. Es la convocatoria vigente; las anteriores quedan guardadas.
- **Tipo de actividad (Bloques):** fija a mano el tipo de actividad de un área mientras Legajos no lo informe (lo fijado a mano manda).
- **Correo:** muestra cómo está configurado el envío (nunca la contraseña) y manda un mail de prueba. Mientras esté el **modo de prueba**, todo mail se desvía a la casilla de prueba con el asunto `[PRUEBA]`.

## 6. Instructivo para la SUBSECRETARÍA (consulta)

- **Tablero:** indicadores y gráficos de las presentaciones enviadas: inversión por año (Convencional/CENCH), pozos, producción, mix de gastos, escenarios de precio, alertas de planillas con problemas, valores atípicos y cambios entre versiones. Filtros por tipo de plan, empresa y cantidad de años.
- **Seguimiento:** por empresa, cuántas áreas tiene, cuántas **enviadas** y cuántas en **borrador**, y las últimas presentaciones. Arriba tres alertas: prórrogas por resolver (rojo), notas sin respuesta (ámbar), respuestas de los últimos 7 días (verde).
- **Comunicaciones:** la línea de tiempo de cada empresa (solo lectura).

---

## 7. Problemas frecuentes

| Síntoma | Qué hacer |
|---|---|
| "La sesión del portal es muy antigua" / no me deja entrar | Cerrar sesión en el portal y volver a entrar; si sigue, avisar a Sistemas |
| "Tu usuario no tiene una empresa asignada" | Pedir a la Subsecretaría que asocie tu usuario a la empresa |
| El Excel dice "Pegá solo valores" | Hay fórmulas en la planilla: copiar y pegar como **valores** y volver a subir |
| El Excel se rechaza con errores | Leer la lista (hoja/fila/columna), corregir y reintentar: no se carga nada hasta que no haya errores |
| No me deja enviar | Revisar el listado de errores y requisitos del área; o estás fuera de plazo (pedir prórroga) |
| No puedo editar un área enviada | Pedir rectificación; la edición solo se permite mientras la habilitación esté vigente |
| "GDE tardó demasiado" al abrir un documento | Es lento (5–10 s). Esperar y reintentar; si persiste, probar más tarde |
| La descarga de un documento de GDE dice que no existe | Revisar que el número esté bien escrito (formato `NO-2027-00000000-NEU-ÁREA#REPARTICIÓN`) |
| Un área no tiene tipo de actividad | Administración la define en **Tipo de actividad** |
