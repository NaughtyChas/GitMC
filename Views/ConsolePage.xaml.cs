using System.Diagnostics;
using GitMC.Services;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace GitMC.Views;

public sealed partial class ConsolePage : Page
{
    private readonly List<string> _commandHistory = new();
    private readonly IGitService _gitService;
    private string _gitVersion = "Unknown";
    private int _historyIndex = -1;

    public ConsolePage()
    {
        InitializeComponent();
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
            foreach (UIElement? child in headerGrid.Children)
                if (child is StackPanel stackPanel)
                    foreach (UIElement? element in stackPanel.Children)
                        if (element is TextBlock textBlock && textBlock.Name == "VersionText")
                        {
                            textBlock.Text = $"v{_gitVersion}";
                            break;
                        }
    }

    private void CommandInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            _ = ExecuteCommand();
        }
        else if (e.Key == VirtualKey.Up)
        {
            e.Handled = true;
            NavigateHistory(-1);
        }
        else if (e.Key == VirtualKey.Down)
        {
            e.Handled = true;
            NavigateHistory(1);
        }
        else if (e.Key == VirtualKey.C &&
                 (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0)
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
        try
        {
            await ExecuteCommand();
        }
        catch (Exception ex)
        {
            // Log error and show user-friendly message
            Debug.WriteLine($"Error executing command: {ex.Message}");
        }
    }

    private async Task ExecuteCommand()
    {
        string command = CommandInput.Text.Trim();
        if (string.IsNullOrEmpty(command)) return;

        // Add to history
        if (_commandHistory.Count == 0 || _commandHistory[^1] != command) _commandHistory.Add(command);
        _historyIndex = _commandHistory.Count;

        // Display the command with current directory
        string currentDirectory = _gitService.GetCurrentDirectory();
        string directoryName = Path.GetFileName(currentDirectory);
        if (string.IsNullOrEmpty(directoryName))
            directoryName = currentDirectory;

        AddOutputLine($"{directoryName}$ {command}", "#00FF00");

        bool succeeded = true;
        int? statusCode = null;

        CommandInput.Text = string.Empty;
        StatusCodeText.Visibility = Visibility.Collapsed;
        CommandStatusIcon.Visibility = Visibility.Collapsed;
        CommandProgressRing.Visibility = Visibility.Visible;

        try
        {
            // Handle special commands
            if (command.ToLower() == "clear" || command.ToLower() == "cls")
            {
                ClearConsole();
                return;
            }

            if (command.ToLower() == "cd")
            {
                succeeded = _gitService.ChangeToInitialDirectory();
                return;
            }

            if (command.ToLower().StartsWith("cd "))
            {
                succeeded = HandleChangeDirectory(command);
                return;
            }

            if (command.ToLower() == "pwd")
            {
                AddOutputLine(_gitService.GetCurrentDirectory(), "#CCCCCC");
                return;
            }

            // Execute Git command using GitService
            GitCommandResult? result = await ExecuteGitCommand(command);
            succeeded = result is { Success: true };
            statusCode = result?.ExitCode;
        }
        catch (Exception ex)
        {
            AddOutputLine($"Error: {ex.Message}", "#FF6B6B");
            succeeded = false;
        }
        finally
        {
            ExecuteButton.IsEnabled = true;
            CommandStatusIcon.Visibility = Visibility.Visible;
            CommandStatusIcon.Glyph = succeeded ? "\uEC61" : "\uEB90";
            StatusCodeText.Visibility = statusCode != null && statusCode != 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusCodeText.Text = statusCode?.ToString();
            StatusCodeText.Foreground = CommandStatusIcon.Foreground
                = Application.Current.Resources[succeeded ? "SystemFillColorSuccessBrush" : "SystemFillColorCriticalBrush"]
                as SolidColorBrush;
            CommandProgressRing.Visibility = Visibility.Collapsed;
            CommandInput.Focus(FocusState.Programmatic);
        }
    }

    private bool HandleChangeDirectory(string command)
    {
        try
        {
            string path = command.Substring(3).Trim().Trim('"');
            bool popDirectory = path == "-";

            if (string.IsNullOrEmpty(path))
            {
                // Show current directory
                AddOutputLine(_gitService.GetCurrentDirectory(), "#CCCCCC");
                return true;
            }

            if (popDirectory)
            {
                string? target = _gitService.GetPreviousDirectory();

                if (string.IsNullOrEmpty(target))
                {
                    AddOutputLine("There is no location history left to navigate backwards.", "#FF6B6B");
                    return false;
                }

                path = target;
            }

            if (_gitService.ChangeDirectory(path, !popDirectory))
            {
                AddOutputLine($"Changed to: {_gitService.GetCurrentDirectory()}", "#CCCCCC");
                if (popDirectory)
                    _gitService.PopDirectory();
            }
            else
            {
                AddOutputLine($"Directory not found: {path}", "#FF6B6B");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            AddOutputLine($"Error changing directory: {ex.Message}", "#FF6B6B");
            return false;
        }
    }

    private async Task<GitCommandResult?> ExecuteGitCommand(string command)
    {
        try
        {
            // Handle enhanced Git commands with LibGit2Sharp
            var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;

            string gitCommand = parts[0].ToLower();

            switch (gitCommand)
            {
                case "status":
                    return await HandleGitStatus();
                case "add":
                    return await HandleGitAdd(parts);
                case "commit":
                    return await HandleGitCommit(command);
                case "log":
                    return await HandleGitLog(parts);
                case "branch":
                    return await HandleGitBranch(parts);
                case "checkout":
                    return await HandleGitCheckout(parts);
                case "pull":
                    return await HandleGitPull();
                case "push":
                    return await HandleGitPush();
                case "fetch":
                    return await HandleGitFetch();
                case "diff":
                    return await HandleGitDiff(parts);
                case "reset":
                    return await HandleGitReset(parts);
                case "clone":
                    return await HandleGitClone(parts);
                case "merge":
                    return await HandleGitMerge(parts);
                default:
                    // Fallback to command line for unsupported commands
                    return await ExecuteCommandLineGit(command);
            }
        }
        catch (Exception ex)
        {
            AddOutputLine($"Error executing command: {ex.Message}", "#FF6B6B");
            return null;
        }
    }

    private async Task<GitCommandResult> HandleGitStatus()
    {
        var status = await _gitService.GetStatusAsync();
        var result = new GitCommandResult { Success = true };

        AddOutputLine($"On branch {status.CurrentBranch}", "#CCCCCC");

        if (status.AheadCount > 0 || status.BehindCount > 0)
        {
            if (status.AheadCount > 0 && status.BehindCount > 0)
                AddOutputLine($"Your branch is ahead by {status.AheadCount} and behind by {status.BehindCount} commits.", "#FFAA00");
            else if (status.AheadCount > 0)
                AddOutputLine($"Your branch is ahead of 'origin/{status.CurrentBranch}' by {status.AheadCount} commits.", "#FFAA00");
            else if (status.BehindCount > 0)
                AddOutputLine($"Your branch is behind 'origin/{status.CurrentBranch}' by {status.BehindCount} commits.", "#FFAA00");
        }

        if (status.StagedFiles.Length > 0)
        {
            AddOutputLine("Changes to be committed:", "#90EE90");
            foreach (var file in status.StagedFiles)
                AddOutputLine($"  modified:   {file}", "#90EE90");
            AddOutputLine("", "#CCCCCC");
        }

        if (status.ModifiedFiles.Length > 0)
        {
            AddOutputLine("Changes not staged for commit:", "#FF6B6B");
            foreach (var file in status.ModifiedFiles)
                AddOutputLine($"  modified:   {file}", "#FF6B6B");
            AddOutputLine("", "#CCCCCC");
        }

        if (status.DeletedFiles.Length > 0)
        {
            AddOutputLine("Deleted files:", "#FF6B6B");
            foreach (var file in status.DeletedFiles)
                AddOutputLine($"  deleted:    {file}", "#FF6B6B");
            AddOutputLine("", "#CCCCCC");
        }

        if (status.UntrackedFiles.Length > 0)
        {
            AddOutputLine("Untracked files:", "#FFAA00");
            foreach (var file in status.UntrackedFiles)
                AddOutputLine($"  {file}", "#FFAA00");
            AddOutputLine("", "#CCCCCC");
        }

        if (!status.HasChanges)
            AddOutputLine("nothing to commit, working tree clean", "#90EE90");

        return result;
    }

    private async Task<GitCommandResult> HandleGitAdd(string[] parts)
    {
        var result = new GitCommandResult();

        if (parts.Length < 2)
        {
            AddOutputLine("usage: git add <pathspec>...", "#FF6B6B");
            result.Success = false;
            return result;
        }

        var addResult = parts[1] == "." || parts[1] == "-A"
            ? await _gitService.StageAllAsync()
            : await _gitService.StageFileAsync(parts[1]);

        if (addResult.Success)
        {
            AddOutputLine($"Added {(parts[1] == "." ? "all files" : parts[1])} to staging area", "#90EE90");
            if (!string.IsNullOrEmpty(addResult.WarningMessage))
            {
                AddOutputLine($"Warning: {addResult.WarningMessage}", "#FFAA00");
            }
            result.Success = true;
        }
        else
        {
            AddOutputLine($"Failed to add {parts[1]}: {addResult.ErrorMessage}", "#FF6B6B");
            result.Success = false;
        }

        return result;
    }

    private async Task<GitCommandResult> HandleGitCommit(string command)
    {
        var result = new GitCommandResult();

        // Extract commit message from command
        var messageMatch = System.Text.RegularExpressions.Regex.Match(command, @"-m\s+[""'](.+?)[""']");
        if (!messageMatch.Success)
        {
            AddOutputLine("usage: git commit -m \"<message>\"", "#FF6B6B");
            result.Success = false;
            return result;
        }

        string message = messageMatch.Groups[1].Value;
        var commitResult = await _gitService.CommitAsync(message);

        if (commitResult.Success)
        {
            AddOutputLine($"Committed changes: {message}", "#90EE90");
            result.Success = true;
        }
        else
        {
            AddOutputLine($"Failed to commit: {commitResult.ErrorMessage}", "#FF6B6B");
            result.Success = false;
        }

        return result;
    }

    private async Task<GitCommandResult> HandleGitLog(string[] parts)
    {
        var result = new GitCommandResult { Success = true };

        int count = 10; // Default
        if (parts.Length > 1 && parts[1].StartsWith("--oneline"))
        {
            count = 20;
        }

        var commits = await _gitService.GetCommitHistoryAsync(count);

        foreach (var commit in commits)
        {
            if (parts.Length > 1 && parts[1].Contains("oneline"))
            {
                AddOutputLine($"{commit.Sha[..7]} {commit.Message}", "#FFAA00");
            }
            else
            {
                AddOutputLine($"commit {commit.Sha}", "#FFAA00");
                AddOutputLine($"Author: {commit.AuthorName} <{commit.AuthorEmail}>", "#CCCCCC");
                AddOutputLine($"Date:   {commit.AuthorDate:ddd MMM dd HH:mm:ss yyyy}", "#CCCCCC");
                AddOutputLine("", "#CCCCCC");
                AddOutputLine($"    {commit.Message}", "#CCCCCC");
                AddOutputLine("", "#CCCCCC");
            }
        }

        return result;
    }

    private async Task<GitCommandResult> HandleGitBranch(string[] parts)
    {
        var result = new GitCommandResult { Success = true };

        if (parts.Length == 1)
        {
            // List branches
            var branches = await _gitService.GetBranchesAsync();
            foreach (var branch in branches)
            {
                AddOutputLine(branch, branch.StartsWith("*") ? "#90EE90" : "#CCCCCC");
            }
        }
        else if (parts.Length == 2)
        {
            // Create new branch
            var createResult = await _gitService.CreateBranchAsync(parts[1]);
            if (createResult.Success)
            {
                AddOutputLine($"Created branch '{parts[1]}'", "#90EE90");
            }
            else
            {
                AddOutputLine($"Failed to create branch '{parts[1]}': {createResult.ErrorMessage}", "#FF6B6B");
                result.Success = false;
            }
        }

        return result;
    }

    private async Task<GitCommandResult> HandleGitCheckout(string[] parts)
    {
        var result = new GitCommandResult();

        if (parts.Length < 2)
        {
            AddOutputLine("usage: git checkout <branch>", "#FF6B6B");
            result.Success = false;
            return result;
        }

        var checkoutResult = await _gitService.CheckoutBranchAsync(parts[1]);
        if (checkoutResult.Success)
        {
            AddOutputLine($"Switched to branch '{parts[1]}'", "#90EE90");
            result.Success = true;
        }
        else
        {
            AddOutputLine($"Failed to checkout branch '{parts[1]}': {checkoutResult.ErrorMessage}", "#FF6B6B");
            result.Success = false;
        }

        return result;
    }

    private async Task<GitCommandResult> HandleGitPull()
    {
        var result = new GitCommandResult();

        bool success = await _gitService.PullAsync();
        if (success)
        {
            AddOutputLine("Successfully pulled changes from remote", "#90EE90");
            result.Success = true;
        }
        else
        {
            AddOutputLine("Failed to pull changes", "#FF6B6B");
            result.Success = false;
        }

        return result;
    }

    private async Task<GitCommandResult> HandleGitPush()
    {
        var result = new GitCommandResult();

        bool success = await _gitService.PushAsync();
        if (success)
        {
            AddOutputLine("Successfully pushed changes to remote", "#90EE90");
            result.Success = true;
        }
        else
        {
            AddOutputLine("Failed to push changes", "#FF6B6B");
            result.Success = false;
        }

        return result;
    }

    private async Task<GitCommandResult> HandleGitFetch()
    {
        var result = new GitCommandResult();

        bool success = await _gitService.FetchAsync();
        if (success)
        {
            AddOutputLine("Successfully fetched from remote", "#90EE90");
            result.Success = true;
        }
        else
        {
            AddOutputLine("Failed to fetch from remote", "#FF6B6B");
            result.Success = false;
        }

        return result;
    }

    private async Task<GitCommandResult> HandleGitDiff(string[] parts)
    {
        var result = new GitCommandResult { Success = true };

        string filePath = parts.Length > 1 ? parts[1] : null;
        string diff = await _gitService.GetDiffAsync(filePath);

        if (!string.IsNullOrEmpty(diff))
        {
            var lines = diff.Split('\n');
            foreach (var line in lines)
            {
                string color = "#CCCCCC";
                if (line.StartsWith("+")) color = "#90EE90";
                else if (line.StartsWith("-")) color = "#FF6B6B";
                else if (line.StartsWith("@@")) color = "#00FFFF";

                AddOutputLine(line, color);
            }
        }
        else
        {
            AddOutputLine("No differences found", "#CCCCCC");
        }

        return result;
    }

    private async Task<GitCommandResult> HandleGitReset(string[] parts)
    {
        var result = new GitCommandResult();

        string mode = "mixed";
        string target = null;

        if (parts.Length > 1)
        {
            if (parts[1] == "--hard") mode = "hard";
            else if (parts[1] == "--soft") mode = "soft";
            else target = parts[1];
        }

        if (parts.Length > 2) target = parts[2];

        bool success = await _gitService.ResetAsync(mode, target);
        if (success)
        {
            AddOutputLine($"Reset to {target ?? "HEAD"} ({mode})", "#90EE90");
            result.Success = true;
        }
        else
        {
            AddOutputLine("Failed to reset", "#FF6B6B");
            result.Success = false;
        }

        return result;
    }

    private async Task<GitCommandResult> HandleGitClone(string[] parts)
    {
        var result = new GitCommandResult();

        if (parts.Length < 3)
        {
            AddOutputLine("usage: git clone <url> <directory>", "#FF6B6B");
            result.Success = false;
            return result;
        }

        bool success = await _gitService.CloneAsync(parts[1], parts[2]);
        if (success)
        {
            AddOutputLine($"Successfully cloned repository to {parts[2]}", "#90EE90");
            result.Success = true;
        }
        else
        {
            AddOutputLine("Failed to clone repository", "#FF6B6B");
            result.Success = false;
        }

        return result;
    }

    private async Task<GitCommandResult> HandleGitMerge(string[] parts)
    {
        var result = new GitCommandResult();

        if (parts.Length < 2)
        {
            AddOutputLine("usage: git merge <branch>", "#FF6B6B");
            result.Success = false;
            return result;
        }

        bool success = await _gitService.MergeAsync(parts[1]);
        if (success)
        {
            AddOutputLine($"Successfully merged branch '{parts[1]}'", "#90EE90");
            result.Success = true;
        }
        else
        {
            AddOutputLine($"Failed to merge branch '{parts[1]}'", "#FF6B6B");
            result.Success = false;
        }

        return result;
    }

    private async Task<GitCommandResult> ExecuteCommandLineGit(string command)
    {
        // Fallback to original command line execution for unsupported commands
        GitCommandResult result = await _gitService.ExecuteCommandAsync(command);

        // Display output
        foreach (string line in result.OutputLines) AddOutputLine(line, "#CCCCCC");

        foreach (string line in result.ErrorLines) AddOutputLine(line, "#FF6B6B");

        if (result.OutputLines.Length == 0 && result.ErrorLines.Length == 0 && result.Success)
            AddOutputLine("Command executed successfully.", "#90EE90");

        // Only display ErrorMessage if it's different from what's already shown in ErrorLines
        if (!result.Success && !string.IsNullOrEmpty(result.ErrorMessage))
        {
            bool errorMessageAlreadyShown = result.ErrorLines.Any(line =>
                line.Contains(result.ErrorMessage, StringComparison.OrdinalIgnoreCase) ||
                result.ErrorMessage.Contains(line, StringComparison.OrdinalIgnoreCase));

            if (!errorMessageAlreadyShown)
            {
                AddOutputLine(result.ErrorMessage, "#FF6B6B");
            }

            if (result.ErrorMessage.Contains("not installed", StringComparison.OrdinalIgnoreCase) ||
                result.ErrorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase))
                AddOutputLine("Please install Git or ensure it's properly configured.", "#FFAA00");
        }

        return result;
    }

    private void AddOutputLine(string text, string color)
    {
        var paragraph = new Paragraph();
        var run = new Run { Text = text };

        // Set color
        try
        {
            var brush = new SolidColorBrush();
            if (color.StartsWith('#'))
            {
                uint colorValue = Convert.ToUInt32(color.Substring(1), 16);
                brush.Color = ColorHelper.FromArgb(
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
            run.Foreground = (SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseHighBrush"];
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

    private void CopyOutputButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string outputText = string.Empty;
            foreach (Block? block in ConsoleOutput.Blocks)
                if (block is Paragraph paragraph)
                    foreach (Inline? inline in paragraph.Inlines)
                        if (inline is Run run)
                            outputText += run.Text + Environment.NewLine;

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
