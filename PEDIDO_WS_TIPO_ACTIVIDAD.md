# Pedido al servicio de Legajos de Áreas: tipo de actividad por bloque

**Para:** Pablo (administra `WSLegajosCustom`)
**De:** Sistema Plan Anual de Inversiones (Subsecretaría de Hidrocarburos)

## Qué necesitamos

El sistema de Plan Anual de Inversiones tiene dos planillas distintas, y a cada bloque le corresponde **una**:

- **Plan de Desarrollo Convencionales**
- **Plan de Desarrollo CENCH** (concesión de explotación no convencional)

Hoy el servicio de Legajos **no informa** cuál es cuál, así que la Subsecretaría tiene que cargarlo a mano bloque por bloque (128 bloques).
Necesitamos que cada bloque venga con su **tipo de actividad**.

## Cómo pedirlo (opción recomendada)

Agregar **una columna `tipo_actividad`** a las dos operaciones que ya usamos, en la misma fila de cada bloque:

| Operación | Cambio |
|---|---|
| `GetAllAreasBloquesActivas()` | agregar la columna `tipo_actividad` |
| `GetAllAreasBloquesActivasByOperador(cuit)` | agregar la columna `tipo_actividad` |

Hoy devuelven: `idbloque`, `nombre`, `nombrearea`, `nombrebloque`, `cuit`, `razon_social`, `ID_NQN`.
Con el cambio: las mismas **más** `tipo_actividad` (string, puede venir vacío).

### Valores

| Valor | Significa |
|---|---|
| `CONVENCIONAL` | El bloque presenta el Plan de Desarrollo Convencionales |
| `NO_CONVENCIONAL` | El bloque presenta el Plan de Desarrollo CENCH |
| vacío / `NULL` | Sin definir (el sistema no deja comenzar esa área hasta que alguien lo defina) |

El sistema interpreta sin distinguir mayúsculas, acentos, guiones ni guiones bajos: `Convencional`, `No convencional`, `CENCH`, `NC` también sirven.
Cualquier otro valor se trata como "sin definir" (nunca se adivina).

### Por qué una columna y no un método nuevo

Un método `GetTipoActividadByBloque(idbloque)` obligaría a una consulta por bloque (128 llamadas por pantalla).
Con la columna, el sistema lo recibe en la misma consulta que ya hace y no necesita ningún cambio ni nueva publicación de nuestro lado.

## Qué no hay que tocar

Para no romper lo que ya anda:

- No renombrar ni quitar ninguna de las 7 columnas actuales.
- Mantener la forma de la respuesta (DataTable `dt` dentro de un DataSet/DiffGram) y el espacio de nombres `http://tempuri.org/`.
- `idbloque` sigue siendo la clave única del bloque.

## Dos preguntas para definir antes

1. **¿De dónde sale el dato?** Probablemente del tipo de concesión del bloque (o de su instrumento legal). Si no está cargado hoy en Legajos, hay que cargarlo ahí una vez: así lo usan todos los sistemas.
2. **¿Existen bloques con actividad convencional y no convencional a la vez?** El sistema hoy asigna **una sola** planilla por bloque. Si hay casos mixtos, hay que decidir cómo se presentan (por ejemplo, un bloque lógico por cada tipo, o dos planillas por bloque). Conviene saberlo ahora.

## Cómo verificamos que quedó bien

1. Llamar `GetAllAreasBloquesActivas` y ver que viene la columna `tipo_actividad`.
2. En el sistema, **Administración → Tipo de actividad**: la columna **"Según Legajos"** se completa sola con lo que informe el servicio.
3. Lo que la Subsecretaría ya haya fijado a mano **sigue mandando** (se puede corregir bloque por bloque); lo demás pasa a regirse por Legajos sin tocar nada.

## Ejemplo de fila esperada

```xml
<dt>
  <idbloque>56</idbloque>
  <nombre>Aguada Baguales - Aguada Baguales</nombre>
  <nombrearea>Aguada Baguales</nombrearea>
  <nombrebloque>Aguada Baguales</nombrebloque>
  <cuit>30711732752</cuit>
  <razon_social>Oilstone Energia S.A.</razon_social>
  <ID_NQN>A002</ID_NQN>
  <tipo_actividad>CONVENCIONAL</tipo_actividad>
</dt>
```
