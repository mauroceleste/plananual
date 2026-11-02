"""
Carga DATOS DE PRUEBA: completa y envia una planilla por cada area (con 9 a 22 anios de plan), entrando como cada
empresa por el ingreso de desarrollo, y deja unas areas sin iniciar a proposito (para probar un tramite incompleto).

NO es para produccion. Uso (la app debe correr en modo Development, contra la base de PRUEBA, en el puerto 5215):
    $env:ASPNETCORE_ENVIRONMENT="Development"; $env:Database__Provider="Postgres"
    $env:ConnectionStrings__Default=(Get-Content db\conexion-local.local -Raw).Trim()
    dotnet run --project src\PlanAnual.Web --no-launch-profile --urls http://localhost:5215
    python db\cargar_datos_de_prueba.py
Salta las areas que ya tienen una presentacion (no pisa lo que cargaste a mano). El mail debe estar deshabilitado.
"""import urllib.request, urllib.parse, http.cookiejar, re, json, html, random, math, subprocess, os

B = "http://localhost:5215"
CONEXION = r"C:/Users/mauro.celeste/Documents/GitHub/Plan Anual/PlanAnual/db/conexion-local.local"


def psql(sql):
    pw = open(CONEXION).read().strip().split("Password=")[1].split(";")[0].strip()
    env = dict(os.environ, PGPASSWORD=pw, PGCLIENTENCODING="UTF8")
    r = subprocess.run([r"C:\Program Files\PostgreSQL\16\bin\psql.exe", "-h", "localhost", "-U", "plananual_app",
                        "-d", "plananual_pruebas", "-w", "-At", "-F", "|", "-c", sql],
                       capture_output=True, text=True, encoding="utf8", errors="replace", env=env)
    if r.returncode:
        raise SystemExit(r.stderr)
    return [l.split("|") for l in r.stdout.splitlines() if l]


tipos = {int(a): int(b) for a, b in psql("select id_bloque,tipo_plan from bloques_tipo_actividad")}
ya = {int(a) for (a,) in psql("select id_bloque from presentaciones")}

# ---- areas reales (Legajos)
sobre = ('<?xml version="1.0" encoding="utf-8"?><soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">'
         '<soap:Body><GetAllAreasBloquesActivas xmlns="http://tempuri.org/"/></soap:Body></soap:Envelope>')
x = urllib.request.build_opener(urllib.request.ProxyHandler({})).open(urllib.request.Request(
    "http://10.1.117.66/WSLegajosCustom/LegajosWS.asmx", sobre.encode(),
    {"Content-Type": "text/xml", "SOAPAction": '"http://tempuri.org/GetAllAreasBloquesActivas"'}), timeout=30
).read().decode("utf8").split("<diffgr:before>")[0]


def g(f, t):
    m = re.search(rf"<{t}>([^<]*)</{t}>", f)
    return html.unescape(m.group(1)).strip() if m else ""


areas = [dict(id=int(g(f, "idbloque")), nombre=g(f, "nombrearea"), cuit=g(f, "cuit"), razon=g(f, "razon_social"))
         for f in re.findall(r"<dt[ >].*?</dt>", x, re.S)]

# ---- 3 areas que quedan SIN CARGAR (mas los 2 borradores vacios que ya existen: Argenta y PAE)
OILSTONE, PAE = "30711732752", "30695542476"


def ultimas(cuit, n):
    c = sorted([a for a in areas if a["cuit"] == cuit and a["id"] not in ya], key=lambda a: a["nombre"])
    return c[-n:]


vacias = ultimas(OILSTONE, 2) + ultimas(PAE, 1)
ids_vacias = {a["id"] for a in vacias}

BIG = {"30546689979", "33515950899", "30678224010", "30711635382", "30695542476", "30592665472",
       "30695570933", "30523479241", "30569719344", "30526552659", "30719018463"}
DEC2 = {"equipos_perforacion", "set_fracturas", "desarrollo_recursos", "precio_gas", "precio_petroleo"}
ENT = {"pozos_perforados", "pozos_conectados", "fracturas", "pozos_abandonar", "act_pulling", "act_workover",
       "cap_proceso_petroleo", "cap_evac_petroleo"}
INV = ("inv_desarrollo", "inv_perforacion", "inv_terminacion", "inv_workover", "inv_pulling", "inv_ampl_plantas",
       "inv_plantas_nuevas", "inv_ductos_petroleo", "inv_ductos_gas", "inv_instal_produccion", "inv_estudios_gg", "inv_sismica")
GCONV = dict(gto_salarios=1.2, gto_pulling=.3, gto_workover=.2, gto_energia=.5, gto_quimicos=.3, gto_compresion=.4,
             gto_vehiculos=.25, gto_superficiarios=.1, gto_servidumbres=.08, gto_canon=.05, gto_tasas=.04, gto_otros=.05)


def gen(tipo, N, s, rng):
    ch = tipo == 2
    A = rng.randint(7, 13) if ch else rng.randint(5, 10)
    peak = rng.randint(2, 4)
    filas = []
    prev = cum = cap = 0
    des = rng.uniform(0.5, 3)
    for k in range(N):
        act = max(0.0, 1 - ((k - peak) / (A / 2 + 0.5)) ** 2) if k <= A else 0.0

        def u(a=0.85, b=1.15):
            return rng.uniform(a, b)

        base = 14 if ch else 3.2
        poz = int(round(s * base * act * u(.8, 1.2)))
        con = prev if k > 0 else int(round(s * base * 0.15))
        prev = poz
        cum += con
        equ = round(min(5.0, s * (1.2 if ch else 0.6) * act * u()), 2) if poz > 0 else 0
        sets = round(equ * 0.3, 2) if ch else 0
        frac = int(round(poz * rng.uniform(38, 52))) if ch else 0
        aban = int(round(rng.uniform(0, 2) * max(1, s))) if k >= N - 4 else 0
        pul = 0 if ch else int(round(s * 6 * u(.5, 1.3)))
        wo = 0 if ch else int(round(s * 3 * u(.4, 1.3)))
        cap = max(cap, int(round((s * (9000 if ch else 900) + cum * (450 if ch else 60)) / 50.0) * 50))
        capg = round(s * (2.5 if ch else .25) + cum * 0.01, 3)
        dec = math.exp(-0.10 * max(0, k - A))
        pet = round(cap * 0.82 * dec * 365 / 1e6, 4)
        gas = round(pet * (rng.uniform(25, 55) if ch else rng.uniform(60, 140)), 4)
        vp, vg = round(pet * 0.985, 4), round(gas * 0.92, 4)
        agua = round(pet * 0.159 * rng.uniform(0.5, 4), 4)
        des = min(100.0, des + poz * (0.28 if ch else 0.05) * u())
        pg = round(3.3 + 0.05 * k + rng.uniform(-.2, .2), 2)
        pp = round(60 + 0.8 * k + rng.uniform(-3, 3), 2)
        v = dict(pozos_perforados=poz, pozos_conectados=con, equipos_perforacion=equ, set_fracturas=sets, fracturas=frac,
                 pozos_abandonar=aban, cap_proceso_petroleo=cap, cap_proceso_gas=capg, cap_evac_petroleo=int(cap * 1.1),
                 cap_evac_gas=round(capg * 1.1, 3), prod_petroleo=pet, venta_petroleo=vp, prod_gas=gas, venta_gas=vg,
                 prod_agua=agua, desarrollo_recursos=round(des, 2), precio_gas=pg, precio_petroleo=pp)
        if ch:
            v.update(inv_desarrollo=poz * u(10.5, 13), inv_ampl_plantas=9 * s if k == 3 else 0,
                     inv_plantas_nuevas=22 * s if k in (1, 6) else 0, inv_ductos_petroleo=11 * s if k == 2 else 0,
                     inv_ductos_gas=8 * s if k == 2 else 0, inv_instal_produccion=poz * 0.9 + (1.5 if act > 0 else 0),
                     inv_estudios_gg=u(.4, 1.8) * s if k < 4 else 0, inv_sismica=2.5 * s if k == 0 else 0)
            opex = 3 + 2.4 * s + 0.05 * k * s
        else:
            v.update(act_pulling=pul, act_workover=wo, inv_perforacion=poz * 1.6 * u(), inv_terminacion=poz * 0.9 * u(),
                     inv_workover=wo * 0.4 * u(), inv_pulling=pul * 0.07 * u(), inv_ampl_plantas=1.1 * s if k == 3 else 0,
                     inv_plantas_nuevas=0, inv_ductos_petroleo=0, inv_ductos_gas=0.6 * s if k == 2 else 0,
                     inv_instal_produccion=0.7 * s * u(), inv_estudios_gg=0.1 * s if k < 2 else 0, inv_sismica=0,
                     abandono_usd=0.35 * s * u() if k >= N - 5 else 0)
            for kk, b in GCONV.items():
                v[kk] = b * s * (1 + 0.03 * k) * u()
            opex = sum(v[kk] for kk in GCONV)
        ing = vp * pp + vg * 0.0369 * pg
        inv = sum(v.get(c, 0) for c in INV)
        if ch:
            v["gto_fijo"] = opex
            v["gto_variable"] = ing * 0.055
            opex = opex + ing * 0.055
        reg, iib = 0.12 * ing, 0.035 * ing
        v.update(ingresos=ing, regalias=reg, iibb_otros=iib, iigg=max(0, 0.35 * (ing - reg - iib - opex - 0.12 * inv)))
        for c, val in list(v.items()):
            val = max(0, val)
            v[c] = int(round(val)) if c in ENT else round(val, 2) if c in DEC2 else round(val, 4)
        filas.append(v)
    return filas


class Cli:
    def __init__(self):
        self.cj = http.cookiejar.CookieJar()
        self.op = urllib.request.build_opener(urllib.request.ProxyHandler({}), urllib.request.HTTPCookieProcessor(self.cj))
        self.csrf = None

    def req(self, path, data=None, js=None):
        h, body = {}, None
        if self.csrf:
            h["RequestVerificationToken"] = self.csrf
        if js is not None:
            body = json.dumps(js).encode()
            h["Content-Type"] = "application/json"
            h["X-Requested-With"] = "XMLHttpRequest"
        elif data is not None:
            body = urllib.parse.urlencode(data).encode()
            h["Content-Type"] = "application/x-www-form-urlencoded"
        r = self.op.open(urllib.request.Request(B + path, body, h), timeout=120)
        raw = r.read()
        m = re.search(rb'<meta name="csrf" content="([^"]+)"', raw)
        if m:
            self.csrf = m.group(1).decode()
        return r.geturl(), raw

    def login(self, cuit):
        u, raw = self.req("/login")
        tok = re.search(rb'name="__RequestVerificationToken"[^>]*value="([^"]+)"', raw).group(1).decode()
        self.req("/login/desarrollo", data={"cuit": cuit, "rol": "1", "email": "prueba@example.com",
                                            "__RequestVerificationToken": tok})
        self.req("/Plan")


rng = random.Random(2026)
fallas, total = [], 0
por_emp = {}
for a in areas:
    por_emp.setdefault(a["cuit"], []).append(a)

for cuit, lst in por_emp.items():
    c = Cli()
    c.login(cuit)
    for a in sorted(lst, key=lambda a: a["nombre"]):
        if a["id"] in ya or a["id"] in ids_vacias or a["id"] not in tipos:
            continue
        try:
            tipo = tipos[a["id"]]
            N = rng.randint(9, 22)
            s = rng.uniform(1.2, 3.2) if tipo == 2 else rng.uniform(0.4, 1.6) * (1.6 if cuit in BIG else 0.8)
            u, _ = c.req("/Plan/Iniciar", data={"idBloque": str(a["id"]), "cuit": cuit})
            pid = re.search(r"/Plan/Editar/(\d+)", u).group(1)
            c.req(f"/Plan/Rango/{pid}", data={"desde": "2026", "hasta": str(2026 + N - 1), "paso": "0"})
            filas = gen(tipo, N, s, rng)
            cel = [{"anio": 2026 + k, "campo": cc, "valor": vv} for k, f in enumerate(filas) for cc, vv in f.items()]
            c.req(f"/Plan/Guardar/{pid}", js={"celdas": cel})
            u, raw = c.req(f"/Plan/Enviar/{pid}", data={})
            if "/Comprobante/" in u:
                total += 1
            else:
                fallas.append((a["nombre"], re.sub(r"<[^>]+>", " ", html.unescape(raw.decode("utf8", "ignore")))[:160]))
        except Exception as e:
            fallas.append((a["nombre"], str(e)[:200]))

print("ENVIADAS:", total)
print("SIN CARGAR a proposito:", "; ".join(f'{a["nombre"]} ({a["razon"]})' for a in vacias))
print("FALLAS:", len(fallas))
for f in fallas[:8]:
    print("  ", f)

