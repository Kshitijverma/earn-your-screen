using Makaretu.Dns;
using TypedPond.Core;

namespace TypedPond.Service;

/// <summary>
/// Advertises the TypedPond HTTP endpoint over mDNS/DNS-SD as
/// "_typedpond._tcp.local" so the Android companion app (via Android NSD) can
/// discover the laptop's address automatically instead of relying on a manually
/// configured IP.
///
/// The advertised port is the configured local HTTP port (default 8787).
/// </summary>
public class MdnsAdvertiser : BackgroundService
{
    // Must match the service type the Android app searches for
    // (LaptopDiscovery: "_typedpond._tcp.").
    private const string ServiceType = "_typedpond._tcp";
    private const string InstanceName = "TypedPond";

    private readonly Config _config;
    private readonly ILogger<MdnsAdvertiser> _logger;

    private MulticastService? _mdns;
    private ServiceDiscovery? _serviceDiscovery;

    public MdnsAdvertiser(Config config, ILogger<MdnsAdvertiser> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var profile = new ServiceProfile(
                InstanceName,
                ServiceType,
                (ushort)_config.LocalHttpPort);

            _mdns = new MulticastService();
            _serviceDiscovery = new ServiceDiscovery(_mdns);
            _serviceDiscovery.Advertise(profile);

            _mdns.Start();
            _logger.LogInformation(
                "mDNS advertising {ServiceType} on port {Port}.",
                ServiceType,
                _config.LocalHttpPort);
        }
        catch (Exception ex)
        {
            // Advertising is a convenience (manual IP is the fallback), so a
            // failure here must not take down the service.
            _logger.LogWarning(ex, "Failed to start mDNS advertising; manual IP will be required.");
        }

        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _serviceDiscovery?.Dispose();
            _mdns?.Stop();
            _mdns?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error while stopping mDNS advertising.");
        }

        return base.StopAsync(cancellationToken);
    }
}
