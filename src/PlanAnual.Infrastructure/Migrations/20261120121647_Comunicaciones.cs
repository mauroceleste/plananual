using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PlanAnual.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Comunicaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "comunicaciones",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ciclo_id = table.Column<int>(type: "integer", nullable: false),
                    cuit = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    razon_social = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    id_bloque = table.Column<int>(type: "integer", nullable: true),
                    nombre_area = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    fecha = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    asunto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    detalle = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    numero_gde = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    plazo_hasta = table.Column<DateOnly>(type: "date", nullable: true),
                    responde_a = table.Column<long>(type: "bigint", nullable: true),
                    visible_para_empresa = table.Column<bool>(type: "boolean", nullable: false),
                    usuario_id = table.Column<int>(type: "integer", nullable: false),
                    usuario = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    adjunto_nombre = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    adjunto_ruta = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    adjunto_tamano = table.Column<long>(type: "bigint", nullable: true),
                    adjunto_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    mail_enviado = table.Column<bool>(type: "boolean", nullable: false),
                    mail_destinatarios = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    mail_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comunicaciones", x => x.id);
                    table.ForeignKey(
                        name: "fk_comunicaciones_ciclos_ciclo_id",
                        column: x => x.ciclo_id,
                        principalTable: "ciclos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_comunicaciones_ciclo_id_cuit_fecha",
                table: "comunicaciones",
                columns: new[] { "ciclo_id", "cuit", "fecha" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "comunicaciones");
        }
    }
}
