namespace PaidTraffic.Api.Operations;

public sealed class EvaluationWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<EvaluationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Evaluation:Enabled", true)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(configuration.GetValue("Evaluation:IntervalSeconds", 60)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TrafficWorkflow>().RunAsync(stoppingToken);
            }
            catch (WorkflowBusyException) { logger.LogDebug("Evaluation skipped because another workflow is active"); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Evaluation failed; durable intents will be recovered on the next tick"); }
        }
    }
}
