using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileSystemManager.Migrations
{
    public partial class AddIsFolderColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFolder",
                table: "FileMetadata",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFolder",
                table: "FileMetadata");
        }
    }
}
