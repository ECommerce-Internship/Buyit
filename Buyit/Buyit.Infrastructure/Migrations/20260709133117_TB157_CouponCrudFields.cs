using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buyit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TB157_CouponCrudFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscountPercentage",
                table: "Coupons");

            migrationBuilder.AddColumn<int>(
                name: "DiscountType",
                table: "Coupons",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountValue",
                table: "Coupons",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "UsageCount",
                table: "Coupons",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UsageLimit",
                table: "Coupons",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscountType",
                table: "Coupons");

            migrationBuilder.DropColumn(
                name: "DiscountValue",
                table: "Coupons");

            migrationBuilder.DropColumn(
                name: "UsageCount",
                table: "Coupons");

            migrationBuilder.DropColumn(
                name: "UsageLimit",
                table: "Coupons");

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercentage",
                table: "Coupons",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }
    }
}
