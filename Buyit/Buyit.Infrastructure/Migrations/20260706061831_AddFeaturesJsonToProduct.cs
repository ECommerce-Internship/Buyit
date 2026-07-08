using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buyit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFeaturesJsonToProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FeaturesJson",
                table: "Products",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FeaturesJson",
                table: "Products");
        }
    }
}
