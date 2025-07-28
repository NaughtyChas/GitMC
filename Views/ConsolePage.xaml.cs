using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using GitMC.Services;

namespace GitMC.Views
{
    public sealed partial class ConsolePage : Page
    {
        private readonly List<string> _commandHistory = new();
        private readonly IGitService _gitService;
        private int _historyIndex = -1;
        private string _gitVersion = "Unknown";

        public ConsolePage()
        {
            this.InitializeComponent();
            _gitService = new GitService();
            CommandInput.Focus(FocusState.Programmatic);
            _ = InitializeGitVersion();
        }

        private async Task InitializeGitVersion()
        {
            try
            {
                _gitVersion = await _gitService.GetVersionAsync();
            }
            catch
            {
                _gitVersion = "Not Found";
            }

            // Update the header with Git version
            UpdateHeaderVersion();
        }

        private void UpdateHeaderVersion()
        {
            // Find the version TextBlock in the header and update it
            var headerGrid = FindName("HeaderGrid") as Grid;
            if (headerGrid != null)
            {
                foreach (var child in headerGrid.Children)
                {
                    if (child is StackPanel stackPanel)
                    {
                        foreach (var element in stackPanel.Children)
                        {
                            if (element is TextBlock textBlock && textBlock.Name == "VersionText")
                            {
                                textBlock.Text = $"v{_gitVersion}";
                                break;
                            }
                        }
                    }
                }
            }
        }

        private void CommandInput_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                _ = ExecuteCommand();
            }
            else if (e.Key == Windows.System.VirtualKey.Up)
            {
                e.Handled = true;
                NavigateHistory(-1);
            }
            else if (e.Key == Windows.System.VirtualKey.Down)
            {
                e.Handled = true;
                NavigateHistory(1);
            }
            else if (e.Key == Windows.System.VirtualKey.C && 
                     (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0)
            {
                e.Handled = true;
                // Git command cancellation is handled by GitService
                AddOutputLine("^C", "#FF6B6B");
                AddOutputLine("Command cancellation requested.", "#FFAA00");
            }
        }

        private void NavigateHistory(int direction)
        {
            if (_commandHistory.Count == 0) return;

            _historyIndex += direction;
            
            if (_historyIndex < 0)
                _historyIndex = 0;
            else if (_historyIndex >= _commandHistory.Count)
                _historyIndex = _commandHistory.Count - 1;

            if (_historyIndex >= 0 && _historyIndex < _commandHistory.Count)
            {
                CommandInput.Text = _commandHistory[_historyIndex];
                CommandInput.SelectionStart = CommandInput.Text.Length;
            }
        }

        private async void ExecuteButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteCommand();
        }

        private async Task ExecuteCommand()
        {
            var command = CommandInput.Text.Trim();
            if (string.IsNullOrEmpty(command)) return;

            // Add to history
            if (_commandHistory.Count == 0 || _commandHistory[^1] != command)
            {
                _commandHistory.Add(command);
            }
            _historyIndex = _commandHistory.Count;

            // Display the command with current directory
            var currentDirectory = _gitService.GetCurrentDirectory();
            var directoryName = Path.GetFileName(currentDirectory);
            if (string.IsNullOrEmpty(directoryName))
                directoryName = currentDirectory;
            
            AddOutputLine($"{directoryName}$ {command}", "#00FF00");
            CommandInput.Text = string.Empty;
            
            try
            {
                // Handle special commands
                if (command.ToLower() == "clear" || command.ToLower() == "cls")
                {
                    ClearConsole();
                    return;
                }

                if (command.ToLower().StartsWith("cd "))
                {
                    HandleChangeDirectory(command);
                    return;
                }

                if (command.ToLower() == "pwd")
                {
                    AddOutputLine(_gitService.GetCurrentDirectory(), "#CCCCCC");
                    return;
                }

                // Execute Git command using GitService
                await ExecuteGitCommand(command);
            }
            catch (Exception ex)
            {
                AddOutputLine($"Error: {ex.Message}", "#FF6B6B");
            }
            finally
            {
                ExecuteButton.IsEnabled = true;
                CommandInput.Focus(FocusState.Programmatic);
            }
        }

        private void HandleChangeDirectory(string command)
        {
            try
            {
                var path = command.Substring(3).Trim().Trim('"');
                if (string.IsNullOrEmpty(path))
                {
                    // Show current directory
                    AddOutputLine(_gitService.GetCurrentDirectory(), "#CCCCCC");
                }
                else
                {
                    if (_gitService.ChangeDirectory(path))
                    {
                        AddOutputLine($"Changed to: {_gitService.GetCurrentDirectory()}", "#CCCCCC");
                    }
                    else
                    {
                        AddOutputLine($"Directory not found: {path}", "#FF6B6B");
                    }
                }
            }
            catch (Exception ex)
            {
                AddOutputLine($"Error changing directory: {ex.Message}", "#FF6B6B");
            }
        }

        private async Task ExecuteGitCommand(string command)
        {
            try
            {
                var result = await _gitService.ExecuteCommandAsync(command);

                // Display output
                foreach (var line in result.OutputLines)
                {
                    AddOutputLine(line, "#CCCCCC");
                }

                foreach (var line in result.ErrorLines)
                {
                    AddOutputLine(line, "#FF6B6B");
                }

                if (result.OutputLines.Length == 0 && result.ErrorLines.Length == 0 && result.Success)
                {
                    AddOutputLine("Command executed successfully.", "#90EE90");
                }

                if (!result.Success && !string.IsNullOrEmpty(result.ErrorMessage))
                {
                    AddOutputLine(result.ErrorMessage, "#FF6B6B");
                    if (result.ErrorMessage.Contains("not installed") || result.ErrorMessage.Contains("not found"))
                    {
                        AddOutputLine("Please install Git or ensure it's properly configured.", "#FFAA00");
                    }
                }
            }
            catch (Exception ex)
            {
                AddOutputLine($"Error executing command: {ex.Message}", "#FF6B6B");
            }
        }

        private void AddOutputLine(string text, string color)
        {
            var paragraph = new Paragraph();
            var run = new Run { Text = text };

            // Set color
            try
            {
                var brush = new SolidColorBrush();
                if (color.StartsWith("#"))
                {
                    var colorValue = Convert.ToUInt32(color.Substring(1), 16);
                    brush.Color = Microsoft.UI.ColorHelper.FromArgb(
                        0xFF,
                        (byte)((colorValue >> 16) & 0xFF),
                        (byte)((colorValue >> 8) & 0xFF),
                        (byte)(colorValue & 0xFF));
                }
                run.Foreground = brush;
            }
            catch
            {
                // Use default color if parsing fails
                run.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
            }

            paragraph.Inlines.Add(run);
            ConsoleOutput.Blocks.Add(paragraph);
            
            // Auto-scroll to bottom
            OutputScrollViewer.UpdateLayout();
            OutputScrollViewer.ScrollToVerticalOffset(OutputScrollViewer.ScrollableHeight);
        }

        private void ClearConsoleButton_Click(object sender, RoutedEventArgs e)
        {
            ClearConsole();
        }

        private void ClearConsole()
        {
            ConsoleOutput.Blocks.Clear();
            AddOutputLine("GitMC Console v0.1.0", "#00FF00");
            AddOutputLine("Git Bash integration ready. Type 'git --version' to verify installation.", "#CCCCCC");
            AddOutputLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━", "#CCCCCC");
        }

        private async void CopyOutputButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var outputText = string.Empty;
                foreach (var block in ConsoleOutput.Blocks)
                {
                    if (block is Paragraph paragraph)
                    {
                        foreach (var inline in paragraph.Inlines)
                        {
                            if (inline is Run run)
                            {
                                outputText += run.Text + Environment.NewLine;
                            }
                        }
                    }
                }

                var dataPackage = new DataPackage();
                dataPackage.SetText(outputText);
                Clipboard.SetContent(dataPackage);

                // Show brief feedback
                AddOutputLine("Console output copied to clipboard.", "#90EE90");
            }
            catch (Exception ex)
            {
                AddOutputLine($"Failed to copy to clipboard: {ex.Message}", "#FF6B6B");
            }
        }
    }
}
