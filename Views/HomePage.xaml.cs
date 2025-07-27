using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using GitMC.ViewModels;
using GitMC.Services;
using GitMC.Models;
using Windows.Storage.Pickers;
using WinRT.Interop;
using System.IO;

namespace GitMC.Views
{
    public sealed partial class HomePage : Page
    {
        public HomePageViewModel ViewModel { get; }
        
        private readonly ObservableCollection<MinecraftSave> _managedSaves;

        public HomePage()
        {
            this.InitializeComponent();
            ViewModel = new HomePageViewModel(new NbtService());
            DataContext = ViewModel;
            _managedSaves = new ObservableCollection<MinecraftSave>();
            
            UpdateWelcomeMessage();
            UpdateUI();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            UpdateWelcomeMessage();
        }

        private void UpdateWelcomeMessage()
        {
            var hour = DateTime.Now.Hour;
            var message = hour switch
            {
                < 12 => "Good morning, Crafter! ☀️",
                < 18 => "Good afternoon, Miner! ⛏️",
                _ => "Good evening, Builder! 🌙"
            };
            WelcomeTextBlock.Text = message;
        }

        private void UpdateUI()
        {
            var hasSaves = _managedSaves.Count > 0;
            SavesGrid.Visibility = hasSaves ? Visibility.Visible : Visibility.Collapsed;
            EmptyStatePanel.Visibility = hasSaves ? Visibility.Collapsed : Visibility.Visible;
            
            ManagedCountTextBlock.Text = _managedSaves.Count.ToString();
        }

        private async void AddSaveButton_Click(object sender, RoutedEventArgs e)
        {
            LoadingPanel.Visibility = Visibility.Visible;
            LoadingProgressRing.IsActive = true;
            
            try
            {
                var folderPicker = new FolderPicker();
                folderPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                folderPicker.FileTypeFilter.Add("*");

                if (App.MainWindow != null)
                {
                    var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
                    InitializeWithWindow.Initialize(folderPicker, hwnd);
                }

                var folder = await folderPicker.PickSingleFolderAsync();
                if (folder != null)
                {
                    var save = await AnalyzeSaveFolder(folder.Path);
                    if (save != null)
                    {
                        _managedSaves.Add(save);
                        AddSaveCard(save);

                        // Add to navigation
                        if (App.MainWindow is MainWindow mainWindow)
                        {
                            mainWindow.AddSaveToNavigation(save.Name, save.Path);
                        }

                        UpdateUI();
                    }
                }
            }
            finally
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
                LoadingProgressRing.IsActive = false;
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow is MainWindow mainWindow)
            {
                mainWindow.NavigateToPage(typeof(SettingsPage));
            }
        }

        private void AddSaveCard(MinecraftSave save)
        {
            var card = CreateSaveCard(save);
            ManagedSavesPanel.Children.Add(card);
            
            // Add to recent saves if not too many
            if (RecentSavesPanel.Children.Count < 5)
            {
                var recentCard = CreateSaveCard(save);
                RecentSavesPanel.Children.Insert(0, recentCard);
            }
        }

        private Border CreateSaveCard(MinecraftSave save)
        {
            var card = new Border
            {
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(16)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // World Icon
            var iconBorder = new Border
            {
                Width = 48,
                Height = 48,
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            
            var iconText = new TextBlock
            {
                Text = save.WorldIcon,
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            iconBorder.Child = iconText;
            Grid.SetRow(iconBorder, 0);
            Grid.SetColumn(iconBorder, 0);
            Grid.SetRowSpan(iconBorder, 2);

            // Name and Path
            var namePanel = new StackPanel { Orientation = Orientation.Vertical };
            
            var nameText = new TextBlock
            {
                Text = save.Name,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            
            var pathText = new TextBlock
            {
                Text = save.Path,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 4, 0, 0)
            };
            
            namePanel.Children.Add(nameText);
            namePanel.Children.Add(pathText);
            Grid.SetRow(namePanel, 0);
            Grid.SetColumn(namePanel, 1);

            // Metadata
            var metaGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            metaGrid.ColumnDefinitions.Add(new ColumnDefinition());
            metaGrid.ColumnDefinitions.Add(new ColumnDefinition());
            
            var leftMeta = new StackPanel();
            var typeText = new TextBlock
            {
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };
            typeText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "Type: " });
            typeText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = save.WorldType, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            
            var sizeText = new TextBlock
            {
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Margin = new Thickness(0, 4, 0, 0)
            };
            sizeText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "Size: " });
            sizeText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = save.WorldSizeFormatted, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            
            leftMeta.Children.Add(typeText);
            leftMeta.Children.Add(sizeText);
            Grid.SetColumn(leftMeta, 0);

            var rightMeta = new StackPanel();
            var playedText = new TextBlock
            {
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };
            playedText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "Last Played: " });
            playedText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = save.LastPlayedFormatted, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            
            var versionText = new TextBlock
            {
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Margin = new Thickness(0, 4, 0, 0)
            };
            versionText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "Version: " });
            versionText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = save.GameVersion, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            
            rightMeta.Children.Add(playedText);
            rightMeta.Children.Add(versionText);
            Grid.SetColumn(rightMeta, 1);
            
            metaGrid.Children.Add(leftMeta);
            metaGrid.Children.Add(rightMeta);
            Grid.SetRow(metaGrid, 1);
            Grid.SetColumn(metaGrid, 1);
            Grid.SetColumnSpan(metaGrid, 2);

            grid.Children.Add(iconBorder);
            grid.Children.Add(namePanel);
            grid.Children.Add(metaGrid);
            
            card.Child = grid;
            return card;
        }

        private System.Threading.Tasks.Task<MinecraftSave?> AnalyzeSaveFolder(string savePath)
        {
            try
            {
                // Validate it's a Minecraft save
                var levelDatPath = Path.Combine(savePath, "level.dat");
                var levelDatOldPath = Path.Combine(savePath, "level.dat_old");

                if (!File.Exists(levelDatPath) && !File.Exists(levelDatOldPath))
                {
                    return System.Threading.Tasks.Task.FromResult<MinecraftSave?>(null);
                }

                var directoryInfo = new DirectoryInfo(savePath);
                var save = new MinecraftSave
                {
                    Name = directoryInfo.Name,
                    Path = savePath,
                    LastPlayed = directoryInfo.LastWriteTime,
                    WorldSize = CalculateFolderSize(directoryInfo),
                    IsGitInitialized = Directory.Exists(Path.Combine(savePath, "GitMC")),
                    WorldType = "Survival", // Default
                    GameVersion = "1.21" // Default
                };

                // Set appropriate world icon based on world type
                save.WorldIcon = save.WorldType.ToLower() switch
                {
                    "creative" => "🎨",
                    "hardcore" => "💀",
                    "spectator" => "👻",
                    "adventure" => "🗺️",
                    _ => "🌍"
                };

                return System.Threading.Tasks.Task.FromResult<MinecraftSave?>(save);
            }
            catch
            {
                return System.Threading.Tasks.Task.FromResult<MinecraftSave?>(null);
            }
        }

        private static long CalculateFolderSize(DirectoryInfo directoryInfo)
        {
            try
            {
                return directoryInfo.EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(file => file.Length);
            }
            catch
            {
                return 0;
            }
        }

        // Legacy support for existing MainWindow integration
        private void SelectSaveButton_Click(object sender, RoutedEventArgs e)
        {
            AddSaveButton_Click(sender, e);
        }
    }
}
