using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanD.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BrokerManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_broker_carriers_users_broker_user_id",
                schema: "app",
                table: "broker_carriers");

            migrationBuilder.DropIndex(
                name: "ix_broker_carriers_broker_user_id",
                schema: "app",
                table: "broker_carriers");

            migrationBuilder.DropColumn(
                name: "broker_user_id",
                schema: "app",
                table: "broker_carriers");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deactivated_at",
                schema: "app",
                table: "brokers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_sign_in_at",
                schema: "app",
                table: "brokers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                schema: "app",
                table: "broker_invites",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "display_name",
                schema: "app",
                table: "broker_invites",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "invited_by_id",
                schema: "app",
                table: "broker_invites",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_broker_invites_agency_id_email",
                schema: "app",
                table: "broker_invites",
                columns: new[] { "agency_id", "email" });

            migrationBuilder.CreateIndex(
                name: "ix_broker_invites_invited_by_id",
                schema: "app",
                table: "broker_invites",
                column: "invited_by_id");

            migrationBuilder.AddForeignKey(
                name: "fk_broker_carriers_brokers_broker_id",
                schema: "app",
                table: "broker_carriers",
                column: "broker_id",
                principalSchema: "app",
                principalTable: "brokers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_broker_invites_agencies_agency_id",
                schema: "app",
                table: "broker_invites",
                column: "agency_id",
                principalSchema: "app",
                principalTable: "agencies",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_broker_invites_brokers_invited_by_id",
                schema: "app",
                table: "broker_invites",
                column: "invited_by_id",
                principalSchema: "app",
                principalTable: "brokers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_broker_carriers_brokers_broker_id",
                schema: "app",
                table: "broker_carriers");

            migrationBuilder.DropForeignKey(
                name: "fk_broker_invites_agencies_agency_id",
                schema: "app",
                table: "broker_invites");

            migrationBuilder.DropForeignKey(
                name: "fk_broker_invites_brokers_invited_by_id",
                schema: "app",
                table: "broker_invites");

            migrationBuilder.DropIndex(
                name: "ix_broker_invites_agency_id_email",
                schema: "app",
                table: "broker_invites");

            migrationBuilder.DropIndex(
                name: "ix_broker_invites_invited_by_id",
                schema: "app",
                table: "broker_invites");

            migrationBuilder.DropColumn(
                name: "deactivated_at",
                schema: "app",
                table: "brokers");

            migrationBuilder.DropColumn(
                name: "last_sign_in_at",
                schema: "app",
                table: "brokers");

            migrationBuilder.DropColumn(
                name: "created_at",
                schema: "app",
                table: "broker_invites");

            migrationBuilder.DropColumn(
                name: "display_name",
                schema: "app",
                table: "broker_invites");

            migrationBuilder.DropColumn(
                name: "invited_by_id",
                schema: "app",
                table: "broker_invites");

            migrationBuilder.AddColumn<Guid>(
                name: "broker_user_id",
                schema: "app",
                table: "broker_carriers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_broker_carriers_broker_user_id",
                schema: "app",
                table: "broker_carriers",
                column: "broker_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_broker_carriers_users_broker_user_id",
                schema: "app",
                table: "broker_carriers",
                column: "broker_user_id",
                principalSchema: "app",
                principalTable: "brokers",
                principalColumn: "id");
        }
    }
}
