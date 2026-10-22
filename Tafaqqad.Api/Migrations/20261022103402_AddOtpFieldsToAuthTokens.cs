using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tafaqqad.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOtpFieldsToAuthTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "AuthTokens",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "FailedAttempts",
                table: "AuthTokens",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "AuthTokens");

            migrationBuilder.DropColumn(
                name: "FailedAttempts",
                table: "AuthTokens");
        }
    }
}
