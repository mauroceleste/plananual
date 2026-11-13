using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanAnual.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PeriodosMensualesYRestricciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_plan_valores",
                table: "plan_valores");

            migrationBuilder.AddColumn<string>(
                name: "conflictos_superficiarios",
                table: "presentaciones",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "equipos_contratos_firmes",
                table: "presentaciones",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "equipos_costos",
                table: "presentaciones",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "equipos_plazos",
                table: "presentaciones",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "mes",
                table: "plan_valores",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "pk_plan_valores",
                table: "plan_valores",
                columns: new[] { "presentacion_id", "anio", "mes", "campo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_plan_valores",
                table: "plan_valores");

            migrationBuilder.DropColumn(
                name: "conflictos_superficiarios",
                table: "presentaciones");

            migrationBuilder.DropColumn(
                name: "equipos_contratos_firmes",
                table: "presentaciones");

            migrationBuilder.DropColumn(
                name: "equipos_costos",
                table: "presentaciones");

            migrationBuilder.DropColumn(
                name: "equipos_plazos",
                table: "presentaciones");

            migrationBuilder.DropColumn(
                name: "mes",
                table: "plan_valores");

            migrationBuilder.AddPrimaryKey(
                name: "pk_plan_valores",
                table: "plan_valores",
                columns: new[] { "presentacion_id", "anio", "campo" });
        }
    }
}
