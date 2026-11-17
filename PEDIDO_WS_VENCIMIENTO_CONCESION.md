# Pedido al servicio de Legajos de Áreas: fecha de vencimiento de la concesión por bloque

**Para:** Pablo (administra `WSLegajosCustom`)
**De:** Sistema Plan Anual de Inversiones (Subsecretaría de Hidrocarburos)

## Qué necesitamos (prioritario)

La **fecha de vencimiento de la concesión de cada bloque**.

Para qué la usamos: el Plan Anual se carga hasta el último año de la concesión. Hoy no tenemos ese dato, así que la planilla Excel trae
25 años por bloque (y por tipo de actividad) y la empresa tiene que dejar en blanco los que no usa. En una empresa con 30 áreas son miles de filas
de más. Con el vencimiento, la planilla y el formulario traen **solo los años que corresponden** y podemos avisar si alguien carga de más.

## Cómo pedirlo (opción recomendada)

Agregar **una columna `fecha_vencimiento`** a las dos operaciones que ya usamos, en la misma fila de cada bloque:

| Operación | Cambio |
|---|---|
| `GetAllAreasBloquesActivas()` | agregar la columna `fecha_vencimiento` |
| `GetAllAreasBloquesActivasByOperador(cuit)` | agregar la columna `fecha_vencimiento` |

Hoy devuelven: `idbloque`, `nombre`, `nombrearea`, `nombrebloque`, `cuit`, `razon_social`, `ID_NQN`.
Con el cambio: las mismas **más** `fecha_vencimiento`.

- **Formato:** texto `yyyy-MM-dd` (igual que ya devuelve `GetFechaInicioByBloque`). Vacío si no hay dato.
- **De dónde sale (sugerencia):** `contratos_contrato.fecha_fin` del contrato del bloque, por `contratos_contrato_bloques`, que es el mismo camino que ya usa
  `GetFechaInicioByBloque` para `fecha_inicio`. Si el bloque tiene más de un contrato: el **vigente hoy** (activo y con `fecha_inicio <= hoy <= fecha_fin`);
  si ninguno está vigente, el de `fecha_fin` más reciente.

### Por qué una columna y no un método nuevo

Un método `GetFechaFinByBloque(codBloque)` (espejo de `GetFechaInicioByBloque`) obligaría a una consulta por bloque: el sistema muestra más de 120.
Con la columna lo recibe en la misma consulta que ya hace. Del lado nuestro el cambio es chico y la columna es **opcional**: si no viene, todo sigue como hoy.

## Preguntas para definir antes

1. **¿`contratos_contrato.fecha_fin` es el vencimiento de la concesión?** ¿Hay contratos de distinto tipo por bloque (exploración, explotación, transporte)
   y, si los hay, cuál es el que corresponde al Plan de Inversiones (la concesión de explotación)?
2. **Prórrogas:** ¿se cargan como un contrato nuevo o se modifica la `fecha_fin` del existente? ¿Hay prórrogas en trámite que todavía no estén reflejadas?
3. **Casos raros:** bloques con más de un contrato vigente, o con `fecha_fin` vacía. ¿Cómo conviene que vengan (vacío, o el más reciente)?

## Dato secundario y opcional: tipo de actividad

Pedíamos antes (`PEDIDO_WS_TIPO_ACTIVIDAD.md`) una columna `tipo_actividad` para decidir qué planilla carga cada bloque. **Eso cambió**:
una empresa puede hacer trabajo de un tipo de actividad distinto al que diga Legajos, y ahora cada área puede tener Convencional y No Convencional a la vez.
Por eso el dato **ya no define nada** en el sistema.

Si se puede agregar sin costo (misma columna `tipo_actividad`, mismos valores `CONVENCIONAL` / `NO_CONVENCIONAL`, vacío si no se sabe),
lo usaríamos solo como **sugerencia** para precompletar el «¿Tiene actividad? Sí / No» de la planilla; la empresa lo corrige si no coincide.
**No es urgente** y no bloquea nada: lo importante es la fecha de vencimiento.

## Qué no hay que tocar

Para no romper lo que ya anda:

- No renombrar ni quitar ninguna de las 7 columnas actuales.
- Mantener la forma de la respuesta (DataTable `dt` dentro de un DataSet/DiffGram) y el espacio de nombres `http://tempuri.org/`.
- `idbloque` sigue siendo la clave única del bloque.

## Cómo verificamos que quedó bien

1. Llamar `GetAllAreasBloquesActivas` y ver que viene la columna `fecha_vencimiento` con fechas `yyyy-MM-dd` (y vacía donde no hay dato).
2. Comparar unos pocos bloques conocidos contra el legajo de la concesión.
3. En el sistema: la planilla Excel de una empresa trae solo los años hasta el vencimiento de cada bloque.
