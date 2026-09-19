using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PaidTraffic.Api.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Campaigns",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Status = table.Column<string>(type: "text", nullable: false),
                Spend = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                Conversions = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                Revenue = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                TransientFailuresRemaining = table.Column<int>(type: "integer", nullable: false),
                PermanentFailure = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Campaigns", x => x.Id);
                table.CheckConstraint("CK_Campaign_Performance", "\"Spend\" >= 0 AND \"Conversions\" >= 0 AND \"Revenue\" >= 0");
            });

        migrationBuilder.CreateTable(
            name: "Policies",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CampaignId = table.Column<string>(type: "character varying(64)", nullable: false),
                MaxSpendWithoutConversion = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                MinimumRoas = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                MinimumSpendForRoas = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Mode = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Policies", x => x.Id);
                table.CheckConstraint("CK_Policy_Thresholds", "\"MaxSpendWithoutConversion\" > 0 AND (\"MinimumRoas\" IS NULL OR \"MinimumRoas\" > 0) AND \"MinimumSpendForRoas\" >= 0");
                table.ForeignKey(
                    name: "FK_Policies_Campaigns_CampaignId",
                    column: x => x.CampaignId,
                    principalTable: "Campaigns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Evaluations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CampaignId = table.Column<string>(type: "text", nullable: false),
                PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                WindowStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                WindowEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Spend = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                Conversions = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                Revenue = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Violated = table.Column<bool>(type: "boolean", nullable: false),
                Reasons = table.Column<string[]>(type: "text[]", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Evaluations", x => x.Id);
                table.CheckConstraint("CK_Evaluation_Window", "\"WindowStart\" < \"WindowEnd\"");
                table.ForeignKey(
                    name: "FK_Evaluations_Policies_PolicyId",
                    column: x => x.PolicyId,
                    principalTable: "Policies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Incidents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                CampaignId = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<string>(type: "text", nullable: false),
                Mode = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Incidents", x => x.Id);
                table.ForeignKey(
                    name: "FK_Incidents_Evaluations_EvaluationId",
                    column: x => x.EvaluationId,
                    principalTable: "Evaluations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Actions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                Attempts = table.Column<int>(type: "integer", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastError = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Actions", x => x.Id);
                table.ForeignKey(
                    name: "FK_Actions_Incidents_IncidentId",
                    column: x => x.IncidentId,
                    principalTable: "Incidents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Audit",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                IncidentId = table.Column<Guid>(type: "uuid", nullable: true),
                EvaluationId = table.Column<Guid>(type: "uuid", nullable: true),
                ActionId = table.Column<Guid>(type: "uuid", nullable: true),
                Event = table.Column<string>(type: "text", nullable: false),
                Actor = table.Column<string>(type: "text", nullable: false),
                Detail = table.Column<string>(type: "text", nullable: false),
                TraceId = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Audit", x => x.Id);
                table.ForeignKey(
                    name: "FK_Audit_Actions_ActionId",
                    column: x => x.ActionId,
                    principalTable: "Actions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Audit_Evaluations_EvaluationId",
                    column: x => x.EvaluationId,
                    principalTable: "Evaluations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Audit_Incidents_IncidentId",
                    column: x => x.IncidentId,
                    principalTable: "Incidents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Actions_IncidentId",
            table: "Actions",
            column: "IncidentId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Audit_ActionId",
            table: "Audit",
            column: "ActionId");

        migrationBuilder.CreateIndex(
            name: "IX_Audit_EvaluationId",
            table: "Audit",
            column: "EvaluationId");

        migrationBuilder.CreateIndex(
            name: "IX_Audit_IncidentId_Id",
            table: "Audit",
            columns: new[] { "IncidentId", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_Evaluations_CampaignId_PolicyId_WindowStart_WindowEnd",
            table: "Evaluations",
            columns: new[] { "CampaignId", "PolicyId", "WindowStart", "WindowEnd" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Evaluations_PolicyId",
            table: "Evaluations",
            column: "PolicyId");

        migrationBuilder.CreateIndex(
            name: "IX_Incidents_CampaignId",
            table: "Incidents",
            column: "CampaignId",
            unique: true,
            filter: "\"Status\" IN ('PendingApproval', 'Executing', 'ExecutionFailed')");

        migrationBuilder.CreateIndex(
            name: "IX_Incidents_CreatedAt_Id",
            table: "Incidents",
            columns: new[] { "CreatedAt", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_Incidents_EvaluationId",
            table: "Incidents",
            column: "EvaluationId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Policies_CampaignId",
            table: "Policies",
            column: "CampaignId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Audit");

        migrationBuilder.DropTable(
            name: "Actions");

        migrationBuilder.DropTable(
            name: "Incidents");

        migrationBuilder.DropTable(
            name: "Evaluations");

        migrationBuilder.DropTable(
            name: "Policies");

        migrationBuilder.DropTable(
            name: "Campaigns");
    }
}
