namespace AdagioMachineAgent.Services;

/// <summary>
/// Hosted background service that periodically prunes idle sessions.
/// Runs every 60 seconds regardless of the configured timeout value.
/// </summary>
public sealed class SessionExpiryService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(60);

    private readonly SessionService _sessionService;
    private readonly ILogger<SessionExpiryService> _logger;

    public SessionExpiryService(SessionService sessionService, ILogger<SessionExpiryService> logger)
    {
        _sessionService = sessionService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(ScanInterval, stoppingToken).ConfigureAwait(false);

            try
            {
                var expired = _sessionService.PruneExpiredSessions();
                if (expired.Count > 0)
                {
                    _logger.LogInformation(
                        "SessionExpiry: pruned {Count} idle session(s): {SessionIds}",
                        expired.Count,
                        string.Join(", ", expired));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SessionExpiry: error during session pruning scan.");
            }
        }
    }
}
