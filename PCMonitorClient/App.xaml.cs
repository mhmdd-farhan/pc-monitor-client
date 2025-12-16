using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace PCMonitorClient
{
    public partial class App : Application
    {
        private static Mutex? _mutex;
        private bool _isShuttingDown = false;
        private bool _isUpdating = false;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        const int SW_RESTORE = 9;

        private const string GITHUB_REPO_URL = "https://github.com/mhmdd-farhan/pc-monitor-client";
        private const string GITHUB_USERNAME = "mhmdd-farhan";
        private const string GITHUB_REPO = "pc-monitor-client";
        private const string INSTALLER_FILENAME = "PCmonitorClientSetup.msi";
        private const string APP_NAME = "Nadi Monitor";

        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            Application.Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            const string appName = "PCMonitorClient";
            bool createdNew;
            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                Process current = Process.GetCurrentProcess();
                foreach (Process process in Process.GetProcessesByName(current.ProcessName))
                {
                    if (process.Id != current.Id)
                    {
                        IntPtr handle = process.MainWindowHandle;
                        if (handle != IntPtr.Zero)
                        {
                            ShowWindow(handle, SW_RESTORE);
                            SetForegroundWindow(handle);
                        }
                        break;
                    }
                }
                Current.Shutdown();
                return;
            }

            base.OnStartup(e);

            Task.Run(async () => await CheckForUpdates());
        }

        private async Task CheckForUpdates()
        {
            try
            {
                var currentVersion = GetCurrentVersion();
                var latestRelease = await GetLatestGitHubRelease();

                if (latestRelease == null)
                {
                    System.Diagnostics.Debug.WriteLine("Tidak dapat mengecek update");
                    return;
                }

                Debug.WriteLine($"Latest Release: {latestRelease}");

                var latestVersion = ParseVersion(latestRelease.tag_name);

                Debug.WriteLine($"Latest Version: {latestVersion}");

                if (latestVersion > currentVersion)
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        if (_isShuttingDown || _isUpdating) return;

                        var result = ShowTopMostMessageBox(
                            $"New version available!\n\n" +
                            $"Current version: {currentVersion}\n" +
                            $"Newest version: {latestVersion}\n\n" +
                            $"You want to update this app now?",
                            "Update Available",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);

                        if (result == MessageBoxResult.Yes && !_isShuttingDown && !_isUpdating)
                        {
                            _isUpdating = true;
                            await DownloadAndInstallUpdate(latestRelease);
                        }
                    });
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("This app is the latest version");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Update check error: {ex.Message}");
            }
        }

        private async Task<GitHubRelease> GetLatestGitHubRelease()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "WPF-Auto-Updater");

                    var url = $"https://api.github.com/repos/{GITHUB_USERNAME}/{GITHUB_REPO}/releases/latest";
                    var response = await client.GetStringAsync(url);

                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };

                    Debug.WriteLine($"Response tag name: {response}.");
                    Debug.WriteLine($"Deserialized response: {JsonSerializer.Deserialize<GitHubRelease>(response, options)}.");

                    return JsonSerializer.Deserialize<GitHubRelease>(response, options);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting GitHub release: {ex.Message}");
                return null;
            }
        }

        private async Task DownloadAndInstallUpdate(GitHubRelease release)
        {
            Window progressWindow = null;
            System.Windows.Controls.TextBlock statusText = null;
            System.Windows.Controls.ProgressBar progressBar = null;
            System.Windows.Controls.TextBlock percentText = null;

            try
            {
                if (_isShuttingDown) return;

                var installerAsset = release.assets?.FirstOrDefault(a =>
                    a.name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
                    a.name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

                if (installerAsset == null)
                {
                    MessageBox.Show(
                        "New installer file not founded in server!.",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                // CRITICAL: Close all windows BEFORE creating progress window
                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        // Close LoginDialog and RegisterDialog first
                        var loginDialogs = Application.Current.Windows.OfType<LoginDialog>().ToList();
                        foreach (var dialog in loginDialogs)
                        {
                            try
                            {
                                dialog.Close();
                            }
                            catch { }
                        }

                        var registerDialogs = Application.Current.Windows.OfType<RegisterDialog>().ToList();
                        foreach (var dialog in registerDialogs)
                        {
                            try
                            {
                                dialog.Close();
                            }
                            catch { }
                        }

                        // Close MainWindow if exists
                        if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsLoaded)
                        {
                            try
                            {
                                Application.Current.MainWindow.Close();
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error closing windows: {ex.Message}");
                    }
                });

                // Wait a bit for windows to close
                await Task.Delay(500);

                // Create progress window
                progressWindow = new Window
                {
                    Title = "Updating...",
                    Width = 450,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStyle = WindowStyle.ToolWindow,
                    Topmost = true
                };

                var stackPanel = new System.Windows.Controls.StackPanel
                {
                    Margin = new Thickness(20)
                };

                statusText = new System.Windows.Controls.TextBlock
                {
                    Text = "Downloading installer...",
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 15)
                };

                progressBar = new System.Windows.Controls.ProgressBar
                {
                    Height = 25,
                    Minimum = 0,
                    Maximum = 100,
                    Value = 0
                };

                percentText = new System.Windows.Controls.TextBlock
                {
                    Text = "0%",
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 5, 0, 0),
                    Foreground = System.Windows.Media.Brushes.Gray
                };

                stackPanel.Children.Add(statusText);
                stackPanel.Children.Add(progressBar);
                stackPanel.Children.Add(percentText);
                progressWindow.Content = stackPanel;
                progressWindow.Show();

                // Download installer
                var tempPath = Path.Combine(Path.GetTempPath(), installerAsset.name);

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "WPF-Auto-Updater");

                    using (var response = await client.GetAsync(installerAsset.browser_download_url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        var canReportProgress = totalBytes != -1;

                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            var totalRead = 0L;
                            var buffer = new byte[8192];
                            var isMoreToRead = true;

                            do
                            {
                                var read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                                if (read == 0)
                                {
                                    isMoreToRead = false;
                                }
                                else
                                {
                                    await fileStream.WriteAsync(buffer, 0, read);
                                    totalRead += read;

                                    if (canReportProgress && !_isShuttingDown)
                                    {
                                        var progress = (int)((totalRead * 100) / totalBytes);

                                        await Dispatcher.InvokeAsync(() =>
                                        {
                                            try
                                            {
                                                if (progressBar != null && progressWindow != null && progressWindow.IsLoaded)
                                                {
                                                    progressBar.Value = progress;
                                                    percentText.Text = $"{progress}% ({FormatBytes(totalRead)} / {FormatBytes(totalBytes)})";
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                Debug.WriteLine($"Error updating progress: {ex.Message}");
                                            }
                                        });
                                    }
                                }
                            }
                            while (isMoreToRead && !_isShuttingDown);
                        }
                    }
                }

                if (_isShuttingDown)
                {
                    progressWindow?.Close();
                    return;
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        if (statusText != null && progressWindow != null && progressWindow.IsLoaded)
                        {
                            statusText.Text = "Download complete!";
                            progressBar.Value = 100;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error updating status: {ex.Message}");
                    }
                });

                await Task.Delay(1000);

                // Close progress window
                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        if (progressWindow != null && progressWindow.IsLoaded)
                        {
                            progressWindow.Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error closing window: {ex.Message}");
                    }
                });

                if (_isShuttingDown) return;

                // Show warning message before installation
                var warningResult = await Dispatcher.InvokeAsync(() =>
                {
                    return ShowTopMostMessageBox(
                        "IMPORTANT NOTICE\n\n" +
                        "Updating to the latest version will cause your PC to restart automatically.\n\n" +
                        "After the restart, please wait for the application to open automatically.\n\n" +
                        "Do you want to proceed with the update?",
                        "Update Warning",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                });

                if (warningResult != MessageBoxResult.Yes)
                {
                    // User cancelled, delete downloaded file
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                    catch { }
                    _isUpdating = false;
                    return;
                }

                // Set shutdown flag BEFORE starting installation
                _isShuttingDown = true;

                // Start installation directly
                if (tempPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    await CreateAndRunUpdateScript(tempPath);
                }
                else
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = tempPath,
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);
                }

                // Shutdown application
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                try
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (progressWindow != null && progressWindow.IsLoaded)
                        {
                            progressWindow.Close();
                        }
                    });
                }
                catch { }

                if (!_isShuttingDown)
                {
                    MessageBox.Show(
                        $"Error while download/install update:\n\n{ex.Message}",
                        "Error Update",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private async Task CreateAndRunUpdateScript(string installerPath)
        {
            try
            {
                string productCode = GetInstalledProductCode(APP_NAME);
                string scriptPath = Path.Combine(Path.GetTempPath(), "update_pcmonitor.bat");
                string appPath = "C:\\MyApps\\PCMonitorClient\\PCMonitorClient.exe";

                var scriptContent = new StringBuilder();
                scriptContent.AppendLine("@echo off");
                scriptContent.AppendLine("echo ========================================");
                scriptContent.AppendLine("echo PC Monitor Client Update Process");
                scriptContent.AppendLine("echo ========================================");
                scriptContent.AppendLine("echo.");

                scriptContent.AppendLine("timeout /t 3 /nobreak > nul");

                if (!string.IsNullOrEmpty(productCode))
                {
                    scriptContent.AppendLine("echo [1/5] Uninstalling old version...");
                    scriptContent.AppendLine($"msiexec.exe /x {productCode} /qn /norestart");
                    scriptContent.AppendLine("timeout /t 5 /nobreak > nul");
                    scriptContent.AppendLine("echo Old version uninstalled successfully.");
                    scriptContent.AppendLine("echo.");
                }

                scriptContent.AppendLine("echo [2/5] Removing old files...");
                scriptContent.AppendLine("if exist \"C:\\MyApps\\PCMonitorClient\" (");
                scriptContent.AppendLine("    rd /s /q \"C:\\MyApps\\PCMonitorClient\"");
                scriptContent.AppendLine("    echo Old files removed successfully.");
                scriptContent.AppendLine(") else (");
                scriptContent.AppendLine("    echo No old files found.");
                scriptContent.AppendLine(")");
                scriptContent.AppendLine("timeout /t 2 /nobreak > nul");
                scriptContent.AppendLine("echo.");

                scriptContent.AppendLine("echo [3/5] Installing new version...");
                scriptContent.AppendLine($"msiexec.exe /i \"{installerPath}\" /qb /norestart");
                scriptContent.AppendLine("timeout /t 8 /nobreak > nul");
                scriptContent.AppendLine("echo New version installed successfully.");
                scriptContent.AppendLine("echo.");

                scriptContent.AppendLine("echo [4/5] Cleaning up temporary files...");
                scriptContent.AppendLine($"del /f /q \"{installerPath}\"");
                scriptContent.AppendLine("echo Cleanup complete.");
                scriptContent.AppendLine("echo.");

                scriptContent.AppendLine("echo [5/5] Restarting PC...");
                scriptContent.AppendLine("echo Your PC will restart in 5 seconds...");
                scriptContent.AppendLine("timeout /t 5 /nobreak");
                scriptContent.AppendLine("echo.");

                // Add application to startup before restart
                scriptContent.AppendLine("echo Adding application to startup...");
                scriptContent.AppendLine($"reg add \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\" /v \"PCMonitorClient\" /t REG_SZ /d \"{appPath}\" /f");
                scriptContent.AppendLine("echo.");

                // Restart PC
                scriptContent.AppendLine("echo Restarting now...");
                scriptContent.AppendLine("shutdown /r /t 0 /f");
                scriptContent.AppendLine("echo.");

                // Delete script (this line might not execute due to restart)
                scriptContent.AppendLine($"del /f /q \"%~f0\"");
                scriptContent.AppendLine("exit");

                await File.WriteAllTextAsync(scriptPath, scriptContent.ToString());

                var startInfo = new ProcessStartInfo
                {
                    FileName = scriptPath,
                    UseShellExecute = true,
                    Verb = "runas",
                    CreateNoWindow = false
                };

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating update script: {ex.Message}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "msiexec.exe",
                    Arguments = $"/i \"{installerPath}\" /qb",
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(startInfo);
            }
        }

        private string GetInstalledProductCode(string appName)
        {
            try
            {
                string uninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(uninstallKey))
                {
                    if (key != null)
                    {
                        foreach (string subKeyName in key.GetSubKeyNames())
                        {
                            using (RegistryKey subKey = key.OpenSubKey(subKeyName))
                            {
                                var displayName = subKey?.GetValue("DisplayName")?.ToString();
                                if (displayName != null && displayName.Contains(appName))
                                {
                                    Debug.WriteLine($"Found installed app: {displayName}, Product Code: {subKeyName}");
                                    return subKeyName;
                                }
                            }
                        }
                    }
                }

                string uninstallKey32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(uninstallKey32))
                {
                    if (key != null)
                    {
                        foreach (string subKeyName in key.GetSubKeyNames())
                        {
                            using (RegistryKey subKey = key.OpenSubKey(subKeyName))
                            {
                                var displayName = subKey?.GetValue("DisplayName")?.ToString();
                                if (displayName != null && displayName.Contains(appName))
                                {
                                    Debug.WriteLine($"Found installed app (32-bit): {displayName}, Product Code: {subKeyName}");
                                    return subKeyName;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting product code: {ex.Message}");
            }

            return null;
        }

        private Version GetCurrentVersion()
        {
            try
            {
                return Assembly.GetExecutingAssembly().GetName().Version;
            }
            catch
            {
                return new Version(1, 0, 0);
            }
        }

        private Version ParseVersion(string tagName)
        {
            try
            {
                return new Version(tagName);
            }
            catch
            {
                return new Version(0, 0, 0);
            }
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        private MessageBoxResult ShowTopMostMessageBox(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            // Create a temporary invisible window to serve as the owner
            var ownerWindow = new Window
            {
                Width = 0,
                Height = 0,
                Left = -10000,
                Top = -10000,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true
            };

            ownerWindow.Show();
            ownerWindow.Activate();

            // Show MessageBox with the owner window
            var result = MessageBox.Show(ownerWindow, messageBoxText, caption, button, icon);

            // Close and cleanup the owner window
            ownerWindow.Close();

            return result;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _isShuttingDown = true;
            base.OnExit(e);
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            LogException(exception, "AppDomain.UnhandledException");

            if (!_isShuttingDown && !_isUpdating)
            {
                try
                {
                    MessageBox.Show($"Critical error: {exception?.Message}\n\nStack: {exception?.StackTrace}",
                                    "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { }
            }
        }

        private void Current_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            LogException(e.Exception, "Dispatcher.UnhandledException");

            // Ignore XAML parse errors during update
            if (_isUpdating || _isShuttingDown)
            {
                e.Handled = true;
                return;
            }

            if (!_isShuttingDown)
            {
                try
                {
                    MessageBox.Show($"UI Thread error: {e.Exception.Message}\n\nStack: {e.Exception.StackTrace}",
                                    "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { }
            }

            e.Handled = true;
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            LogException(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved();
        }

        private void LogException(Exception ex, string source)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error_log.txt");
                string logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{ex}\n\n";
                File.AppendAllText(logPath, logMessage);
                Debug.WriteLine($"Exception logged: {ex.Message}");
            }
            catch { }
        }
    }

    public class GitHubRelease
    {
        public string tag_name { get; set; }
        public string name { get; set; }
        public GitHubAsset[] assets { get; set; }
    }

    public class GitHubAsset
    {
        public string name { get; set; }
        public string browser_download_url { get; set; }
        public long size { get; set; }
    }
}