<#
  Crea en el PostgreSQL LOCAL de esta PC el usuario "plananual_app" y la base "plananual_pruebas",
  aplica db\001_creacion_inicial.sql y deja la cadena de conexion en db\conexion-local.local.

  NO necesita la clave de "postgres": abre un acceso temporal SOLO para ese usuario y SOLO desde
  esta PC (127.0.0.1 / ::1), lo usa, y lo REVIERTE siempre (bloque finally), dejando pg_hba.conf
  exactamente como estaba. Hace una copia de seguridad antes de tocarlo.

  Ejecutar en PowerShell COMO ADMINISTRADOR:
    powershell -ExecutionPolicy Bypass -File "C:\Users\mauro.celeste\Documents\GitHub\Plan Anual\PlanAnual\db\crear_base_local.ps1"
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
$isready   = "$base\bin\pg_isready.exe"
$sqlSchema = Join-Path $PSScriptRoot '001_creacion_inicial.sql'
$salida    = Join-Path $PSScriptRoot 'conexion-local.local'
$marca     = '# PLANANUAL-TEMPORAL'

foreach ($f in @($psql, $isready, $hba, $sqlSchema)) {
    if (-not (Test-Path $f)) { throw "No se encuentra: $f" }
}
$svc = (Get-CimInstance Win32_Service -Filter "Name like 'postgresql%$Version'" | Select-Object -First 1).Name
if (-not $svc) { throw "No se encontro el servicio de PostgreSQL $Version" }

Write-Host ""
Write-Host "PostgreSQL $Version local - servicio: $svc"
Write-Host "Se va a:"
Write-Host "  1) abrir un acceso temporal sin clave SOLO para 'postgres' y SOLO desde esta PC,"
Write-Host "  2) crear el usuario plananual_app y la base plananual_pruebas,"
Write-Host "  3) aplicar db\001_creacion_inicial.sql,"
Write-Host "  4) REVERTIR el acceso temporal (pg_hba.conf vuelve a como estaba)."
Write-Host "El servicio se reinicia dos veces (unos segundos)."
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

function Esperar-Servidor {
    for ($i = 0; $i -lt 40; $i++) {
        & $isready -h 127.0.0.1 -p $Puerto | Out-Null
        if ($LASTEXITCODE -eq 0) { return }
        Start-Sleep -Seconds 1
    }
    throw 'PostgreSQL no volvio a responder.'
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
    Restart-Service -Name $svc -Force
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

    [IO.File]::WriteAllText($salida, "Host=localhost;Port=$Puerto;Database=plananual_pruebas;Username=plananual_app;Password=$clave`r`n")
    Write-Host "Cadena de conexion guardada en: $salida"
}
finally {
    # 4) revertir SIEMPRE
    [IO.File]::WriteAllText($hba, $original)
    Restart-Service -Name $svc -Force
    Esperar-Servidor
    & $psql -h 127.0.0.1 -p $Puerto -U postgres -d postgres -w -q -c "select 1" 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { Write-Warning "OJO: 'postgres' sigue entrando sin clave. Revisa $hba (copia en $bak)." }
    else { Write-Host "Acceso temporal REVERTIDO: 'postgres' vuelve a pedir clave." }
}

Write-Host ""
Write-Host "LISTO. Avisale a Claude que termino."
