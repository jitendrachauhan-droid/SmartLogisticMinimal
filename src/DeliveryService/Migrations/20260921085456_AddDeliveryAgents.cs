using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryService.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeliveryAgents",
                columns: table => new
                {
                    AgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    IsAvailable = table.Column<bool>(type: "bit", nullable: false),
                    CurrentLatitude = table.Column<double>(type: "float", nullable: false),
                    CurrentLongitude = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryAgents", x => x.AgentId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryAgents_Email",
                table: "DeliveryAgents",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryAgents_IsAvailable_CurrentLatitude_CurrentLongitude",
                table: "DeliveryAgents",
                columns: new[] { "IsAvailable", "CurrentLatitude", "CurrentLongitude" });

            migrationBuilder.AddForeignKey(
                name: "FK_Deliveries_DeliveryAgents_AgentId",
                table: "Deliveries",
                column: "AgentId",
                principalTable: "DeliveryAgents",
                principalColumn: "AgentId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Deliveries_DeliveryAgents_AgentId",
                table: "Deliveries");

            migrationBuilder.DropTable(
                name: "DeliveryAgents");
        }
    }
}
