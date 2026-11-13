using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PlanAnual.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SolicitudesRectificacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "solicitudes_rectificacion",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ciclo_id = table.Column<int>(type: "integer", nullable: false),
                    cuit = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    razon_social = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    id_bloque = table.Column<int>(type: "integer", nullable: false),
                    nombre_area = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    presentacion_id = table.Column<long>(type: "bigint", nullable: false),
                    motivo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    solicitada_por = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    solicitada_email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    solicitada_en = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    resuelta_por = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    resuelta_en = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    habilitada_hasta = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solicitudes_rectificacion", x => x.id);
                    table.ForeignKey(
                        name: "fk_solicitudes_rectificacion_presentaciones_presentacion_id",
                        column: x => x.presentacion_id,
                        principalTable: "presentaciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_solicitudes_rectificacion_ciclo_id_cuit_id_bloque",
                table: "solicitudes_rectificacion",
                columns: new[] { "ciclo_id", "cuit", "id_bloque" });

            migrationBuilder.CreateIndex(
                name: "ix_solicitudes_rectificacion_estado",
                table: "solicitudes_rectificacion",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_solicitudes_rectificacion_presentacion_id",
                table: "solicitudes_rectificacion",
                column: "presentacion_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "solicitudes_rectificacion");
        }
    }
}
