using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PaidTraffic.Api.Persistence.Migrations;

[DbContext(typeof(TrafficDbContext))]
[Migration("20260919105000_AppendOnlyAudit")]
public sealed class AppendOnlyAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION prevent_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            RAISE EXCEPTION 'Audit entries are append-only';
        END;
        $$;
        CREATE TRIGGER audit_no_update_delete BEFORE UPDATE OR DELETE ON "Audit"
            FOR EACH ROW EXECUTE FUNCTION prevent_audit_mutation();
        CREATE TRIGGER audit_no_truncate BEFORE TRUNCATE ON "Audit"
            FOR EACH STATEMENT EXECUTE FUNCTION prevent_audit_mutation();
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TRIGGER audit_no_truncate ON "Audit";
        DROP TRIGGER audit_no_update_delete ON "Audit";
        DROP FUNCTION prevent_audit_mutation();
        """);
}
