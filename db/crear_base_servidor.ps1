<#
  Version para el SERVIDOR 10.1.117.117 (comparte PostgreSQL con Tramites).
  Crea el usuario "plananual_app" y la base "plananual_pruebas", aplica 001_creacion_inicial.sql
  (tiene que estar en la misma carpeta que este script) y deja la cadena de conexion en conexion-servidor.local.

  NO necesita ni cambia la clave de "postgres": abre un acceso temporal SOLO para ese usuario y SOLO desde
  este servidor (127.0.0.1 / ::1), lo usa, y lo REVIERTE siempre (bloque finally), dejando pg_hba.conf
  exactamente como estaba. Hace una copia de seguridad antes de tocarlo.
  NO reinicia el servicio de PostgreSQL (no corta Tramites): solo recarga la configuracion.

  Ejecutar en PowerShell COMO ADMINISTRADOR, en el servidor:
    powershell -ExecutionPolicy Bypass -File "C:\temp\plananual\crear_base_servidor.ps1"
#>
#Requires -RunAsAdministrator
param([string]$Version = '16', [int]$Puerto = 5432)

$ErrorActionPreference = 'Continue'     # los avisos de psql salen por stderr: se controla $LASTEXITCODE a mano
$env:PGOPTIONS = '-c client_min_messages=warning'
$env:PGCLIENTENCODING = 'UTF8'          # el .sql esta en UTF-8; sin esto psql usa la codificacion de Windows

$base      = "C:\Program Files\PostgreSQL\$Version"
$data      = "$base\data"
$hba       = "$data\pg_hba.conf"
$bak       = "$data\pg_hba.conf.plananual.bak"
$psql      = "$base\bin\psql.exe"
$pgctl     = "$base\bin\pg_ctl.exe"
$isready   = "$base\bin\pg_isready.exe"
$sqlSchema = Join-Path $PSScriptRoot '001_creacion_inicial.sql'
$salida    = Join-Path $PSScriptRoot 'conexion-servidor.local'
$marca     = '# PLANANUAL-TEMPORAL'

foreach ($f in @($psql, $pgctl, $isready, $hba, $sqlSchema)) {
    if (-not (Test-Path $f)) { throw "No se encuentra: $f" }
}

Write-Host ""
Write-Host "PostgreSQL $Version del servidor"
Write-Host "Se va a:"
Write-Host "  1) abrir un acceso temporal sin clave SOLO para 'postgres' y SOLO desde este servidor,"
Write-Host "  2) crear el usuario plananual_app y la base plananual_pruebas,"
Write-Host "  3) aplicar 001_creacion_inicial.sql,"
Write-Host "  4) REVERTIR el acceso temporal (pg_hba.conf vuelve a como estaba)."
Write-Host "No se reinicia el servicio: solo se recarga la configuracion (Tramites no se corta)."
$r = Read-Host "Continuar? (S/N)"
if ($r -notmatch '^[sS]') { Write-Host "Cancelado. No se toco nada."; return }

# Si una corrida anterior quedo a medias, partir de la copia de seguridad (el original verdadero).
$original = [IO.File]::ReadAllText($hba)
if ($original.Contains($marca)) {
    if (-not (Test-Path $bak)) { throw "pg_hba.conf tiene lineas temporales de una corrida anterior y no hay copia en $bak. Revisalo a mano." }
    $original = [IO.File]::ReadAllText($bak)
    Write-Host "Se encontro una corrida anterior a medias: se parte de la copia de seguridad."
} else {
    [IO.File]::WriteAllText($bak, $original)
}

function Recargar-Config {
    & $pgctl reload -D $data | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "pg_ctl reload fallo (codigo $LASTEXITCODE). Revisa pg_hba.conf (copia en $bak)." }
    Start-Sleep -Seconds 2
}

function Esperar-Servidor {
    for ($i = 0; $i -lt 40; $i++) {
        & $isready -h 127.0.0.1 -p $Puerto | Out-Null
        if ($LASTEXITCODE -eq 0) { return }
        Start-Sleep -Seconds 1
    }
    throw 'PostgreSQL no responde.'
}

function Psql-Admin([string]$sql, [string]$db = 'postgres') {
    $out = $sql | & $psql -h 127.0.0.1 -p $Puerto -U postgres -d $db -w -v ON_ERROR_STOP=1 -q -t -A
    if ($LASTEXITCODE -ne 0) { throw "psql fallo ejecutando: $sql" }
    return $out
}

$clave = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 24 | ForEach-Object { [char]$_ })

try {
    # 1) acceso temporal (solo usuario postgres, solo loopback)
    $temporal = "$marca`r`nhost    all    postgres    127.0.0.1/32    trust`r`nhost    all    postgres    ::1/128         trust`r`n"
    [IO.File]::WriteAllText($hba, $temporal + $original)
    Recargar-Config
    Esperar-Servidor
    Write-Host "Acceso temporal abierto."

    # 2) usuario y base
    if (-not (Psql-Admin "select 1 from pg_roles where rolname='plananual_app'")) {
        Psql-Admin "CREATE ROLE plananual_app LOGIN PASSWORD '$clave';" | Out-Null
        Write-Host "Usuario plananual_app creado."
    } else {
        Psql-Admin "ALTER ROLE plananual_app PASSWORD '$clave';" | Out-Null
        Write-Host "El usuario plananual_app ya existia: se le puso una clave nueva."
    }
    if (-not (Psql-Admin "select 1 from pg_database where datname='plananual_pruebas'")) {
        Psql-Admin "CREATE DATABASE plananual_pruebas OWNER plananual_app ENCODING 'UTF8' TEMPLATE template0;" | Out-Null
        Psql-Admin "REVOKE ALL ON DATABASE plananual_pruebas FROM PUBLIC;" | Out-Null
        Write-Host "Base plananual_pruebas creada."
    } else {
        Write-Host "La base plananual_pruebas ya existia."
    }

    # 3) esquema (las tablas quedan a nombre de plananual_app)
    & $psql -h 127.0.0.1 -p $Puerto -U postgres -d plananual_pruebas -w -v ON_ERROR_STOP=1 -q -c "SET ROLE plananual_app" -f $sqlSchema
    if ($LASTEXITCODE -ne 0) { throw 'Fallo al aplicar 001_creacion_inicial.sql' }
    Write-Host "Esquema aplicado."

    [IO.File]::WriteAllText($salida, "Host=127.0.0.1;Port=$Puerto;Database=plananual_pruebas;Username=plananual_app;Password=$clave`r`n")
    Write-Host "Cadena de conexion guardada en: $salida"
}
finally {
    # 4) revertir SIEMPRE
    [IO.File]::WriteAllText($hba, $original)
    Recargar-Config
    Esperar-Servidor
    & $psql -h 127.0.0.1 -p $Puerto -U postgres -d postgres -w -q -c "select 1" 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { Write-Warning "OJO: 'postgres' sigue entrando sin clave. Revisa $hba (copia en $bak)." }
    else { Write-Host "Acceso temporal REVERTIDO: 'postgres' vuelve a pedir clave." }
}

Write-Host ""
Write-Host "LISTO. La cadena de conexion esta en $salida (tiene la clave: no la compartas por mail ni chat)."
