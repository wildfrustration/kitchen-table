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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "broker_invites",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_broker_invites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "brokers",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    PhotoUrl = table.Column<string>(type: "text", nullable: true),
                    PublicSlug = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brokers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_brokers_agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalSchema: "app",
                        principalTable: "agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "broker_carriers",
                schema: "app",
                columns: table => new
                {
                    BrokerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentOrganization = table.Column<string>(type: "text", nullable: false),
                    BrokerUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_broker_carriers", x => new { x.BrokerId, x.ParentOrganization });
                    table.ForeignKey(
                        name: "FK_broker_carriers_brokers_BrokerUserId",
                        column: x => x.BrokerUserId,
                        principalSchema: "app",
                        principalTable: "brokers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "broker_claims",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_broker_claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_broker_claims_brokers_UserId",
                        column: x => x.UserId,
                        principalSchema: "app",
                        principalTable: "brokers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "broker_logins",
                schema: "app",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_broker_logins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_broker_logins_brokers_UserId",
                        column: x => x.UserId,
                        principalSchema: "app",
                        principalTable: "brokers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "broker_tokens",
                schema: "app",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_broker_tokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_broker_tokens_brokers_UserId",
                        column: x => x.UserId,
                        principalSchema: "app",
                        principalTable: "brokers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "clients",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BrokerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    FirstName = table.Column<string>(type: "text", nullable: false),
                    LastName = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    Zip = table.Column<string>(type: "text", nullable: true),
                    CountyCode = table.Column<string>(type: "text", nullable: true),
                    BirthMonth = table.Column<int>(type: "integer", nullable: true),
                    BirthYear = table.Column<int>(type: "integer", nullable: true),
                    MedicareStatus = table.Column<string>(type: "text", nullable: true),
                    CurrentPlan = table.Column<string>(type: "text", nullable: true),
                    ExtraHelp = table.Column<string>(type: "text", nullable: false),
                    ExtraHelpLevel = table.Column<int>(type: "integer", nullable: false),
                    UsesMailOrder = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SeenByBrokerAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_clients_agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalSchema: "app",
                        principalTable: "agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clients_brokers_BrokerId",
                        column: x => x.BrokerId,
                        principalSchema: "app",
                        principalTable: "brokers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_drugs",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rxcui = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    UnitsPerDose = table.Column<decimal>(type: "numeric", nullable: false),
                    DosesPerDay = table.Column<decimal>(type: "numeric", nullable: false),
                    DaysSupply = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_drugs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_client_drugs_clients_ClientId",
                        column: x => x.ClientId,
                        principalSchema: "app",
                        principalTable: "clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "client_notes",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_notes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_client_notes_clients_ClientId",
                        column: x => x.ClientId,
                        principalSchema: "app",
                        principalTable: "clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "client_pharmacies",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Npi = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: true),
                    Zip = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_pharmacies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_client_pharmacies_clients_ClientId",
                        column: x => x.ClientId,
                        principalSchema: "app",
                        principalTable: "clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "consents",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    TextVersion = table.Column<string>(type: "text", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_consents_clients_ClientId",
                        column: x => x.ClientId,
                        principalSchema: "app",
                        principalTable: "clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "patient_links",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "text", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patient_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_patient_links_clients_ClientId",
                        column: x => x.ClientId,
                        principalSchema: "app",
                        principalTable: "clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quote_snapshots",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    BrokerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PlanYear = table.Column<int>(type: "integer", nullable: false),
                    SpufReleaseId = table.Column<int>(type: "integer", nullable: false),
                    LandscapeReleaseId = table.Column<int>(type: "integer", nullable: true),
                    RequestJson = table.Column<string>(type: "jsonb", nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quote_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quote_snapshots_clients_ClientId",
                        column: x => x.ClientId,
                        principalSchema: "app",
                        principalTable: "clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_broker_carriers_BrokerUserId",
                schema: "app",
                table: "broker_carriers",
                column: "BrokerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_broker_claims_UserId",
                schema: "app",
                table: "broker_claims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_broker_invites_TokenHash",
                schema: "app",
                table: "broker_invites",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_broker_logins_UserId",
                schema: "app",
                table: "broker_logins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "app",
                table: "brokers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_brokers_AgencyId",
                schema: "app",
                table: "brokers",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_brokers_PublicSlug",
                schema: "app",
                table: "brokers",
                column: "PublicSlug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "app",
                table: "brokers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_drugs_ClientId",
                schema: "app",
                table: "client_drugs",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_client_notes_ClientId",
                schema: "app",
                table: "client_notes",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_client_pharmacies_ClientId",
                schema: "app",
                table: "client_pharmacies",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_clients_AgencyId_BrokerId_Status",
                schema: "app",
                table: "clients",
                columns: new[] { "AgencyId", "BrokerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_clients_BrokerId",
                schema: "app",
                table: "clients",
                column: "BrokerId");

            migrationBuilder.CreateIndex(
                name: "IX_consents_ClientId",
                schema: "app",
                table: "consents",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_links_ClientId",
                schema: "app",
                table: "patient_links",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_links_TokenHash",
                schema: "app",
                table: "patient_links",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quote_snapshots_ClientId",
                schema: "app",
                table: "quote_snapshots",
                column: "ClientId");
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
