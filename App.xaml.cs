using GitMC.Services;

namespace GitMC;

/// <summary>
///     Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App
{
    /// <summary>
    ///     Initializes the singleton application object.  This is the first line of authored code
    ///     executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();

        // Initialize services
        LocalizationService = new LocalizationService();

        // Set default language to English for development
        LocalizationService.SetLanguage("en-US");
    }

    /// <summary>
    ///     Gets the main window of the application.
    /// </summary>
    public static Window? MainWindow { get; private set; }

    /// <summary>
    ///     Gets the localization service for the application.
    /// </summary>
    public ILocalizationService LocalizationService { get; }

    /// <summary>
    ///     Invoked when the application is launched normally by the end user.  Other entry points
    ///     will be used such as when the application is launched to open a specific file.
    /// </summary>
    /// <param name="e">Details about the launch request and process.</param>
    protected override void OnLaunched(LaunchActivatedEventArgs e)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
