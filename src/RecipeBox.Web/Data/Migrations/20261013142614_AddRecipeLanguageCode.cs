using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeBox.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipeLanguageCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LanguageCode",
                table: "Recipes",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "en");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LanguageCode",
                table: "Recipes");
        }
    }
}
