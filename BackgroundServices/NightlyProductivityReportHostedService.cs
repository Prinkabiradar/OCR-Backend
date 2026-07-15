using OCR_BACKEND.Services;

namespace OCR_BACKEND.BackgroundServices
{
    public class NightlyProductivityReportHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<NightlyProductivityReportHostedService> _logger;

        public NightlyProductivityReportHostedService(
            IServiceScopeFactory scopeFactory,
            IConfiguration config,
            ILogger<NightlyProductivityReportHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _config = config;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_config.GetValue("ProductivityReport:Enabled", true))
            {
                _logger.LogInformation("Nightly productivity report scheduler is disabled.");
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.Now;
                var nextRun = GetNextRun(now);
                var delay = nextRun - now;

                _logger.LogInformation("Next productivity report scheduled at {NextRun}", nextRun);
                await Task.Delay(delay, stoppingToken);

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var reportService = scope.ServiceProvider.GetRequiredService<IProductivityReportService>();
                    await reportService.SendDailyReportAsync(DateOnly.FromDateTime(nextRun.DateTime), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Nightly productivity report failed.");
                }
            }
        }

        private DateTimeOffset GetNextRun(DateTimeOffset now)
        {
            var runAt = TimeOnly.TryParse(_config["ProductivityReport:RunAt"], out var configuredRunAt)
                ? configuredRunAt
                : new TimeOnly(22, 0);

            var nextRun = new DateTimeOffset(
                now.Year,
                now.Month,
                now.Day,
                runAt.Hour,
                runAt.Minute,
                0,
                now.Offset);

            return nextRun <= now ? nextRun.AddDays(1) : nextRun;
        }
    }
}
