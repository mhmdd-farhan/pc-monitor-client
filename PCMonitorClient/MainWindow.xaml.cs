using dotenv.net;
using Microsoft.VisualBasic.Devices;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace PCMonitorClient
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    ///     

    public partial class MainWindow : Window
    {
        public DispatcherTimer timer;
        public TimeSpan idleTime;
        public int time = 0;

        private static string SUPABASE_URL;

        public int currentSessionMinutes;

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        private Dictionary<string, List<string>> visitedAppsData = new()
        {
            { "web", new List<string>() },
            { "windowsApp", new List<string>() }
        };

        private string lastTitle = "";

        private ulong previousBytesSent = 0;
        private ulong previousBytesReceived = 0;
        private bool isFirstNetworkRead = true;

        private static readonly SemaphoreSlim _logoutLock = new SemaphoreSlim(1, 1);
        private bool _isLoggingOut = false;
        public MainWindow()
        {
            try
            {
                DotEnv.Load();
                var envVars = DotEnv.Read();
                SUPABASE_URL = envVars["SUPABASE_URL"] ?? "";
                if (SUPABASE_URL == "")
                {
                    MessageBox.Show("SUPABASE_URL not found in .env file");
                }
            }
            catch
            {
                Debug.WriteLine("No .env file found");
            }
            InitializeComponent();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            CleanupResources();
        }

        private void CleanupResources()
        {
            try
            {
                // Stop and dispose timer
                if (timer != null)
                {
                    timer.Stop();
                    timer.Tick -= null;
                    timer = null;
                }

                // Force garbage collection
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                Debug.WriteLine("MainWindow resources cleaned up");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }

        private async void logoutButton_Click(object sender, RoutedEventArgs e)
        {
            logoutButton.IsEnabled = false;
            logoutButton.Content = "Loading...";
            await PerformLogoutAsync();
            logoutButton.IsEnabled = true;
            logoutButton.Content = "Logout";
        }

        public async Task PerformLogoutAsync()
        {
            if (_isLoggingOut) return;

            await _logoutLock.WaitAsync();
            try
            {
                if (_isLoggingOut) return;
                _isLoggingOut = true;

                Debug.WriteLine("=== STARTING LOGOUT SEQUENCE ===");

                // Stop timer to prevent new data collection
                timer?.Stop();
                Debug.WriteLine("Timer stopped");

                // Collect final metrics
                int sessionTime = time;
                var finalVisitedApps = new Dictionary<string, List<string>>(visitedAppsData);

                // Get final system metrics
                PerformanceCounter cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                ComputerInfo ci = new ComputerInfo();

                double cpuUsage = cpuCounter.NextValue();
                await Task.Delay(500);
                cpuUsage = cpuCounter.NextValue();

                ulong totalMemory = ci.TotalPhysicalMemory;
                ulong availableMemory = ci.AvailablePhysicalMemory;
                ulong usedMemory = totalMemory - availableMemory;
                double usedMemoryGB = usedMemory / (1024.0 * 1024 * 1024);
                double totalMemoryGB = totalMemory / (1024.0 * 1024 * 1024);
                double ramUsage = usedMemoryGB / totalMemoryGB * 100.0;

                double[] diskInfo = GetAllDrivesInfo();
                double usedSpaceGB = diskInfo[0];
                double totalSizeGB = diskInfo[1];
                double diskUsage = diskInfo[2];

                var activeNetworkInterface = GetActiveNetworkInterface();
                ulong deltaSent = 0;
                ulong deltaReceived = 0;

                if (activeNetworkInterface != null)
                {
                    try
                    {
                        var currentStats = activeNetworkInterface.GetIPv4Statistics();
                        ulong currentBytesSent = (ulong)currentStats.BytesSent;
                        ulong currentBytesReceived = (ulong)currentStats.BytesReceived;

                        deltaSent = currentBytesSent - previousBytesSent;
                        deltaReceived = currentBytesReceived - previousBytesReceived;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error getting final network stats: {ex.Message}");
                    }
                }

                Debug.WriteLine("Final metrics collected");

                // Send final status data - WAIT for completion
                try
                {
                    Debug.WriteLine("📤 Sending final StoreStatus...");
                    await StoreStatus(cpuUsage, usedMemoryGB, totalMemoryGB, ramUsage,
                        usedSpaceGB, totalSizeGB, diskUsage, deltaSent, deltaReceived,
                        sessionTime, finalVisitedApps);
                    Debug.WriteLine("✅ Final StoreStatus completed");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ Error in final StoreStatus: {ex.Message}");
                }

                // Small delay to ensure data is sent
                await Task.Delay(500);

                // Send final PC state - WAIT for completion
                try
                {
                    Debug.WriteLine("📤 Sending final PC state...");
                    await SendData();
                    Debug.WriteLine("✅ Final SendData completed");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ Error in final SendData: {ex.Message}");
                }

                // Call logout API - WAIT for completion
                try
                {
                    Debug.WriteLine("📤 Calling logout API...");
                    await LogoutAsync();
                    Debug.WriteLine("✅ Logout API completed");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ Error in logout API: {ex.Message}");
                }

                // Wait a bit more to ensure all HTTP requests are sent
                await Task.Delay(1000);

                // ONLY NOW set logout flag
                SharedData.logoutFlag = true;

                Debug.WriteLine("=== LOGOUT SEQUENCE COMPLETED ===");
            }
            finally
            {
                _isLoggingOut = false;
                _logoutLock.Release();
            }
        }

        private async Task LogoutAsync()
        {
            string pc_logout_url = $"{SUPABASE_URL}/functions/v1/pc-end-session";
            string apiKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InJ1YW5ld3licXhyZGZ2cmR5ZXFyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3Mzg1NDU3MzAsImV4cCI6MjA1NDEyMTczMH0.Sy_h_BHoN23rzRFpVc9ARN2wimJ8lRPEVh_hpw_7tlY";

            var requestData = new
            {
                asset_id = SharedData.assetID,
                site_id = SharedData.siteID
            };

            string jsonLogData = System.Text.Json.JsonSerializer.Serialize(requestData);

            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(60);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                var content = new StringContent(jsonLogData, Encoding.UTF8, "application/json");

                try
                {
                    HttpResponseMessage response = await client.PostAsync(pc_logout_url, content);
                    response.EnsureSuccessStatusCode();
                    Debug.WriteLine("✅ Logout API called successfully");
                }
                catch (Exception hre)
                {
                    Debug.WriteLine($"❌ Logout API failed: {hre.Message}");
                }
            }
        }

        public void CloseAllOtherApplications()
        {
            //try
            //{
            //    int currentProcessId = Process.GetCurrentProcess().Id;
            //    string currentProcessName = Process.GetCurrentProcess().ProcessName;

            //    Process[] processes = Process.GetProcesses();

            //    foreach (Process process in processes)
            //    {
            //        try
            //        {
            //            if (process.Id == currentProcessId)
            //                continue;

            //            if (IsSystemProcess(process.ProcessName))
            //                continue;

            //            if (process.MainWindowHandle == IntPtr.Zero)
            //                continue;

            //            if (!process.CloseMainWindow())
            //            {
            //                if (!process.WaitForExit(2000))
            //                {
            //                    process.Kill();
            //                }
            //            }

            //            Debug.WriteLine($"Closed: {process.ProcessName}");
            //        }
            //        catch (Exception ex)
            //        {
            //            Debug.WriteLine($"Failed to close {process.ProcessName}: {ex.Message}");
            //        }
            //        finally
            //        {
            //            process.Dispose();
            //        }
            //    }
            //}
            //catch (Exception ex)
            //{
            //    Debug.WriteLine($"Error closing applications: {ex.Message}");
            //}
        }

        private bool IsSystemProcess(string processName)
        {
            string[] systemProcesses = new string[]
            {
                "System", "Registry", "smss", "csrss", "wininit", "services",
                "lsass", "svchost", "winlogon", "explorer", "dwm", "taskmgr",
                "SearchHost", "StartMenuExperienceHost", "RuntimeBroker",
                "ShellExperienceHost", "TextInputHost", "SecurityHealthSystray",
                "SecurityHealthService", "MsMpEng", "NisSrv", "conhost",
                "fontdrvhost", "sihost", "ctfmon", "SearchApp", "ApplicationFrameHost"
            };

            return systemProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase);
        }

        private void StartTrackingApps()
        {
            string activeTitle = GetActiveWindowTitle();

            if (!string.IsNullOrEmpty(activeTitle) && activeTitle != lastTitle)
            {
                if (IsBrowser(activeTitle))
                {
                    string site = ExtractWebsiteName(activeTitle);
                    AddToList("web", site);
                }
                else
                {
                    AddToList("windowsApp", activeTitle);
                }

                lastTitle = activeTitle;
            }
        }

        private string GetActiveWindowTitle()
        {
            IntPtr handle = GetForegroundWindow();
            StringBuilder buffer = new StringBuilder(256);
            if (GetWindowText(handle, buffer, buffer.Capacity) > 0)
            {
                return buffer.ToString();
            }
            return string.Empty;
        }

        private bool IsBrowser(string title)
        {
            // Simple check: if title ends with browser name
            return title.Contains("Chrome") || title.Contains("Edge") || title.Contains("Firefox");
        }

        private string ExtractWebsiteName(string title)
        {
            // Example: "YouTube - Google Chrome" → "YouTube"
            var parts = title.Split('-');
            if (parts.Length > 0)
            {
                return parts[0].Trim();
            }
            return title;
        }

        private void AddToList(string key, string value)
        {
            if (!visitedAppsData[key].Contains(value))
            {
                visitedAppsData[key].Add(value);
            }
        }

        private void minimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {      
            this.WindowStartupLocation = WindowStartupLocation.Manual;
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            this.Left = screenWidth - this.Width - 100;
            this.Top = 100;

            int sessionTime = currentSessionMinutes * 60;
            PerformanceCounter cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            ComputerInfo ci = new ComputerInfo();

            var activeNetworkInterface = GetActiveNetworkInterface();
            if (activeNetworkInterface != null)
            {
                var initialStats = activeNetworkInterface.GetIPv4Statistics();
                previousBytesSent = (ulong)initialStats.BytesSent;
                previousBytesReceived = (ulong)initialStats.BytesReceived;
            }

            timer?.Stop();
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += async (s, e) =>
            {
                // Session Time & Remain Time
                int currentSessionTime = currentSessionMinutes * 60;

                time++;
                int remainTime = currentSessionTime - time;

                this.remainBox.Content = $"{remainTime / 3600:D2}:{remainTime / 60 % 60:D2}:{remainTime % 60:D2}";

                // Idle Time
                idleTime = IdleTimeHelper.GetIdleTime();

                // CPU
                cpuCounter.NextValue();
                await Task.Delay(500);
                double cpuUsage = cpuCounter.NextValue();

                // RAM
                ulong totalMemory = ci.TotalPhysicalMemory;
                ulong availableMemory = ci.AvailablePhysicalMemory;
                ulong usedMemory = totalMemory - availableMemory;
                double usedMemoryGB = usedMemory / (1024.0 * 1024 * 1024);
                double totalMemoryGB = totalMemory / (1024.0 * 1024 * 1024);
                double ramUsage = usedMemoryGB / totalMemoryGB * 100.0;

                // Disk
                double[] diskInfo = GetAllDrivesInfo();
                double usedSpaceGB = diskInfo[0]; 
                double totalSizeGB = diskInfo[1];
                double diskUsage = diskInfo[2];

                // Visited Apps
                StartTrackingApps();

                // Network (example for total bytes received)
                double netSpeed = 0.0;
                ulong deltaSent = 0;
                ulong deltaReceived = 0;

                var ni = GetActiveNetworkInterface();
                if (ni != null)
                {
                    try
                    {
                        var currentStats = ni.GetIPv4Statistics();
                        ulong currentBytesSent = (ulong)currentStats.BytesSent;
                        ulong currentBytesReceived = (ulong)currentStats.BytesReceived;

                        if (!isFirstNetworkRead)
                        {
                            // Calculate delta from previous reading
                            deltaSent = currentBytesSent - previousBytesSent;
                            deltaReceived = currentBytesReceived - previousBytesReceived;
                        }
                        else
                        {
                            isFirstNetworkRead = false;
                        }

                        // Update previous values for next iteration
                        previousBytesSent = currentBytesSent;
                        previousBytesReceived = currentBytesReceived;

                        var totalBytes = deltaSent + deltaReceived;
                        netSpeed = totalBytes * 8 / 1024.0; // Kbps
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Network stats error: {ex.Message}");
                    }
                }
                Debug.WriteLine($"Session Minutes: {currentSessionMinutes} Session Time: {currentSessionTime}s, Remain time: {remainTime} in main window");
                // Show alert for last 10 minutes session
                if (remainTime == 600)
                {
                    var alert = new AlertWindow ($"10 {Properties.Resources.timeAlert}");
                    alert.Show();
                }
                // Show alert for last 5 minutes session
                else if (remainTime == 300)
                {
                    var alert = new AlertWindow($"5 {Properties.Resources.timeAlert}");
                    alert.Show();
                }
                else if (remainTime < 0)
                {
                    timer.Stop(); // Stop timer immediately

                    Debug.WriteLine("Session expired - initiating automatic logout");
                    await PerformLogoutAsync(); // Use centralized logout

                    CloseAllOtherApplications();
                }

                //if (idleTime.Minutes == 20)
                //{
                //    var alert = new AlertWindow($"{Properties.Resources.idleAlert} 20m");
                //    alert.Show();
                //}
                //else if (idleTime.Minutes > 30)
                //{
                //    timer.Stop(); // Stop timer immediately

                //    Debug.WriteLine("Idle timeout - initiating automatic logout");
                //    await PerformLogoutAsync(); // Use centralized logout

                //    CloseAllOtherApplications();
                //}              
            };
            timer.Start();
        }

        private NetworkInterface GetActiveNetworkInterface()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni =>
                        ni.OperationalStatus == OperationalStatus.Up &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                        ni.GetIPv4Statistics().BytesReceived > 0)
                    .OrderByDescending(ni => ni.GetIPv4Statistics().BytesReceived)
                    .FirstOrDefault();

                return interfaces;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting network interface: {ex.Message}");
                return null;
            }
        }

        public async Task StoreStatus(double cpuUsage, double usedMemoryGB, double totalMemoryGB,
        double ramUsage, double usedSpaceGB, double totalSizeGB, double diskUsage,
        ulong deltaSent, ulong deltaReceived, int sessionTime,
        Dictionary<string, List<string>> visitedApps)
        {
            Debug.WriteLine("Storing status...");
            string curTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            string pc_log_url = $"{SUPABASE_URL}/functions/v1/pc-log-activity";
            string apiKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InJ1YW5ld3licXhyZGZ2cmR5ZXFyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3Mzg1NDU3MzAsImV4cCI6MjA1NDEyMTczMH0.Sy_h_BHoN23rzRFpVc9ARN2wimJ8lRPEVh_hpw_7tlY";
            string uptime = $"{sessionTime / 60:D2}:{sessionTime % 60:D2}";

            var requestLogData = new
            {
                asset_id = SharedData.assetID,
                site_id = SharedData.siteID,
                data_log = new
                {
                    timestamp = curTime,
                    metrics = new
                    {
                        cpu_usage_percent = $"{cpuUsage:F2}",
                        ram_usage = new
                        {
                            used_mb = $"{usedMemoryGB:F1}",
                            total_mb = $"{totalMemoryGB:F1}",
                            usage_percent = $"{ramUsage:F0}"
                        },
                        disk_usage = new
                        {
                            used_gb = $"{usedSpaceGB:F1}",
                            total_gb = $"{totalSizeGB:F1}",
                            usage_percent = $"{diskUsage:F0}"
                        },
                        network = new
                        {
                            upload_kbps = $"{deltaSent * 8 / 1024.0:F1}",
                            download_kbps = $"{deltaReceived * 8 / 1024.0:F1}"
                        },
                        visited_apps = visitedApps,
                        uptime_minutes = uptime
                    },
                    status = "active"
                },
                created_by = SharedData.createdBy
            };

            string jsonLogData = System.Text.Json.JsonSerializer.Serialize(requestLogData);

            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(60); // Add timeout
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                var content = new StringContent(jsonLogData, Encoding.UTF8, "application/json");

                try
                {
                    HttpResponseMessage response = await client.PostAsync(pc_log_url, content);
                    response.EnsureSuccessStatusCode();
                    Debug.WriteLine("✅ Status stored successfully");
                }
                catch (HttpRequestException hre)
                {
                    Debug.WriteLine($"❌ StoreStatus failed: {hre.Message}");
                }
            }
        }

        // Add timeout to SendData
        public async Task SendData()
        {
            Debug.WriteLine("Sending PC state...");
            string pc_state_url = $"{SUPABASE_URL}/functions/v1/log-pc-state";
            string apiKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InJ1YW5ld3licXhyZGZ2cmR5ZXFyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3Mzg1NDU3MzAsImV4cCI6MjA1NDEyMTczMH0.Sy_h_BHoN23rzRFpVc9ARN2wimJ8lRPEVh_hpw_7tlY";
            string statePC = SharedData.startFlag == 2 ? "unlocked" : "locked";

            var requestLogData = new
            {
                asset_id = SharedData.assetID,
                site_id = SharedData.siteID,
                state = statePC,
                created_by = SharedData.createdBy
            };

            string jsonLogData = System.Text.Json.JsonSerializer.Serialize(requestLogData);

            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(60); // Add timeout
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                var content = new StringContent(jsonLogData, Encoding.UTF8, "application/json");

                try
                {
                    HttpResponseMessage response = await client.PostAsync(pc_state_url, content);
                    response.EnsureSuccessStatusCode();
                    Debug.WriteLine("✅ PC state sent successfully");
                }
                catch (HttpRequestException hre)
                {
                    Debug.WriteLine($"❌ SendData failed: {hre.Message}");
                }
            }
        }

        public void ResetMonitoringData()
        {
            timer?.Stop();

            time = 0;
            idleTime = TimeSpan.Zero;
            lastTitle = "";
            visitedAppsData = new()
            {
                { "web", new List<string>() },
                { "windowsApp", new List<string>() }
            };
            // Reset network tracking
            isFirstNetworkRead = true;
            previousBytesSent = 0;
            previousBytesReceived = 0;

            int sessionTime = SharedData.duration * 60;
            remainBox.Content = $"{sessionTime / 3600:D2}:{sessionTime / 60 % 60:D2}:{sessionTime % 60:D2}";
        }

        private double[] GetAllDrivesInfo()
        {
            var drives = DriveInfo.GetDrives();
            double totalSizeGB = 0;
            double freeSpaceGB = 0;
            double usedSpaceGB = 0;
            double diskUsage = 0;
            foreach (var drive in drives)
            {
                if (drive.IsReady)
                {
                    totalSizeGB += drive.TotalSize / (1024.0 * 1024 * 1024);
                    freeSpaceGB += drive.TotalFreeSpace / (1024.0 * 1024 * 1024);
                }
            }
            usedSpaceGB = totalSizeGB - freeSpaceGB;
            diskUsage = usedSpaceGB / totalSizeGB * 100.0;
            return new double[] { usedSpaceGB, totalSizeGB, diskUsage };
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            this.DragMove();
        }
    }
    public static class IdleTimeHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("User32.dll")]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("kernel32.dll")]
        static extern uint GetTickCount();

        public static TimeSpan GetIdleTime()
        {
            //if (SharedData.startFlag == 2 || SharedData.startFlag == 3)
            //{
            //    //Simulate pressing "A"
            //    keybd_event(0x41, 0, 0x0002, UIntPtr.Zero);
            //}
            LASTINPUTINFO lastInputInfo = new LASTINPUTINFO();
            lastInputInfo.cbSize = (uint)Marshal.SizeOf(lastInputInfo);

            if (GetLastInputInfo(ref lastInputInfo))
            {
                uint lastInputTick = lastInputInfo.dwTime;
                uint currentTick = GetTickCount();
                uint idleTimeMs = currentTick - lastInputTick;
                return TimeSpan.FromMilliseconds(idleTimeMs);
            }
            else
            {
                return TimeSpan.Zero;
            }
        }
    }
}