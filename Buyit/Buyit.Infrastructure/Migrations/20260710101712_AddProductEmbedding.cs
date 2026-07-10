using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Buyit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductEmbedding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TB-156: the "vector" column type only exists once the pgvector extension is enabled.
            // Idempotent and safe to run on every environment (local Docker, Neon); no-op if present.
            // Must run BEFORE the AddColumn below, or "type vector does not exist" is thrown.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            migrationBuilder.AddColumn<Vector>(
                name: "Embedding",
                table: "Products",
                type: "vector(768)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Embedding",
                table: "Products");
        }
    }
}
