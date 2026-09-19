using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using PaidTraffic.Api.Integration;
using PaidTraffic.Api.Operations;
using PaidTraffic.Api.Persistence;
using PaidTraffic.Domain;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaidTraffic.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder("postgres:17-alpine").Build();
    public Task InitializeAsync() => Container.StartAsync();
    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}

public sealed class FixedClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 9, 19, 12, 30, 0, TimeSpan.Zero);
}

public sealed class CompletionFailure : SaveChangesInterceptor
{
    public bool FailNextCompletion { get; set; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (FailNextCompletion && data.Context!.ChangeTracker.Entries<Incident>().Any(e => e.Entity.Status == IncidentStatus.Executed))
        {
            FailNextCompletion = false;
            throw new DbUpdateException("Injected persistence failure after provider success.");
        }
        return ValueTask.FromResult(result);
    }
}

public sealed class AppFactory(string connection, CompletionFailure failure, bool demo = true) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Traffic"] = connection,
            ["Demo:Enabled"] = demo.ToString(),
            ["Security:ApiKey"] = "integration-test-key-only",
            ["Evaluation:Enabled"] = "false"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider, FixedClock>();
            services.AddDbContextFactory<TrafficDbContext>(options => options.AddInterceptors(failure));
        });
    }
}

public sealed class WorkflowTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private AppFactory app = null!;
    private HttpClient client = null!;
    private string connection = "";
    private readonly CompletionFailure failure = new();

    public async Task InitializeAsync()
    {
        var database = "test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(postgres.Container.GetConnectionString());
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
        await create.ExecuteNonQueryAsync();
        connection = new NpgsqlConnectionStringBuilder(postgres.Container.GetConnectionString()) { Database = database }.ConnectionString;
        app = new AppFactory(connection, failure);
        client = app.CreateClient();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrafficDbContext>();
        await db.Database.MigrateAsync();
        await DemoSeed.InitializeAsync(db, default);
    }

    public async Task DisposeAsync() { client.Dispose(); await app.DisposeAsync(); }

    private async Task<TrafficDbContext> DbAsync() => await app.Services.GetRequiredService<IDbContextFactory<TrafficDbContext>>().CreateDbContextAsync();
    private async Task RunAsync() => (await client.PostAsync("/api/evaluations/run", null)).EnsureSuccessStatusCode();

    [Fact]
    public async Task Automatic_workflow_is_durable_audited_and_idempotent()
    {
        await RunAsync();
        await RunAsync();
        await using var db = await DbAsync();
        Assert.Equal(5, await db.Evaluations.CountAsync());
        Assert.Equal(4, await db.Incidents.CountAsync());
        Assert.Equal(4, await db.Actions.CountAsync());
        Assert.Equal(CampaignStatus.Paused, (await db.Campaigns.FindAsync("1001"))!.Status);
        Assert.Equal(CampaignStatus.Enabled, (await db.Campaigns.FindAsync("1003"))!.Status);
        var incident = await db.Incidents.SingleAsync(i => i.CampaignId == "1001");
        Assert.Equal(IncidentStatus.Executed, incident.Status);
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/incidents/{incident.Id}");
        Assert.Equal(4, detail.GetProperty("audit").GetArrayLength());
        Assert.Contains(await db.Audit.ToListAsync(), a => a.IncidentId == incident.Id && a.Event == "PauseCompleted");
    }

    [Fact]
    public async Task Human_approval_is_required_and_cannot_execute_twice()
    {
        await RunAsync();
        await using var db = await DbAsync();
        var incident = await db.Incidents.SingleAsync(i => i.CampaignId == "1002");
        Assert.Equal(IncidentStatus.PendingApproval, incident.Status);
        Assert.Equal(CampaignStatus.Enabled, (await db.Campaigns.FindAsync("1002"))!.Status);
        (await client.PostAsync($"/api/incidents/{incident.Id}/approve", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/incidents/{incident.Id}/approve", null)).StatusCode);
        db.ChangeTracker.Clear();
        Assert.Equal(IncidentStatus.Executed, (await db.Incidents.FindAsync(incident.Id))!.Status);
        Assert.Equal(1, (await db.Actions.SingleAsync(a => a.IncidentId == incident.Id)).Attempts);
    }

    [Fact]
    public async Task Rejection_preserves_campaign_and_prevents_same_window_reopening()
    {
        await RunAsync();
        await using var db = await DbAsync();
        var incident = await db.Incidents.SingleAsync(i => i.CampaignId == "1002");
        (await client.PostAsync($"/api/incidents/{incident.Id}/reject", null)).EnsureSuccessStatusCode();
        await RunAsync();
        Assert.Equal(CampaignStatus.Enabled, (await db.Campaigns.FindAsync("1002"))!.Status);
        Assert.Equal(1, await db.Incidents.CountAsync(i => i.CampaignId == "1002"));
        Assert.Equal(0, (await db.Actions.SingleAsync(a => a.IncidentId == incident.Id)).Attempts);
    }

    [Fact]
    public async Task Transient_provider_error_retries_with_audit_for_each_attempt()
    {
        await RunAsync();
        await using var db = await DbAsync();
        var incident = await db.Incidents.SingleAsync(i => i.CampaignId == "1005");
        Assert.Equal(IncidentStatus.Executed, incident.Status);
        Assert.Equal(2, (await db.Actions.SingleAsync(a => a.IncidentId == incident.Id)).Attempts);
        Assert.Equal(2, await db.Audit.CountAsync(a => a.IncidentId == incident.Id && a.Event == "PauseAttempted"));
    }

    [Fact]
    public async Task Permanent_failure_is_not_retried_and_operator_can_retry_after_repair()
    {
        await using var db = await DbAsync();
        await db.Campaigns.Where(c => c.Id == "1001").ExecuteUpdateAsync(s => s.SetProperty(c => c.PermanentFailure, true));
        await RunAsync();
        var incident = await db.Incidents.SingleAsync(i => i.CampaignId == "1001");
        Assert.Equal(IncidentStatus.ExecutionFailed, incident.Status);
        Assert.Equal(1, (await db.Actions.SingleAsync(a => a.IncidentId == incident.Id)).Attempts);
        await db.Campaigns.Where(c => c.Id == "1001").ExecuteUpdateAsync(s => s.SetProperty(c => c.PermanentFailure, false));
        (await client.PostAsync($"/api/incidents/{incident.Id}/retry", null)).EnsureSuccessStatusCode();
        db.ChangeTracker.Clear();
        Assert.Equal(IncidentStatus.Executed, (await db.Incidents.FindAsync(incident.Id))!.Status);
    }

    [Fact]
    public async Task Retries_are_bounded_and_failed_intents_do_not_retry_each_tick()
    {
        await using var db = await DbAsync();
        await db.Campaigns.Where(c => c.Id == "1001").ExecuteUpdateAsync(s => s.SetProperty(c => c.TransientFailuresRemaining, 20));
        await RunAsync();
        await RunAsync();
        var incident = await db.Incidents.SingleAsync(i => i.CampaignId == "1001");
        Assert.Equal(IncidentStatus.ExecutionFailed, incident.Status);
        Assert.Equal(3, (await db.Actions.SingleAsync(a => a.IncidentId == incident.Id)).Attempts);
    }

    [Fact]
    public async Task Provider_success_followed_by_database_failure_recovers_after_host_restart()
    {
        failure.FailNextCompletion = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsync("/api/evaluations/run", null)).StatusCode);
        await using (var db = await DbAsync())
        {
            Assert.Equal(CampaignStatus.Paused, (await db.Campaigns.FindAsync("1001"))!.Status);
            Assert.Equal(IncidentStatus.Executing, (await db.Incidents.SingleAsync(i => i.CampaignId == "1001")).Status);
        }
        client.Dispose();
        await app.DisposeAsync();
        app = new AppFactory(connection, failure);
        client = app.CreateClient();
        await RunAsync();
        await using var recovered = await DbAsync();
        var incident = await recovered.Incidents.SingleAsync(i => i.CampaignId == "1001");
        Assert.Equal(IncidentStatus.Executed, incident.Status);
        Assert.Contains(await recovered.Audit.ToListAsync(), a => a.IncidentId == incident.Id && a.Detail == "AlreadyPaused");
    }

    [Fact]
    public async Task Overlapping_host_is_rejected_by_database_lock()
    {
        await using var db = await DbAsync();
        await using var gate = new WorkflowLock(db);
        await gate.AcquireAsync(default);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/evaluations/run", null)).StatusCode);
        Assert.Equal(0, await db.Evaluations.CountAsync());
    }

    [Fact]
    public async Task Audit_cannot_be_changed_even_through_raw_sql()
    {
        await RunAsync();
        await using var db = await DbAsync();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"Audit\" SET \"Detail\" = 'tampered'"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM \"Audit\""));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("TRUNCATE \"Audit\""));
    }

    [Fact]
    public async Task Policy_validation_creation_and_duplicate_conflict()
    {
        var policy = new { campaignId = "1007", maxSpendWithoutConversion = 50, minimumRoas = 1.5, minimumSpendForRoas = 20, currency = "USD", mode = "RequireApproval" };
        var response = await client.PostAsJsonAsync("/api/policies", policy);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        (await client.GetAsync(response.Headers.Location)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/policies", policy)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/policies", new { campaignId = "1007", maxSpendWithoutConversion = -1, currency = "USD", mode = "Automatic" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/incidents/{Guid.Empty}")).StatusCode);
        (await client.GetAsync("/health")).EnsureSuccessStatusCode();
        (await client.GetAsync("/openapi/v1.json")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Non_demo_api_requires_key_for_reads_and_mutations()
    {
        await using var secureApp = new AppFactory(connection, new CompletionFailure(), demo: false);
        using var secureClient = secureApp.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await secureClient.GetAsync("/api/campaigns")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await secureClient.PostAsync("/api/evaluations/run", null)).StatusCode);
        secureClient.DefaultRequestHeaders.Add("X-Api-Key", "integration-test-key-only");
        (await secureClient.GetAsync("/api/campaigns")).EnsureSuccessStatusCode();
    }
}
