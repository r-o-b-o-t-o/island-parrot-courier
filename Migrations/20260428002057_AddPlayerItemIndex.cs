using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IslandParrotCourier.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerItemIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ItemIndex",
                table: "Players",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ItemIndex",
                table: "Players");
        }
    }
}
