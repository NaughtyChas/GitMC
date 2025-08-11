using GitMC.Services;

namespace GitMC.Utils;

/// <summary>
///     Service aggregator interface
/// </summary>
public interface IServiceAggregator
{
    IConfigurationService Configuration { get; }
    IGitService Git { get; }
    IOnboardingService Onboarding { get; }
    IDataStorageService DataStorage { get; }
    ILocalizationService Localization { get; }
    INbtService Nbt { get; }
    ISaveInitializationService SaveInitialization { get; }
    ISessionLockMonitorService SessionLockMonitor { get; }
    IOperationManager Operations { get; }
}

/// <summary>
///     Service aggregator implementation
/// </summary>
internal class ServiceAggregator : IServiceAggregator
{
    public ServiceAggregator(
        IConfigurationService configurationService,
        IGitService gitService,
        IOnboardingService onboardingService,
        IDataStorageService dataStorageService,
        ILocalizationService localizationService,
        INbtService nbtService,
    ISaveInitializationService saveInitializationService,
    ISessionLockMonitorService sessionLockMonitorService,
    IOperationManager operationManager)
    {
        Configuration = configurationService;
        Git = gitService;
        Onboarding = onboardingService;
        DataStorage = dataStorageService;
        Localization = localizationService;
        Nbt = nbtService;
        SaveInitialization = saveInitializationService;
        SessionLockMonitor = sessionLockMonitorService;
        Operations = operationManager;
    }

    public IConfigurationService Configuration { get; }
    public IGitService Git { get; }
    public IOnboardingService Onboarding { get; }
    public IDataStorageService DataStorage { get; }
    public ILocalizationService Localization { get; }
    public INbtService Nbt { get; }
    public ISaveInitializationService SaveInitialization { get; }
    public ISessionLockMonitorService SessionLockMonitor { get; }
    public IOperationManager Operations { get; }
}
