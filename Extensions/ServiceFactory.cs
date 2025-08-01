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

    /// <summary>
    ///     Gets the singleton service aggregator instance
    /// </summary>
    public static IServiceAggregator Services => _serviceAggregator.Value;

    /// <summary>
    ///     Gets the Minecraft analyzer service
    /// </summary>
    public static IMinecraftAnalyzerService MinecraftAnalyzer => _minecraftAnalyzer.Value;

    private static IServiceAggregator CreateServiceAggregator()
    {
        // Create core services
        var configurationService = new ConfigurationService();
        var gitService = new GitService();
        var dataStorageService = new DataStorageService();
        var localizationService = new LocalizationService();
        var nbtService = new NbtService();
        var onboardingService = new OnboardingService(gitService, configurationService);
        var saveInitializationService = new SaveInitializationService(gitService, nbtService, dataStorageService);

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
