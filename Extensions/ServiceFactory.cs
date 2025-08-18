using GitMC.Services;
using GitMC.Utils;

namespace GitMC.Extensions;

/// <summary>
///     Factory for creating and managing application services
/// </summary>
public static class ServiceFactory
{
    private static readonly Lazy<IServiceAggregator> _serviceAggregator = new(CreateServiceAggregator);

    private static readonly Lazy<IMinecraftAnalyzerService> _minecraftAnalyzer =
        new(() => new MinecraftAnalyzerService(Services.Nbt));

    private static readonly Lazy<IGitHubAppsService> _gitHubAppsService =
        new(() => new GitHubAppsService());

    private static readonly Lazy<IManifestService> _manifestService =
        new(() => new ManifestService(Services.Git));

    /// <summary>
    ///     Gets the singleton service aggregator instance
    /// </summary>
    public static IServiceAggregator Services => _serviceAggregator.Value;

    /// <summary>
    ///     Gets the logging service for convenient access
    /// </summary>
    public static ILoggingService Logger => Services.Logging;

    /// <summary>
    ///     Gets the Minecraft analyzer service
    /// </summary>
    public static IMinecraftAnalyzerService MinecraftAnalyzer => _minecraftAnalyzer.Value;

    /// <summary>
    ///     Gets the GitHub Apps service
    /// </summary>
    public static IGitHubAppsService GitHubApps => _gitHubAppsService.Value;

    /// <summary>
    ///     Gets the Manifest service
    /// </summary>
    public static IManifestService Manifest => _manifestService.Value;

    private static IServiceAggregator CreateServiceAggregator()
    {
        // Create logging service first (other services may need it)
        var loggingService = new LoggingService(enableFileLogging: true, enableConsoleLogging: true);
        
        // Create core services
        var configurationService = new ConfigurationService();
        var gitService = new GitService(configurationService, loggingService);
        var dataStorageService = new DataStorageService(loggingService);
        var localizationService = new LocalizationService();
        var nbtService = new NbtService();
        var manifestService = new ManifestService(gitService);
        var onboardingService = new OnboardingService(gitService, configurationService);
        var saveInitializationService = new SaveInitializationService(gitService, nbtService, dataStorageService, manifestService, loggingService);
        var sessionLockMonitorService = new SessionLockMonitorService();
        var operationManager = new OperationManager();

        // Create service aggregator
        return new ServiceAggregator(
            loggingService,
            configurationService,
            gitService,
            onboardingService,
            dataStorageService,
            localizationService,
            nbtService,
            saveInitializationService,
            sessionLockMonitorService,
            operationManager
        );
    }
}
