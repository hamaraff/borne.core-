using borne.business.Services;
using borne.core.IInfra;

namespace borne.device.api;

/// <summary>Optional development-only source of cumulative meter readings.</summary>
public sealed class ConsumptionSimulator(
    BorneRuntimeOptions options,
    IBorneLocalStore store,
    BorneOperationService operations,
    ILogger<ConsumptionSimulator> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.SimulationIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var session = await store.GetSessionAsync(stoppingToken);
                if (session is null || session.IsPaused) continue;

                decimal nextCumulative = session.LastCumulativeQuantity + options.SimulationIncrement;
                await operations.RecordCumulativeReadingAsync(nextCumulative, cancellationToken: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (InvalidOperationException)
            {
                logger.LogWarning("The Borne simulator could not add a reading because the current session or tariff configuration is not valid.");
            }
        }
    }
}
