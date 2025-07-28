using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace GitMC.Views
{
    public sealed partial class ConsolePage : Page
    {
        private readonly List<string> _commandHistory = new();
        private int _historyIndex = -1;
        private Process? _currentProcess;
        private string _currentDirectory;
        private string _gitVersion = "Unknown";

        public ConsolePage()
        {
            this.InitializeComponent();
            _currentDirectory = Directory.GetCurrentDirectory();
            CommandInput.Focus(FocusState.Programmatic);
            _ = InitializeGitVersion();
        }

        private async Task InitializeGitVersion()
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = processInfo };
                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (!string.IsNullOrEmpty(output))
                {
                    // Extract version
                    var parts = output.Trim().Split(' ');
                    if (parts.Length >= 3)
                    {
                        _gitVersion = parts[2];
                    }
                }
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
                // Cancel current command
                if (_currentProcess != null && !_currentProcess.HasExited)
                {
                    try
                    {
                        _currentProcess.Kill();
                        AddOutputLine("^C", "#FF6B6B");
                        AddOutputLine("Command cancelled.", "#FFAA00");
                    }
                    catch { }
                }
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
            var directoryName = Path.GetFileName(_currentDirectory);
            if (string.IsNullOrEmpty(directoryName))
                directoryName = _currentDirectory;
            
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
                    await HandleChangeDirectory(command);
                    return;
                }

                if (command.ToLower() == "pwd")
                {
                    AddOutputLine(_currentDirectory, "#CCCCCC");
                    return;
                }

                // Execute Git command
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

        private async Task HandleChangeDirectory(string command)
        {
            try
            {
                var path = command.Substring(3).Trim().Trim('"');
                if (string.IsNullOrEmpty(path))
                {
                    // Show current directory
                    AddOutputLine(_currentDirectory, "#CCCCCC");
                }
                else
                {
                    if (path == "..")
                    {
                        var parent = Directory.GetParent(_currentDirectory);
                        if (parent != null)
                        {
                            _currentDirectory = parent.FullName;
                            Directory.SetCurrentDirectory(_currentDirectory);
                            AddOutputLine($"Changed to: {_currentDirectory}", "#CCCCCC");
                        }
                    }
                    else if (Path.IsPathRooted(path))
                    {
                        if (Directory.Exists(path))
                        {
                            _currentDirectory = path;
                            Directory.SetCurrentDirectory(_currentDirectory);
                            AddOutputLine($"Changed to: {_currentDirectory}", "#CCCCCC");
                        }
                        else
                        {
                            AddOutputLine($"Directory not found: {path}", "#FF6B6B");
                        }
                    }
                    else
                    {
                        var fullPath = Path.Combine(_currentDirectory, path);
                        if (Directory.Exists(fullPath))
                        {
                            _currentDirectory = Path.GetFullPath(fullPath);
                            Directory.SetCurrentDirectory(_currentDirectory);
                            AddOutputLine($"Changed to: {_currentDirectory}", "#CCCCCC");
                        }
                        else
                        {
                            AddOutputLine($"Directory not found: {fullPath}", "#FF6B6B");
                        }
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
                var processInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = command.StartsWith("git ") ? command.Substring(4) : command,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = _currentDirectory
                };

                _currentProcess = new Process { StartInfo = processInfo };
                
                var outputLines = new List<string>();
                var errorLines = new List<string>();

                _currentProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        outputLines.Add(e.Data);
                };

                _currentProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        errorLines.Add(e.Data);
                };

                _currentProcess.Start();
                _currentProcess.BeginOutputReadLine();
                _currentProcess.BeginErrorReadLine();

                await _currentProcess.WaitForExitAsync();

                // Display output
                foreach (var line in outputLines)
                {
                    AddOutputLine(line, "#CCCCCC");
                }

                foreach (var line in errorLines)
                {
                    AddOutputLine(line, "#FF6B6B");
                }

                if (outputLines.Count == 0 && errorLines.Count == 0 && _currentProcess.ExitCode == 0)
                {
                    AddOutputLine("Command executed successfully.", "#90EE90");
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("cannot find") || ex.Message.Contains("not found"))
                {
                    AddOutputLine("Git is not installed or not found in PATH.", "#FF6B6B");
                    AddOutputLine("Please install Git or ensure it's properly configured.", "#FFAA00");
                }
                else
                {
                    AddOutputLine($"Error executing command: {ex.Message}", "#FF6B6B");
                }
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
