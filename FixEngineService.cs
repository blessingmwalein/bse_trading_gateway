using QuickFix;
using QuickFix.Transport;
using QuickFix.Store;
using Microsoft.Extensions.Hosting;

public class FixEngineService : BackgroundService
{
    private readonly BseFixApplication _application;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FixEngineService> _logger;
    private SocketInitiator? _initiator;

    public FixEngineService(
        BseFixApplication application,
        IConfiguration configuration,
        ILogger<FixEngineService> logger)
    {
        _application = application;
        _configuration = configuration;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("Starting FIX Engine...");
                string cfgPath = _configuration["FixEngine:ConfigPath"] ?? "client.cfg";
                
                SessionSettings settings = new SessionSettings(cfgPath);
                IMessageStoreFactory storeFactory = new MemoryStoreFactory();
                // ILogFactory logFactory = new ScreenLogFactory(settings); // ScreenLog might interfere with Web API logging
                _initiator = new SocketInitiator(_application, storeFactory, settings, (QuickFix.Logger.ILogFactory?)null, new DefaultMessageFactory());
                _initiator.Start();

                _logger.LogInformation("FIX Engine started.");

                while (!stoppingToken.IsCancellationRequested)
                {
                    Thread.Sleep(1000);
                }

                _logger.LogInformation("Stopping FIX Engine...");
                _initiator.Stop();
                _logger.LogInformation("FIX Engine stopped.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in FIX Engine Background Service");
            }
        }, stoppingToken);
    }
}
