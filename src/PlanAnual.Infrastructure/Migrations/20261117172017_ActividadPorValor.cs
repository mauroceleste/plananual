using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanAnual.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ActividadPorValor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_plan_valores",
                table: "plan_valores");

            migrationBuilder.AddColumn<int>(
                name: "actividad",
                table: "plan_valores",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Lo que ya estaba cargado pertenece al tipo de plan de su presentación (hasta ahora había uno solo por área).
            migrationBuilder.Sql(
                "UPDATE plan_valores SET actividad = (SELECT tipo_plan FROM presentaciones WHERE presentaciones.id = plan_valores.presentacion_id);");

            migrationBuilder.AddPrimaryKey(
                name: "pk_plan_valores",
                table: "plan_valores",
                columns: new[] { "presentacion_id", "actividad", "anio", "mes", "campo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_plan_valores",
                table: "plan_valores");

            migrationBuilder.DropColumn(
                name: "actividad",
                table: "plan_valores");

            migrationBuilder.AddPrimaryKey(
                name: "pk_plan_valores",
                table: "plan_valores",
                columns: new[] { "presentacion_id", "anio", "mes", "campo" });
        }
    }
}
