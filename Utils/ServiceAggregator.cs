using GitMC.Services;

namespace GitMC.Utils
{
    /// <summary>
    /// Service aggregator to reduce service dependency injection complexity
    /// </summary>
    public interface IServiceAggregator
    {
        IConfigurationService Configuration { get; }
        IGitService Git { get; }
        IOnboardingService Onboarding { get; }
        IDataStorageService DataStorage { get; }
        ILocalizationService Localization { get; }
        INbtService Nbt { get; }
    }

    /// <summary>
    /// Implementation of service aggregator
    /// </summary>
    public class ServiceAggregator : IServiceAggregator
    {
        public IConfigurationService Configuration { get; }
        public IGitService Git { get; }
        public IOnboardingService Onboarding { get; }
        public IDataStorageService DataStorage { get; }
        public ILocalizationService Localization { get; }
        public INbtService Nbt { get; }

        public ServiceAggregator(
            IConfigurationService configurationService,
            IGitService gitService,
            IOnboardingService onboardingService,
            IDataStorageService dataStorageService,
            ILocalizationService localizationService,
            INbtService nbtService)
        {
            Configuration = configurationService;
            Git = gitService;
            Onboarding = onboardingService;
            DataStorage = dataStorageService;
            Localization = localizationService;
            Nbt = nbtService;
        }
    }
}
