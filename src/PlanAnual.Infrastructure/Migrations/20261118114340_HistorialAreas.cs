using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PlanAnual.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HistorialAreas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "historial_areas",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ciclo_id = table.Column<int>(type: "integer", nullable: false),
                    cuit = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    id_bloque = table.Column<int>(type: "integer", nullable: false),
                    fecha = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    usuario = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    campo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    anterior = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    nuevo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_historial_areas", x => x.id);
                    table.ForeignKey(
                        name: "fk_historial_areas_ciclos_ciclo_id",
                        column: x => x.ciclo_id,
                        principalTable: "ciclos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_historial_areas_ciclo_id_cuit_id_bloque_fecha",
                table: "historial_areas",
                columns: new[] { "ciclo_id", "cuit", "id_bloque", "fecha" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "historial_areas");
        }
    }
}
