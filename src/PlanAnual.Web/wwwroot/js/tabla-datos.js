/* Tablas de consulta: buscador, orden por columna, paginación y exportación (copiar, CSV, Excel, imprimir).
   Sin dependencias. Se activa en toda <table class="datos">. Se pueden excluir columnas con data-sin-exportar en el <th>. */
(function () {
    'use strict';

    var TAMANOS = [10, 25, 50, 100, 0];   // 0 = todos

    function sinTildes(t) { return (t || '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase(); }
    function texto(celda) { return (celda.innerText || celda.textContent || '').replace(/\s+/g, ' ').trim(); }

    /* Valor comparable de una celda: fecha dd/mm/aaaa, número (1.234,56 / 12,5 / 12.5) o texto. */
    function clave(t) {
        var f = /^(\d{2})\/(\d{2})\/(\d{4})/.exec(t);
        if (f) return { n: Date.UTC(+f[3], +f[2] - 1, +f[1]) };
        var limpio = t.replace(/[%\s]|km²|km2/gi, '');
        if (/^-?[\d.]+(,\d+)?$/.test(limpio) || /^-?\d+(\.\d+)?$/.test(limpio)) {
            var num = limpio.indexOf(',') >= 0 ? limpio.replace(/\./g, '').replace(',', '.') : limpio;
            var v = parseFloat(num);
            if (!isNaN(v)) return { n: v };
        }
        return { s: sinTildes(t) };
    }
    function comparar(a, b) {
        if (a.n !== undefined && b.n !== undefined) return a.n - b.n;
        if (a.n !== undefined) return -1;
        if (b.n !== undefined) return 1;
        return a.s < b.s ? -1 : a.s > b.s ? 1 : 0;
    }

    function descargar(nombre, tipo, contenido) {
        var blob = new Blob([contenido], { type: tipo });
        var a = document.createElement('a');
        a.href = URL.createObjectURL(blob); a.download = nombre;
        document.body.appendChild(a); a.click();
        setTimeout(function () { URL.revokeObjectURL(a.href); a.remove(); }, 500);
    }
    function esc(t) { return t.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;'); }

    function iniciar(tabla) {
        var thead = tabla.tHead, tbody = tabla.tBodies[0];
        if (!thead || !tbody) return;
        var cab = Array.prototype.slice.call(thead.rows[0].cells);
        var filas = Array.prototype.slice.call(tbody.rows).map(function (tr) {
            return { tr: tr, textos: Array.prototype.map.call(tr.cells, texto) };
        });
        filas.forEach(function (f) { f.busqueda = sinTildes(f.textos.join(' ')); });

        var estado = { q: '', tam: parseInt(tabla.getAttribute('data-tam') || '25', 10), pagina: 0, col: -1, asc: true, visibles: filas.slice() };
        var titulo = tabla.getAttribute('data-titulo') || document.title.split(' - ')[0];

        // ---- barra superior
        var barra = document.createElement('div');
        barra.className = 'tabla-barra d-flex flex-wrap justify-content-between align-items-center gap-2 mb-2';
        barra.innerHTML =
            '<div class="d-flex flex-wrap align-items-center gap-3">' +
              '<label class="d-flex align-items-center gap-2 mb-0">Mostrar ' +
                '<select class="form-select form-select-sm tabla-tam" style="width:auto">' +
                TAMANOS.map(function (n) { return '<option value="' + n + '"' + (n === estado.tam ? ' selected' : '') + '>' + (n === 0 ? 'Todos' : n) + '</option>'; }).join('') +
                '</select> registros</label>' +
              '<label class="d-flex align-items-center gap-2 mb-0">Buscar: <input type="search" class="form-control form-control-sm tabla-buscar" style="width:16rem" placeholder="Escribí para filtrar"></label>' +
            '</div>' +
            '<div class="btn-group btn-group-sm tabla-botones no-imprimir" role="group">' +
              '<button type="button" class="btn btn-outline-secondary" data-acc="copiar">Copiar</button>' +
              '<button type="button" class="btn btn-outline-secondary" data-acc="csv">CSV</button>' +
              '<button type="button" class="btn btn-outline-secondary" data-acc="excel">Excel</button>' +
              '<button type="button" class="btn btn-outline-secondary" data-acc="imprimir">Imprimir</button>' +
            '</div>';
        var pie = document.createElement('div');
        pie.className = 'tabla-pie d-flex flex-wrap justify-content-between align-items-center gap-2 mt-2 no-imprimir';
        pie.innerHTML = '<div class="tabla-info text-muted small"></div><nav><ul class="pagination pagination-sm mb-0"></ul></nav>';

        var contenedor = tabla.closest('.table-responsive') || tabla;
        contenedor.parentNode.insertBefore(barra, contenedor);
        contenedor.parentNode.insertBefore(pie, contenedor.nextSibling);
        var vacio = document.createElement('tr');
        vacio.className = 'tabla-vacia'; vacio.innerHTML = '<td colspan="' + cab.length + '" class="text-center text-muted py-3">No se encontraron registros.</td>';

        // ---- orden
        cab.forEach(function (th, i) {
            if (th.hasAttribute('data-sin-orden')) return;
            th.classList.add('ordenable'); th.tabIndex = 0; th.setAttribute('role', 'button');
            var alternar = function () {
                estado.asc = estado.col === i ? !estado.asc : true; estado.col = i; estado.pagina = 0; dibujar();
            };
            th.addEventListener('click', alternar);
            th.addEventListener('keydown', function (e) { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); alternar(); } });
        });

        function filtrar() {
            var q = sinTildes(estado.q).split(/\s+/).filter(Boolean);
            estado.visibles = filas.filter(function (f) { return q.every(function (p) { return f.busqueda.indexOf(p) >= 0; }); });
            if (estado.col >= 0) {
                var i = estado.col;
                estado.visibles.forEach(function (f) { if (!f.k) f.k = []; if (!f.k[i]) f.k[i] = clave(f.textos[i] || ''); });
                estado.visibles.sort(function (a, b) { var r = comparar(a.k[i], b.k[i]); return estado.asc ? r : -r; });
            }
        }

        function dibujar() {
            filtrar();
            var total = estado.visibles.length, tam = estado.tam === 0 ? total || 1 : estado.tam;
            var paginas = Math.max(1, Math.ceil(total / tam));
            if (estado.pagina >= paginas) estado.pagina = paginas - 1;
            var desde = estado.pagina * tam, hasta = Math.min(total, desde + tam);

            while (tbody.firstChild) tbody.removeChild(tbody.firstChild);
            if (total === 0) tbody.appendChild(vacio);
            estado.visibles.slice(desde, hasta).forEach(function (f) { tbody.appendChild(f.tr); });

            cab.forEach(function (th, i) {
                th.classList.remove('orden-asc', 'orden-desc');
                th.setAttribute('aria-sort', 'none');
                if (i === estado.col) { th.classList.add(estado.asc ? 'orden-asc' : 'orden-desc'); th.setAttribute('aria-sort', estado.asc ? 'ascending' : 'descending'); }
            });

            pie.querySelector('.tabla-info').textContent = total === 0 ? 'Sin registros'
                : 'Mostrando registros del ' + (desde + 1) + ' al ' + hasta + ' de un total de ' + total +
                  (total !== filas.length ? ' (filtrados de ' + filas.length + ')' : '') + ' registros';

            var ul = pie.querySelector('ul'); ul.innerHTML = '';
            function item(txt, p, desactivado, activo) {
                var li = document.createElement('li'); li.className = 'page-item' + (desactivado ? ' disabled' : '') + (activo ? ' active' : '');
                var a = document.createElement('a'); a.className = 'page-link'; a.href = '#'; a.textContent = txt;
                a.addEventListener('click', function (e) { e.preventDefault(); if (!desactivado) { estado.pagina = p; dibujar(); } });
                li.appendChild(a); ul.appendChild(li);
            }
            if (paginas > 1) {
                item('Anterior', estado.pagina - 1, estado.pagina === 0);
                var mostrar = [];
                for (var p = 0; p < paginas; p++) if (p === 0 || p === paginas - 1 || Math.abs(p - estado.pagina) <= 2) mostrar.push(p);
                var previo = -1;
                mostrar.forEach(function (p) {
                    if (p - previo > 1) item('…', p, true);
                    item(String(p + 1), p, false, p === estado.pagina); previo = p;
                });
                item('Siguiente', estado.pagina + 1, estado.pagina === paginas - 1);
            }
        }

        // ---- exportación (siempre todas las filas que pasan el filtro, con todas las páginas)
        function columnasExportables() { return cab.map(function (th, i) { return th.hasAttribute('data-sin-exportar') ? -1 : i; }).filter(function (i) { return i >= 0; }); }
        function matriz() {
            var cols = columnasExportables();
            var m = [cols.map(function (i) { return cab[i].textContent.replace(/\s+/g, ' ').trim(); })];   // el encabezado con su capitalización original
            estado.visibles.forEach(function (f) { m.push(cols.map(function (i) { return f.textos[i] || ''; })); });
            if (tabla.tFoot) Array.prototype.forEach.call(tabla.tFoot.rows, function (tr) {
                m.push(cols.map(function (i) { return tr.cells[i] ? texto(tr.cells[i]) : ''; }));
            });
            return m;
        }
        var nombreArchivo = (tabla.getAttribute('data-archivo') || titulo).toLowerCase().replace(/[^a-z0-9]+/g, '_') + '_' + new Date().toISOString().slice(0, 10);

        barra.querySelector('.tabla-botones').addEventListener('click', function (e) {
            var b = e.target.closest('button'); if (!b) return;
            var acc = b.getAttribute('data-acc'), m = matriz();
            if (acc === 'copiar') {
                var tsv = m.map(function (f) { return f.join('\t'); }).join('\n');
                (navigator.clipboard ? navigator.clipboard.writeText(tsv) : Promise.reject()).then(function () { aviso(b, 'Copiado (' + (m.length - 1) + ' filas)'); },
                    function () { var ta = document.createElement('textarea'); ta.value = tsv; document.body.appendChild(ta); ta.select(); document.execCommand('copy'); ta.remove(); aviso(b, 'Copiado'); });
            } else if (acc === 'csv') {
                var csv = m.map(function (f) { return f.map(function (c) { return '"' + c.replace(/"/g, '""') + '"'; }).join(';'); }).join('\r\n');
                descargar(nombreArchivo + '.csv', 'text/csv;charset=utf-8', '﻿' + csv);
            } else if (acc === 'excel') {
                // XML Spreadsheet 2003: Excel lo abre directamente, sin librerías.
                var xml = '<?xml version="1.0" encoding="UTF-8"?><?mso-application progid="Excel.Sheet"?>' +
                    '<Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet" xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">' +
                    '<Styles><Style ss:ID="h"><Font ss:Bold="1" ss:Color="#FCF2DC"/><Interior ss:Color="#00374C" ss:Pattern="Solid"/></Style></Styles>' +
                    '<Worksheet ss:Name="' + esc(titulo.slice(0, 31).replace(/[\\\/?*\[\]:]/g, ' ')) + '"><Table>' +
                    m.map(function (f, r) { return '<Row>' + f.map(function (c) { return '<Cell' + (r === 0 ? ' ss:StyleID="h"' : '') + '><Data ss:Type="String">' + esc(c) + '</Data></Cell>'; }).join('') + '</Row>'; }).join('') +
                    '</Table></Worksheet></Workbook>';
                descargar(nombreArchivo + '.xls', 'application/vnd.ms-excel', xml);
            } else if (acc === 'imprimir') {
                var previo = { tam: estado.tam, pagina: estado.pagina };
                estado.tam = 0; estado.pagina = 0; dibujar();
                document.body.classList.add('imprimiendo');
                var fin = function () { document.body.classList.remove('imprimiendo'); estado.tam = previo.tam; estado.pagina = previo.pagina; dibujar(); window.removeEventListener('afterprint', fin); };
                window.addEventListener('afterprint', fin);
                window.print();
            }
        });
        function aviso(boton, msg) { var t = boton.textContent; boton.textContent = msg; setTimeout(function () { boton.textContent = t; }, 1500); }

        barra.querySelector('.tabla-tam').addEventListener('change', function (e) { estado.tam = parseInt(e.target.value, 10); estado.pagina = 0; dibujar(); });
        var espera;
        barra.querySelector('.tabla-buscar').addEventListener('input', function (e) {
            clearTimeout(espera); var v = e.target.value; espera = setTimeout(function () { estado.q = v; estado.pagina = 0; dibujar(); }, 120);
        });
        dibujar();
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('table.datos').forEach(iniciar);
    });
})();
