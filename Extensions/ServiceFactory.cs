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
        // Create core services
        var configurationService = new ConfigurationService();
        var gitService = new GitService(configurationService);
        var dataStorageService = new DataStorageService();
        var localizationService = new LocalizationService();
        var nbtService = new NbtService();
        var manifestService = new ManifestService(gitService);
        var onboardingService = new OnboardingService(gitService, configurationService);
        var saveInitializationService = new SaveInitializationService(gitService, nbtService, dataStorageService, manifestService);

        // Create service aggregator
        return new ServiceAggregator(
            configurationService,
            gitService,
            onboardingService,
            dataStorageService,
            localizationService,
            nbtService,
            saveInitializationService
        );
    }
}
