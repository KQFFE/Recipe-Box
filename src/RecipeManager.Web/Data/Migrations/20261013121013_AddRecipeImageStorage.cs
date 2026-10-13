using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeManager.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipeImageStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StoredImageContentType",
                table: "Recipes",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoredImageKey",
                table: "Recipes",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_StoredImageKey",
                table: "Recipes",
                column: "StoredImageKey",
                unique: true,
                filter: "\"StoredImageKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Recipes_StoredImageKey",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "StoredImageContentType",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "StoredImageKey",
                table: "Recipes");
        }
    }
}
