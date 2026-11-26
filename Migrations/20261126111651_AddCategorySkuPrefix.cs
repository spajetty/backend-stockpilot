using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend_stockpilot.Migrations
{
    /// <inheritdoc />
    public partial class AddCategorySkuPrefix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SkuPrefix",
                table: "Categories",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_SkuPrefix",
                table: "Categories",
                column: "SkuPrefix",
                unique: true,
                filter: "\"SkuPrefix\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Categories_SkuPrefix",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "SkuPrefix",
                table: "Categories");
        }
    }
}
