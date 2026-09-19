using Microsoft.EntityFrameworkCore;

namespace PaidTraffic.Api.Persistence;

public sealed class TrafficDbContext(DbContextOptions<TrafficDbContext> options) : DbContext(options)
{
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<Evaluation> Evaluations => Set<Evaluation>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<OperationalAction> Actions => Set<OperationalAction>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Campaign>().Property(x => x.Id).HasMaxLength(64);
        model.Entity<Campaign>().Property(x => x.Name).HasMaxLength(200);
        model.Entity<Campaign>().Property(x => x.Status).HasConversion<string>();
        model.Entity<Policy>().Property(x => x.Mode).HasConversion<string>();
        model.Entity<Incident>().Property(x => x.Mode).HasConversion<string>();
        model.Entity<Incident>().Property(x => x.Status).HasConversion<string>();
        model.Entity<Policy>().HasIndex(x => x.CampaignId).IsUnique();
        model.Entity<Policy>().HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Evaluation>().HasOne<Policy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Evaluation>().HasIndex(x => new { x.CampaignId, x.PolicyId, x.WindowStart, x.WindowEnd }).IsUnique();
        model.Entity<Incident>().HasOne<Evaluation>().WithMany().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Incident>().HasIndex(x => x.EvaluationId).IsUnique();
        model.Entity<Incident>().HasIndex(x => x.CampaignId).IsUnique()
            .HasFilter("\"Status\" IN ('PendingApproval', 'Executing', 'ExecutionFailed')");
        model.Entity<Incident>().HasIndex(x => new { x.CreatedAt, x.Id });
        model.Entity<OperationalAction>().HasOne<Incident>().WithMany().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<OperationalAction>().HasIndex(x => x.IncidentId).IsUnique();
        model.Entity<AuditEntry>().HasIndex(x => new { x.IncidentId, x.Id });
        model.Entity<AuditEntry>().HasOne<Incident>().WithMany().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<AuditEntry>().HasOne<Evaluation>().WithMany().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<AuditEntry>().HasOne<OperationalAction>().WithMany().HasForeignKey(x => x.ActionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Campaign>().ToTable(t => t.HasCheckConstraint("CK_Campaign_Performance", "\"Spend\" >= 0 AND \"Conversions\" >= 0 AND \"Revenue\" >= 0"));
        model.Entity<Policy>().ToTable(t => t.HasCheckConstraint("CK_Policy_Thresholds", "\"MaxSpendWithoutConversion\" > 0 AND (\"MinimumRoas\" IS NULL OR \"MinimumRoas\" > 0) AND \"MinimumSpendForRoas\" >= 0"));
        model.Entity<Evaluation>().ToTable(t => t.HasCheckConstraint("CK_Evaluation_Window", "\"WindowStart\" < \"WindowEnd\""));
        foreach (var entity in model.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
                {
                    property.SetPrecision(20);
                    property.SetScale(6);
                }
                if (property.Name == "Currency") property.SetMaxLength(3);
            }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (ChangeTracker.Entries<AuditEntry>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Audit entries are append-only.");
        return base.SaveChangesAsync(cancellationToken);
    }
}
