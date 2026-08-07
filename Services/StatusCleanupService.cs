namespace AeroChat.Services;

public class StatusCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(30);

    public StatusCleanupService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var data = scope.ServiceProvider.GetRequiredService<DataService>();
                var removed = data.CleanupExpiredStatuses();
                if (removed > 0)
                    Console.WriteLine($"[StatusCleanup] Estados vencidos eliminados: {removed}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StatusCleanup] Error: {ex.Message}");
            }
            await Task.Delay(_interval, stoppingToken);
        }
    }
}
