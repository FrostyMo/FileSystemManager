using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileSystemManager.Migrations
{
    public partial class AddIsDeletedAndDeletedPathColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeletedPath",
                table: "FileMetadata",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "FileMetadata",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedPath",
                table: "FileMetadata");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "FileMetadata");
        }
    }
}
