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

        private void logoutButton_Click(object sender, RoutedEventArgs e)
        {
            LogoutAsync();
            CloseAllOtherApplications();
        }

        private async void LogoutAsync()
        {
            // Edge function URL to send the remain time, CPU Usage, etc.
            string pc_logout_url = $"{SUPABASE_URL}/functions/v1/pc-end-session";
            string apiKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InJ1YW5ld3licXhyZGZ2cmR5ZXFyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3Mzg1NDU3MzAsImV4cCI6MjA1NDEyMTczMH0.Sy_h_BHoN23rzRFpVc9ARN2wimJ8lRPEVh_hpw_7tlY";
            // Data to include in the request (e.g., JSON)
            var requestData = new
            {
                asset_id = SharedData.assetID,
                site_id = SharedData.siteID
            };

            // Serialize to JSON string
            string jsonLogData = System.Text.Json.JsonSerializer.Serialize(requestData);

            // Create HttpClient instance
            using (HttpClient client = new HttpClient())
            {
                // Add Authorization header (if needed)
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                // Create HttpContent with JSON data
                var content = new StringContent(jsonLogData, Encoding.UTF8, "application/json");

                try
                {
                    // Send POST request
                    HttpResponseMessage response = await client.PostAsync(pc_logout_url, content);
                    response.EnsureSuccessStatusCode();

                    SharedData.logoutFlag = true;

                    CloseAllOtherApplications();

                    Debug.WriteLine("Logout API called successfully");
                }
                catch (Exception hre)
                {
                    MessageBox.Show($"Log out failed: {hre.Message} \n {hre.StackTrace}");
                }
            }
        }

        public void CloseAllOtherApplications()
        {
            try
            {
                int currentProcessId = Process.GetCurrentProcess().Id;
                string currentProcessName = Process.GetCurrentProcess().ProcessName;

                Process[] processes = Process.GetProcesses();

                foreach (Process process in processes)
                {
                    try
                    {
                        if (process.Id == currentProcessId)
                            continue;

                        if (IsSystemProcess(process.ProcessName))
                            continue;

                        if (process.MainWindowHandle == IntPtr.Zero)
                            continue;

                        if (!process.CloseMainWindow())
                        {
                            if (!process.WaitForExit(2000))
                            {
                                process.Kill();
                            }
                        }

                        Debug.WriteLine($"Closed: {process.ProcessName}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to close {process.ProcessName}: {ex.Message}");
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error closing applications: {ex.Message}");
            }
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

            int sessionTime = SharedData.duration * 60;
            PerformanceCounter cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            ComputerInfo ci = new ComputerInfo();
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += async (s, e) =>
            {
                // Session Time & Remain Time
                time++;
                int remainTime = sessionTime - time;
                this.sessionBox.Content = $"{time / 60 / 60:D2}:{time / 60 % 60:D2}:{time % 60:D2}";
                this.remainBox.Content = $"{remainTime / 60 / 60:D2}:{remainTime / 60 % 60:D2}:{remainTime % 60:D2}";

                // Idle Time
                idleTime = IdleTimeHelper.GetIdleTime();
                this.idleBox.Content = idleTime.ToString(@"mm\:ss");

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
                var ni = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault();
                if (ni != null)
                {
                    var stats1 = ni.GetIPv4Statistics();
                    ulong sent1 = (ulong)stats1.BytesSent;
                    ulong received1 = (ulong)stats1.BytesReceived;

                    await Task.Delay(1000);

                    var stats2 = ni.GetIPv4Statistics();
                    ulong sent2 = (ulong)stats2.BytesSent;
                    ulong received2 = (ulong)stats2.BytesReceived;
                        
                    deltaSent = sent2 - sent1;
                    deltaReceived = received2 - received1;

                    var totalBytes = deltaSent + deltaReceived;
                    netSpeed = totalBytes * 8 / 1024.0; // Kbytes per second
                }
                cpuUsageBox.Content = $"{cpuUsage:F2}%";
                ramUsageBox.Content = $"{usedMemoryGB:F1}/{totalMemoryGB:F1} GB ({ramUsage:F0}%)";
                diskUsageBox.Content = $"{usedSpaceGB:F1}/{totalSizeGB:F1} GB ({diskUsage:F0}%)";
                if ( SharedData.curCulture == "en" )
                    netUsageBox.Content = $"Send     {deltaSent * 8 / 1024.0:F1} Kbps\nReceive {deltaReceived * 8 / 1024.0:F1} Kbps";
                else
                    netUsageBox.Content = $"Hantar   {deltaSent * 8 / 1024.0:F1} Kbps\nTerima  {deltaReceived * 8 / 1024.0:F1} Kbps";

                if ( remainTime / 60 % 60 == 10 && remainTime % 60 == 0 )
                {
                    var alert = new AlertWindow ($"10 {Properties.Resources.timeAlert}");
                    alert.Show();
                }
                else if (remainTime / 60 % 60 == 5 && remainTime % 60 == 0)
                {
                    var alert = new AlertWindow($"5 {Properties.Resources.timeAlert}");
                    alert.Show();
                }
                else if (remainTime < 0)
                {
                    SharedData.createdBy = "";
                    SharedData.duration = 0;
                    SharedData.logoutFlag = true;
                    LogoutAsync();
                    CloseAllOtherApplications();
                }

                if (idleTime.Minutes == 20)
                {
                    var alert = new AlertWindow($"{Properties.Resources.idleAlert} 20m");
                    alert.Show();
                }
                else if (idleTime.Minutes > 30)
                {
                    SharedData.logoutFlag = true;
                    LogoutAsync();
                    CloseAllOtherApplications();
                }

                if (time % 30 == 0)
                {
                    StoreStatus(cpuUsage, usedMemoryGB, totalMemoryGB, ramUsage, usedSpaceGB, totalSizeGB, diskUsage, deltaSent, deltaReceived, time, visitedAppsData);
                    SendData();
                }                
            };
            timer.Start();
        }

        private async void StoreStatus(double cpuUsage, double usedMemoryGB, double totalMemoryGB, double ramUsage, double usedSpaceGB, double totalSizeGB, double diskUsage, ulong deltaSent, ulong deltaReceived, int sessionTime, Dictionary<string, List<string>> visitedApps)
        {
            string curTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            // Edge function URL to send the remain time, CPU Usage, etc.
            string pc_log_url = $"{SUPABASE_URL}/functions/v1/pc-log-activity";
            string apiKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InJ1YW5ld3licXhyZGZ2cmR5ZXFyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3Mzg1NDU3MzAsImV4cCI6MjA1NDEyMTczMH0.Sy_h_BHoN23rzRFpVc9ARN2wimJ8lRPEVh_hpw_7tlY";
            string uptime = $"{sessionTime / 60:D2}:{sessionTime % 60:D2}";
            // Data to include in the request (e.g., JSON)
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

            // Serialize to JSON string
            string jsonLogData = System.Text.Json.JsonSerializer.Serialize(requestLogData);

            // Create HttpClient instance
            using (HttpClient client = new HttpClient())
            {
                // Add Authorization header (if needed)
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                // Create HttpContent with JSON data
                var content = new StringContent(jsonLogData, Encoding.UTF8, "application/json");

                try
                {
                    // Send POST request
                    HttpResponseMessage response = await client.PostAsync(pc_log_url, content);
                    response.EnsureSuccessStatusCode();
                }
                catch (HttpRequestException hre)
                {
                    Debug.WriteLine($"StoreStatus() failed");
                }
            }
        }
        private async void SendData()
        {
            // Edge function URL
            string pc_state_url = $"{SUPABASE_URL}/functions/v1/log-pc-state";
            string apiKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InJ1YW5ld3licXhyZGZ2cmR5ZXFyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3Mzg1NDU3MzAsImV4cCI6MjA1NDEyMTczMH0.Sy_h_BHoN23rzRFpVc9ARN2wimJ8lRPEVh_hpw_7tlY";
            string statePC = "";
            if (SharedData.startFlag == 2) statePC = "unlocked";
            else statePC = "locked";
            // Data to include in the request (e.g., JSON)
            var requestLogData = new
            {
                asset_id = SharedData.assetID,
                site_id = SharedData.siteID,
                state = statePC,
                created_by = SharedData.createdBy
            };

            // Serialize to JSON string
            string jsonLogData = System.Text.Json.JsonSerializer.Serialize(requestLogData);

            // Create HttpClient instance
            using (HttpClient client = new HttpClient())
            {
                // Add Authorization header (if needed)
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                // Create HttpContent with JSON data
                var content = new StringContent(jsonLogData, Encoding.UTF8, "application/json");

                try
                {
                    // Send POST request
                    HttpResponseMessage response = await client.PostAsync(pc_state_url, content);
                    response.EnsureSuccessStatusCode();
                }
                catch (HttpRequestException hre)
                {
                    Debug.WriteLine($"SendData() failed.");
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

            sessionBox.Content = "00:00:00";
            remainBox.Content = "00:00:00";
            idleBox.Content = "00:00";
            cpuUsageBox.Content = "0%";
            ramUsageBox.Content = "0/0 GB (0%)";
            diskUsageBox.Content = "0/0 GB (0%)";
            netUsageBox.Content = "Send 0 Kbps\nReceive 0 Kbps";
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