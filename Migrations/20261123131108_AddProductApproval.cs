using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend_stockpilot.Migrations
{
    /// <inheritdoc />
    public partial class AddProductApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Approval",
                table: "Products",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotRequired");

            migrationBuilder.AddColumn<bool>(
                name: "AvailableInPos",
                table: "Products",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "PendingOpeningStock",
                table: "Products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "Products",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestedByStoreId",
                table: "Products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "Products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewedById",
                table: "Products",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_Approval",
                table: "Products",
                column: "Approval",
                filter: "\"Approval\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_Products_RequestedByStoreId",
                table: "Products",
                column: "RequestedByStoreId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_PendingOpeningStock",
                table: "Products",
                sql: "\"PendingOpeningStock\" >= 0");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Stores_RequestedByStoreId",
                table: "Products",
                column: "RequestedByStoreId",
                principalTable: "Stores",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Stores_RequestedByStoreId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_Approval",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_RequestedByStoreId",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_PendingOpeningStock",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Approval",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "AvailableInPos",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PendingOpeningStock",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "RequestedByStoreId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ReviewedById",
                table: "Products");
        }
    }
}
