using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeBox.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipeSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecipeShares",
                columns: table => new
                {
                    RecipeId = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeShares", x => new { x.RecipeId, x.OwnerId });
                    table.ForeignKey(
                        name: "FK_RecipeShares_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecipeShares_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeShares_OwnerId",
                table: "RecipeShares",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeShares_RecipeId",
                table: "RecipeShares",
                column: "RecipeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecipeShares_TokenHash",
                table: "RecipeShares",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecipeShares");
        }
    }
}
