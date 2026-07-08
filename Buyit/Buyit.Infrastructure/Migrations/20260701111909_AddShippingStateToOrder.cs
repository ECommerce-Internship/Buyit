using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buyit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingStateToOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShippingState",
                table: "Orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShippingState",
                table: "Orders");
        }
    }
}
