using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PlanD.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "agencies",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agencies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "broker_invites",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broker_invites", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "brokers",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    photo_url = table.Column<string>(type: "text", nullable: true),
                    public_slug = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brokers", x => x.id);
                    table.ForeignKey(
                        name: "fk_brokers_agencies_agency_id",
                        column: x => x.agency_id,
                        principalSchema: "app",
                        principalTable: "agencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "broker_carriers",
                schema: "app",
                columns: table => new
                {
                    broker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_organization = table.Column<string>(type: "text", nullable: false),
                    broker_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broker_carriers", x => new { x.broker_id, x.parent_organization });
                    table.ForeignKey(
                        name: "fk_broker_carriers_users_broker_user_id",
                        column: x => x.broker_user_id,
                        principalSchema: "app",
                        principalTable: "brokers",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "broker_claims",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broker_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_broker_claims_brokers_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "brokers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "broker_logins",
                schema: "app",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broker_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_broker_logins_brokers_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "brokers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "broker_tokens",
                schema: "app",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broker_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_broker_tokens_brokers_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "brokers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "clients",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    broker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    first_name = table.Column<string>(type: "text", nullable: false),
                    last_name = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    zip = table.Column<string>(type: "text", nullable: true),
                    county_code = table.Column<string>(type: "text", nullable: true),
                    birth_month = table.Column<int>(type: "integer", nullable: true),
                    birth_year = table.Column<int>(type: "integer", nullable: true),
                    medicare_status = table.Column<string>(type: "text", nullable: true),
                    current_plan = table.Column<string>(type: "text", nullable: true),
                    extra_help = table.Column<string>(type: "text", nullable: false),
                    extra_help_level = table.Column<int>(type: "integer", nullable: false),
                    uses_mail_order = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    seen_by_broker_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clients", x => x.id);
                    table.ForeignKey(
                        name: "fk_clients_agencies_agency_id",
                        column: x => x.agency_id,
                        principalSchema: "app",
                        principalTable: "agencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_clients_users_broker_id",
                        column: x => x.broker_id,
                        principalSchema: "app",
                        principalTable: "brokers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_drugs",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rxcui = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    units_per_dose = table.Column<decimal>(type: "numeric", nullable: false),
                    doses_per_day = table.Column<decimal>(type: "numeric", nullable: false),
                    days_supply = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_drugs", x => x.id);
                    table.ForeignKey(
                        name: "fk_client_drugs_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "app",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "client_notes",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_client_notes_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "app",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "client_pharmacies",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    npi = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "text", nullable: true),
                    zip = table.Column<string>(type: "text", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_pharmacies", x => x.id);
                    table.ForeignKey(
                        name: "fk_client_pharmacies_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "app",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "consents",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    text_version = table.Column<string>(type: "text", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ip_address = table.Column<string>(type: "text", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consents", x => x.id);
                    table.ForeignKey(
                        name: "fk_consents_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "app",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "patient_links",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "text", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_patient_links_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "app",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quote_snapshots",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    broker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    plan_year = table.Column<int>(type: "integer", nullable: false),
                    spuf_release_id = table.Column<int>(type: "integer", nullable: false),
                    landscape_release_id = table.Column<int>(type: "integer", nullable: true),
                    request_json = table.Column<string>(type: "jsonb", nullable: false),
                    result_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quote_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_quote_snapshots_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "app",
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_broker_carriers_broker_user_id",
                schema: "app",
                table: "broker_carriers",
                column: "broker_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_broker_claims_user_id",
                schema: "app",
                table: "broker_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_broker_invites_token_hash",
                schema: "app",
                table: "broker_invites",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_broker_logins_user_id",
                schema: "app",
                table: "broker_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "app",
                table: "brokers",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "ix_brokers_agency_id",
                schema: "app",
                table: "brokers",
                column: "agency_id");

            migrationBuilder.CreateIndex(
                name: "ix_brokers_public_slug",
                schema: "app",
                table: "brokers",
                column: "public_slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "app",
                table: "brokers",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_client_drugs_client_id",
                schema: "app",
                table: "client_drugs",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_notes_client_id",
                schema: "app",
                table: "client_notes",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_pharmacies_client_id",
                schema: "app",
                table: "client_pharmacies",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_clients_agency_id_broker_id_status",
                schema: "app",
                table: "clients",
                columns: new[] { "agency_id", "broker_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_clients_broker_id",
                schema: "app",
                table: "clients",
                column: "broker_id");

            migrationBuilder.CreateIndex(
                name: "ix_consents_client_id",
                schema: "app",
                table: "consents",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_patient_links_client_id",
                schema: "app",
                table: "patient_links",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_patient_links_token_hash",
                schema: "app",
                table: "patient_links",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_quote_snapshots_client_id",
                schema: "app",
                table: "quote_snapshots",
                column: "client_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "broker_carriers",
                schema: "app");

            migrationBuilder.DropTable(
                name: "broker_claims",
                schema: "app");

            migrationBuilder.DropTable(
                name: "broker_invites",
                schema: "app");

            migrationBuilder.DropTable(
                name: "broker_logins",
                schema: "app");

            migrationBuilder.DropTable(
                name: "broker_tokens",
                schema: "app");

            migrationBuilder.DropTable(
                name: "client_drugs",
                schema: "app");

            migrationBuilder.DropTable(
                name: "client_notes",
                schema: "app");

            migrationBuilder.DropTable(
                name: "client_pharmacies",
                schema: "app");

            migrationBuilder.DropTable(
                name: "consents",
                schema: "app");

            migrationBuilder.DropTable(
                name: "patient_links",
                schema: "app");

            migrationBuilder.DropTable(
                name: "quote_snapshots",
                schema: "app");

            migrationBuilder.DropTable(
                name: "clients",
                schema: "app");

            migrationBuilder.DropTable(
                name: "brokers",
                schema: "app");

            migrationBuilder.DropTable(
                name: "agencies",
                schema: "app");
        }
    }
}
