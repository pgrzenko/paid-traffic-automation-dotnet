using Microsoft.EntityFrameworkCore;
using PaidTraffic.Api.Persistence;

namespace PaidTraffic.Api.Operations;

public sealed class WorkflowBusyException() : Exception("Another workflow is running. Retry later.");

// Session lock survives intermediate commits. A dedicated open connection must be retained until disposal.
public sealed class WorkflowLock(TrafficDbContext db) : IAsyncDisposable
{
    private const long Key = 731840219;
    private bool held;

    public async Task AcquireAsync(CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT pg_try_advisory_lock({Key})";
        held = (bool)(await command.ExecuteScalarAsync(ct))!;
        if (!held)
        {
            await db.Database.CloseConnectionAsync();
            throw new WorkflowBusyException();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!held) return;
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT pg_advisory_unlock({Key})";
            await command.ExecuteScalarAsync(CancellationToken.None);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
