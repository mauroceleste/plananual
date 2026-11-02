using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PlanAnual.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CreacionInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bloques_tipo_actividad",
                columns: table => new
                {
                    id_bloque = table.Column<int>(type: "integer", nullable: false),
                    nombre_area = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    nombre_bloque = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    tipo_plan = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bloques_tipo_actividad", x => x.id_bloque);
                });

            migrationBuilder.CreateTable(
                name: "ciclos",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    anio = table.Column<int>(type: "integer", nullable: false),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    fecha_limite = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_prorroga = table.Column<DateOnly>(type: "date", nullable: true),
                    abierto = table.Column<bool>(type: "boolean", nullable: false),
                    nota_gde = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ciclos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "presentaciones",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ciclo_id = table.Column<int>(type: "integer", nullable: false),
                    cuit = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    razon_social = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    id_bloque = table.Column<int>(type: "integer", nullable: false),
                    nombre_area = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    nombre_bloque = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    tipo_plan = table.Column<int>(type: "integer", nullable: false),
                    anio_desde = table.Column<int>(type: "integer", nullable: false),
                    anio_hasta = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    usuario_id = table.Column<int>(type: "integer", nullable: false),
                    usuario_nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    usuario_email = table.Column<string>(type: "text", nullable: false),
                    enviada_por = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    asistida_por = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    creada_en = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    modificada_en = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    enviada_en = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    numero_comprobante = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    huella_envio = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    observaciones = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_presentaciones", x => x.id);
                    table.ForeignKey(
                        name: "fk_presentaciones_ciclos_ciclo_id",
                        column: x => x.ciclo_id,
                        principalTable: "ciclos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "anexos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    presentacion_id = table.Column<long>(type: "bigint", nullable: false),
                    tipo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    nombre_archivo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ruta_almacenada = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    tamano = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    subido_en = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anexos", x => x.id);
                    table.ForeignKey(
                        name: "fk_anexos_presentaciones_presentacion_id",
                        column: x => x.presentacion_id,
                        principalTable: "presentaciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "envios_mail",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    presentacion_id = table.Column<long>(type: "bigint", nullable: false),
                    destinatarios = table.Column<string>(type: "text", nullable: false),
                    asunto = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    enviado = table.Column<bool>(type: "boolean", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    fecha = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_envios_mail", x => x.id);
                    table.ForeignKey(
                        name: "fk_envios_mail_presentaciones_presentacion_id",
                        column: x => x.presentacion_id,
                        principalTable: "presentaciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plan_valores",
                columns: table => new
                {
                    presentacion_id = table.Column<long>(type: "bigint", nullable: false),
                    anio = table.Column<int>(type: "integer", nullable: false),
                    campo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    valor = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_valores", x => new { x.presentacion_id, x.anio, x.campo });
                    table.ForeignKey(
                        name: "fk_plan_valores_presentaciones_presentacion_id",
                        column: x => x.presentacion_id,
                        principalTable: "presentaciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_anexos_presentacion_id",
                table: "anexos",
                column: "presentacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_ciclos_anio",
                table: "ciclos",
                column: "anio",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_envios_mail_presentacion_id",
                table: "envios_mail",
                column: "presentacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_plan_valores_campo",
                table: "plan_valores",
                column: "campo");

            migrationBuilder.CreateIndex(
                name: "ix_presentaciones_ciclo_id_cuit_id_bloque_version",
                table: "presentaciones",
                columns: new[] { "ciclo_id", "cuit", "id_bloque", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_presentaciones_numero_comprobante",
                table: "presentaciones",
                column: "numero_comprobante",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anexos");

            migrationBuilder.DropTable(
                name: "bloques_tipo_actividad");

            migrationBuilder.DropTable(
                name: "envios_mail");

            migrationBuilder.DropTable(
                name: "plan_valores");

            migrationBuilder.DropTable(
                name: "presentaciones");

            migrationBuilder.DropTable(
                name: "ciclos");
        }
    }
}
