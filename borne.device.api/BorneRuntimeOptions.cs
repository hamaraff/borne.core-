namespace borne.device.api;

public sealed record BorneRuntimeOptions(
    string BorneId,
    string? CentralApiBaseUrl,
    string? CentralApiToken,
    bool SimulationEnabled,
    int SimulationIntervalSeconds,
    decimal SimulationIncrement);

public sealed class LocalStoreStartup(borne.local.infrastructure.SqliteBorneLocalStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => store.InitializeAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
