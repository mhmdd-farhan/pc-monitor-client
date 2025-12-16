using dotenv.net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Encoders;
using SIPSorceryMedia.FFmpeg;
using SIPSorceryMedia.Windows;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WebSocketSharp;
using WindowsInput;
using WindowsInput.Native;
using MessageBox = System.Windows.MessageBox;

namespace PCMonitorClient
{
    /// <summary>
    /// Interaction logic for LoginDialog.xaml
    /// </summary>
    public partial class LoginDialog : Window
    {
        private KeyboardHook _keyboardHook;
        private static MainWindow _mainWindow;

        private static bool _isInitialized = false;

        private static bool _isCleaningUp = false;
        private static bool _hasWebClient = false;
        private static bool _isReconnecting = false;
        private static SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);

        private string SUPABASE_URL;
        private static string ffmpegLibFullPath;
        private const int TEST_PATTERN_FRAMES_PER_SECOND = 30;
        private static int _frameCount = 0;
        private static DateTime _startTime;
        private static Microsoft.Extensions.Logging.ILogger _logger = NullLogger.Instance;

        private static WebSocket ws;
        private static RTCPeerConnection pc;
        private static List<RTCIceCandidateInit> iceBuffer = new List<RTCIceCandidateInit>();
        private WriteableBitmap remoteVideoBitmap;
        private static string channelName = SharedData.assetID.ToString();
        private static string userId = "Client" + SharedData.assetID.ToString();

        private static AudioSource _audioSource;
        private static DxgiScreenCaptureSource _videoSource;
        private static Boolean logoutFlag = SharedData.logoutFlag;

        private static InputSimulator _inputSimulator;

        private CancellationTokenSource _cancellationTokenSource;
        private SemaphoreSlim _initializationLock = new SemaphoreSlim(1, 1);

        private static System.Windows.Threading.DispatcherTimer _statusMonitoringTimer;
        private static readonly object _timerLock = new object();
        private static bool _isMonitoringActive = false;

        private static System.Timers.Timer _realTimeBroadCastTimer = new System.Timers.Timer();
        public LoginDialog()
        {
            if (File.Exists("Settings.dll") == false)
            {
                var _registerDialog = new Register();
                _registerDialog.ShowDialog();
            }

            try
            {
                 byte[] encryptedFromFile = File.ReadAllBytes("Settings.dll");
                 string decryptedJson = DecryptStringFromBytes_Aes(encryptedFromFile, SharedData.key, SharedData.iv);
                 var data = System.Text.Json.JsonSerializer.Deserialize<PCData>(decryptedJson);
                 SharedData.assetID = data.pc_id;
                 SharedData.pcName = data.pc_name;
                 SharedData.siteID = data.site_id;
                 SharedData.siteName = data.site_name;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Read File Error: {ex.Message}, {ex.StackTrace}");
            }

            try
            {
                string[] path = { AppDomain.CurrentDomain.BaseDirectory, "ffmpeg-bin" };
                ffmpegLibFullPath = Path.Combine(path);
                Debug.WriteLine($"FFmpeg path: {ffmpegLibFullPath}");
            }
            catch (Exception ex)
            {
                Debug.Write(ex);
            }

            try
            {
                DotEnv.Load();
                var envVars = DotEnv.Read();
                SUPABASE_URL = envVars["SUPABASE_URL"] ?? "";
                if (SUPABASE_URL == "")
                {
                    MessageBox.Show("supabase url or wss url not found");
                }
            }
            catch
            {
                Debug.WriteLine("No .env file found");
            }
            _cancellationTokenSource = new CancellationTokenSource();
            InitializeComponent();
            try
            {
                _keyboardHook = new KeyboardHook();
                _keyboardHook.SetHook();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize keyboard hook: {ex.Message}");
            }
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow();
            }
            lock (_timerLock)
            {
                if (_statusMonitoringTimer == null)
                {
                    Debug.WriteLine("=== INITIALIZING MONITORING TIMER ===");
                    InitializeStatusMonitoring();
                }
                else
                {
                    Debug.WriteLine($"=== TIMER EXISTS - IsEnabled: {_statusMonitoringTimer.IsEnabled} ===");
           
                    if (!_statusMonitoringTimer.IsEnabled)
                    {
                        _statusMonitoringTimer.Start();
                        Debug.WriteLine("=== TIMER RESTARTED ===");
                    }
                }
            }
            // Open ws and peer connection.
            if (!_isInitialized)
            {
                InitializeAsync();
                _isInitialized = true;
            }
        }

        private async void InitializeAsync()
        {
            try
            {
                RunRealtimeBroadcast();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Initialization error: {ex.Message}");
                MessageBox.Show("Initialization error.");
            }
        }

        private void LoginDialog_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue == true)
            {
                Debug.WriteLine("=== LOGIN WINDOW BECAME VISIBLE ===");

                lock (_timerLock)
                {
                    if (_statusMonitoringTimer == null)
                    {
                        Debug.WriteLine("Timer is null, initializing...");
                        InitializeStatusMonitoring();
                    }
                    else if (!_statusMonitoringTimer.IsEnabled)
                    {
                        Debug.WriteLine("Timer is stopped, restarting...");
                        _statusMonitoringTimer.Start();
                    }
                    else
                    {
                        Debug.WriteLine($"Timer is running (Interval: {_statusMonitoringTimer.Interval.TotalMilliseconds}ms)");
                    }
                }

                // Reinitialize keyboard hook
                try
                {
                    if (_keyboardHook == null)
                    {
                        Debug.WriteLine("Reinitializing keyboard hook...");
                        _keyboardHook = new KeyboardHook();
                        _keyboardHook.SetHook();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reinitializing keyboard hook: {ex.Message}");
                }
            }
            else
            {
                Debug.WriteLine("=== LOGIN WINDOW BECAME HIDDEN ===");
            }
        }


        private void InitializeStatusMonitoring()
        {
            lock (_timerLock)
            {
                if (_statusMonitoringTimer != null)
                {
                    Debug.WriteLine("Status monitoring already initialized");
                    return;
                }

                _statusMonitoringTimer = new System.Windows.Threading.DispatcherTimer();
                _statusMonitoringTimer.Interval = TimeSpan.FromMilliseconds(500);
                _statusMonitoringTimer.Tick += StatusMonitoring_Tick;
                _statusMonitoringTimer.Start();

                Debug.WriteLine("=== STATUS MONITORING INITIALIZED ===");
            }
        }

        private async void StatusMonitoring_Tick(object sender, EventArgs e)
        {
            try
            {
                if (_isMonitoringActive)
                {
                    return;
                }

                _isMonitoringActive = true;

                var currentStartFlag = SharedData.startFlag;
                var currentLogoutFlag = SharedData.logoutFlag;

                Debug.WriteLine($"[Monitor] startFlag={currentStartFlag}, logoutFlag={currentLogoutFlag}, IsVisible={this.IsVisible}");

                // Handle login success (startFlag = 1)
                if (currentStartFlag == 1 && !currentLogoutFlag && this.IsVisible)
                {
                    await HandleLoginSuccess();
                }
                // Handle logout (logoutFlag = true AND was logged in)
                else if (currentLogoutFlag && currentStartFlag >= 2)
                {
                    await HandleLogout();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR in StatusMonitoring_Tick: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                _isMonitoringActive = false;
            }
        }

        private async Task HandleLoginSuccess()
        {
            Debug.WriteLine("=== HANDLING LOGIN SUCCESS ===");

            try
            {
                if (SharedData.startFlag != 1)
                {
                    Debug.WriteLine($"Invalid state for login: startFlag={SharedData.startFlag}");
                    return;
                }

                if (SharedData.duration == 0)
                {
                    Debug.WriteLine("Setting Manual duration..");
                    SharedData.duration = 60; // Default to 60 minutes if not set
                    _mainWindow.currentSessionMinutes = SharedData.duration;
                }

                // Set flag FIRST to prevent re-entry
                SharedData.startFlag = 2;
                SharedData.logoutFlag = false;

                await Dispatcher.InvokeAsync(() =>
                {
                    var registerDialogs = Application.Current.Windows.OfType<RegisterDialog>().ToList();
                    foreach (var regDialog in registerDialogs)
                    {
                        Debug.WriteLine("Closing RegisterDialog before login");
                        try
                        {
                            regDialog.Close();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error closing RegisterDialog: {ex.Message}");
                        }
                    }
                });

                // Dispose keyboard hook
                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        _keyboardHook?.Dispose();
                        _keyboardHook = null;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error disposing keyboard hook: {ex.Message}");
                    }
                });

                // Reset and show main window
                await Dispatcher.InvokeAsync(() =>
                {
                    _mainWindow.ResetMonitoringData();
                    _mainWindow.Show();
                    _mainWindow.Activate();
                    _mainWindow.timer?.Start();
                    this.Hide();
                });

                Debug.WriteLine("Main window shown successfully");

                // Initialize WebSocket connection with delay
                await Task.Delay(500);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await RunAsync();
                        Debug.WriteLine("WebSocket connection initialized successfully");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"RunAsync error: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR in HandleLoginSuccess: {ex.Message}");
                // Rollback state
                SharedData.startFlag = 1;
            }
        }

        private async Task HandleLogout()
        {
            Debug.WriteLine("=== HANDLING LOGOUT ===");

            try
            {
                if (SharedData.startFlag != 2 && SharedData.startFlag != 3)
                {
                    Debug.WriteLine($"Invalid state for logout: startFlag={SharedData.startFlag}");
                    return;
                }

                // Set flags FIRST to prevent re-entry
                var wasLoggedIn = SharedData.startFlag == 2;
                SharedData.startFlag = 3;
                SharedData.logoutFlag = false;

                // Cleanup if was logged in
                if (wasLoggedIn)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await LogOutAsync();
                            await _mainWindow.PerformLogoutAsync();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error in LogOutAsync: {ex.Message}");
                        }
                    });

                    await Dispatcher.InvokeAsync(() =>
                    {
                        _mainWindow.timer?.Stop();
                        _mainWindow.Hide();
                        _mainWindow.CloseAllOtherApplications();
                    });
                }

                // Show login window
                await Dispatcher.InvokeAsync(() =>
                {
                    this.Show();
                    this.Activate();
                });

                // Reinitialize keyboard hook
                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        if (_keyboardHook == null)
                        {
                            _keyboardHook = new KeyboardHook();
                        }
                        _keyboardHook.SetHook();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error reinitializing keyboard hook: {ex.Message}");
                    }
                });

                Debug.WriteLine("Logout completed successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR in HandleLogout: {ex.Message}");
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateUI();

            lock (_timerLock)
            {
                if (_statusMonitoringTimer == null)
                {
                    InitializeStatusMonitoring();
                }
            }
        }
        public void loginBtn_Click(object sender, RoutedEventArgs e)
        {
            LoginAsync();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            try
            {
                if (_keyboardHook == null)
                {
                    _keyboardHook = new KeyboardHook();
                }

                _keyboardHook.SetHook();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting keyboard hook on activation: {ex.Message}");
            }
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);

            try
            {
                _keyboardHook?.Dispose();
                _keyboardHook = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error disposing keyboard hook on deactivation: {ex.Message}");
            }
        }
        public async Task LoginAsync()
        {
            string login_member_url = $"{SUPABASE_URL}/functions/v1/login-member";
            string apiKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InJ1YW5ld3licXhyZGZ2cmR5ZXFyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3Mzg1NDU3MzAsImV4cCI6MjA1NDEyMTczMH0.Sy_h_BHoN23rzRFpVc9ARN2wimJ8lRPEVh_hpw_7tlY";

            var requestData = new
            {
                password = textSitePassword.Password,
                membership_id = textMemberShipId.Text,
                ic_number = textIcNumber.Text,
                asset_id = SharedData.assetID,
                site_id = SharedData.siteID
            };

            loginBtn.IsEnabled = false;

            string jsonData = System.Text.Json.JsonSerializer.Serialize(requestData);

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

                try
                {
                    HttpResponseMessage response = await client.PostAsync(login_member_url, content);
                    var responseBody = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        try
                        {
                            var jsonDoc = JsonDocument.Parse(responseBody);
                            if (jsonDoc.RootElement.TryGetProperty("isMemberAccountActive", out JsonElement errorMemberAccountNotFound))
                            {
                                bool isActive = errorMemberAccountNotFound.GetBoolean();
                                if (!isActive)
                                {
                                    MessageBox.Show($"Member account is not active. Please proceed to activate");
                                    NavigateRegisterPage();
                                    return;
                                }
                            }
                            else if (jsonDoc.RootElement.TryGetProperty("isMemberActive", out JsonElement errorMemberNotFound))
                            {
                                bool isActive = errorMemberNotFound.GetBoolean();
                                if (!isActive)
                                {
                                    MessageBox.Show($"Member not found. Please proceed to register");
                                    NavigateRegisterPage();
                                    return;
                                }
                            }
                            else if (jsonDoc.RootElement.TryGetProperty("error", out JsonElement errorElement))
                            {
                                string errorMessage = errorElement.GetString() ?? "Unknown error occurred.";
                                MessageBox.Show($"{errorMessage}");
                            }
                            else
                            {
                                MessageBox.Show($"{responseBody}");
                            }
                        }
                        catch
                        {
                            MessageBox.Show($"{responseBody}");
                        }
                        return;
                    }

                    try
                    {
                        var jsonDoc = JsonDocument.Parse(responseBody);
                        if (jsonDoc.RootElement.TryGetProperty("bookingData", out JsonElement bookingElement))
                        {
                            SharedData.createdBy = bookingElement.GetProperty("requester_id").ToString();
                            var starttime = bookingElement.GetProperty("booking_start").ToString();
                            var endtime = bookingElement.GetProperty("booking_end").ToString();
                            DateTime startTime = DateTime.Parse(starttime);
                            DateTime endTime = DateTime.Parse(endtime);
                            DateTime curTime = DateTime.Now;

                            SharedData.duration = (int)endTime.Subtract(curTime).TotalMinutes;
                            _mainWindow.currentSessionMinutes = SharedData.duration;
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Login Failed: This PC has been booked by another member!");
                        return;
                    }

                    textMemberShipId.Clear();
                    textIcNumber.Clear();
                    textSitePassword.Clear();

                    // Make select IC is default visible
                    comboSelect.SelectedItem = optionIcNumber;
                    textIcNumber.Visibility = Visibility.Visible;
                    // Hide others
                    textMemberShipId.Visibility = Visibility.Collapsed;
                    textSitePassword.Visibility = Visibility.Collapsed;

                    Debug.WriteLine($"=== LOGIN SUCCESS - Duration: {SharedData.duration} minutes ===");

                    SharedData.logoutFlag = false;
                    SharedData.startFlag = 1; // This will trigger the monitoring timer

                    MessageBox.Show(Properties.Resources.msgSuccess);
                }
                catch (Exception hre)
                {
                    MessageBox.Show($"Login Failed: Something wrong while login!\n{hre.Message}");
                }
                finally
                {
                    loginBtn.IsEnabled = true;
                }
            }
        }

        private void StopStatusMonitoring()
        {
            lock (_timerLock)
            {
                if (_statusMonitoringTimer != null)
                {
                    _statusMonitoringTimer.Stop();
                    _statusMonitoringTimer.Tick -= StatusMonitoring_Tick;
                    _statusMonitoringTimer = null;
                    _isMonitoringActive = false;
                    Debug.WriteLine("=== STATUS MONITORING STOPPED ===");
                }
            }
        }

        //private void StartStatusMonitoring()
        //{
        //    var timer = new System.Windows.Threading.DispatcherTimer();
        //    timer.Interval = TimeSpan.FromMilliseconds(500);
        //    timer.Tick += async (s, e) =>
        //    {
        //        if (SharedData.startFlag == 1)
        //        {
        //            try
        //            {
        //                _keyboardHook?.Dispose();
        //                _keyboardHook = null;
        //            }
        //            catch (Exception ex)
        //            {
        //                Debug.WriteLine($"Error disposing keyboard hook: {ex.Message}");
        //            }
        //            _mainWindow.ResetMonitoringData();
        //            _mainWindow.Show();
        //            _mainWindow.timer?.Start();
        //            this.Hide();
        //            SharedData.logoutFlag = false;
        //            SharedData.startFlag = 2;
        //            _ = Task.Run(async () => await RunAsync());
        //        }
        //        else if (SharedData.logoutFlag)
        //        {
        //            Debug.WriteLine("Logout detected - hiding main window");
        //            _ = Task.Run(async () => await LogOutAsync());
        //            _mainWindow.timer?.Stop();
        //            _mainWindow.Hide();
        //            _mainWindow.CloseAllOtherApplications();
        //            this.Show();
        //            SharedData.logoutFlag = false;
        //            SharedData.startFlag = 3;
        //            try
        //            {
        //                if (_keyboardHook == null)
        //                {
        //                    _keyboardHook = new KeyboardHook();
        //                }
        //                _keyboardHook.SetHook();
        //            }
        //            catch (Exception ex)
        //            {
        //                Debug.WriteLine($"Error reinitializing keyboard hook: {ex.Message}");
        //            }
        //        }
        //    };
        //    timer.Start();
        //}



        private async Task LogOutAsync()
        {
            // Edge function URL
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

                    var wsClient = new ChannelAwareWebsocket();
                    await wsClient.EndSessionAsync(channelName, userId);

                    Debug.WriteLine("Logout API called successfully");
                }
                catch (Exception hre)
                {
                    MessageBox.Show($"Log out failed: {hre.Message} \n {hre.StackTrace}");
                }
            }
        }
        private async void RunRealtimeBroadcast()
        {
            try
            {
                var messenger = new RealtimeMessenger();
                await messenger.InitializeAsync($"remote-{SharedData.pcName}-{SharedData.siteID}");
            } catch (Exception ex)
            {
                Debug.WriteLine($"Realtime PC Messenger initialization error: {ex.Message}");
            }

            try
            {
                var siteMessager = new RealtimeMessenger();
                await siteMessager.InitializeAsync($"site-{SharedData.siteID}");
            } catch (Exception ex)
            {
                Debug.WriteLine($"Realtime Site Messenger initialization error: {ex.Message}");
            }

            _realTimeBroadCastTimer.Interval = 1000;
            _realTimeBroadCastTimer.Elapsed += async (s, e) => { };
            _realTimeBroadCastTimer.Start();
        }
        private void SetCulture(string cultureCode)
        {
            CultureInfo culture = new CultureInfo(cultureCode);
            Thread.CurrentThread.CurrentUICulture = culture;

            UpdateUI();
        }
        private void UpdateUI()
        {
            labelMemberShipId.Content = Properties.Resources.labelMembershipId;
            loginBtn.Content = Properties.Resources.loginBtn;
            // Display IC as default
            textIcNumber.Visibility = Visibility.Visible;
            // Hide others
            textMemberShipId.Visibility = Visibility.Collapsed;
            textSitePassword.Visibility = Visibility.Collapsed;

            _mainWindow.remainLabel.Content = Properties.Resources.remainLabel;
        }

        public static string DecryptStringFromBytes_Aes(byte[] cipherText, byte[] Key, byte[] IV)
        {
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Key;
                aesAlg.IV = IV;

                ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

                using (MemoryStream msDecrypt = new MemoryStream(cipherText))
                using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                {
                    return srDecrypt.ReadToEnd();
                }
            }
        }

        private void comboSelect_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (textMemberShipId == null || textIcNumber == null || textSitePassword == null) return;

            if (comboSelect.SelectedItem == optionMemberShipId)
            {
                textMemberShipId.Visibility = Visibility.Visible;
                textIcNumber.Visibility = Visibility.Collapsed;
                textSitePassword.Visibility = Visibility.Collapsed;

                textIcNumber.Clear();
                textSitePassword.Clear();
            }
            else if (comboSelect.SelectedItem == optionIcNumber)
            {
                textMemberShipId.Visibility = Visibility.Collapsed;
                textSitePassword.Visibility = Visibility.Collapsed;
                textIcNumber.Visibility = Visibility.Visible;

                textMemberShipId.Clear();
                textSitePassword.Clear();
            }
            else if (comboSelect.SelectedItem == optionSitePassword)
            {
                textSitePassword.Visibility = Visibility.Visible;
                textIcNumber.Visibility = Visibility.Collapsed;
                textMemberShipId.Visibility = Visibility.Collapsed;

                textMemberShipId.Clear();
                textIcNumber.Clear();
            }
        }

        private void RegisterLink_Click(object sender, RoutedEventArgs e)
        {
            NavigateRegisterPage();
        }

        private void NavigateRegisterPage()
        {
            // clear all input value first
            textMemberShipId.Clear();
            textIcNumber.Clear();
            textSitePassword.Clear();
            // Make select IC is default visible
            comboSelect.SelectedItem = optionIcNumber;
            textIcNumber.Visibility = Visibility.Visible;
            // Hide others
            textMemberShipId.Visibility = Visibility.Collapsed;
            textSitePassword.Visibility = Visibility.Collapsed;
            

            // Dispose keyboard hook
            try
            {
                _keyboardHook?.Dispose();
                _keyboardHook = null;
                Debug.WriteLine("Keyboard hook disposed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error disposing keyboard hook: {ex.Message}");
            }

            // Cleanup WebSocket connection
            _ = Task.Run(async () =>
            {
                try
                {
                    await CleanupConnection();
                    Debug.WriteLine("Connection cleanup completed");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in CleanupConnection: {ex.Message}");
                }
            });

            // CRITICAL FIX: Properly close ALL existing RegisterDialog instances
            var existingRegisterDialogs = Application.Current.Windows
                .OfType<RegisterDialog>()
                .ToList();

            if (existingRegisterDialogs.Any())
            {
                Debug.WriteLine($"Found {existingRegisterDialogs.Count} existing RegisterDialog(s), closing them...");

                foreach (var dialog in existingRegisterDialogs)
                {
                    try
                    {
                        // Force close without asking
                        dialog.Closing -= null;
                        dialog.Close();
                        Debug.WriteLine("Closed existing RegisterDialog");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error closing RegisterDialog: {ex.Message}");
                    }
                }

                // Give time for proper cleanup
                System.Threading.Thread.Sleep(200);
            }

            // Force garbage collection to ensure old dialogs are destroyed
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Use Dispatcher to ensure UI thread operations are safe
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // Create new RegisterDialog instance
                    Debug.WriteLine("Creating new RegisterDialog");
                    RegisterDialog registerDialog = new RegisterDialog();

                    // Show the new dialog
                    registerDialog.Show();
                    registerDialog.Activate();
                    registerDialog.Focus();

                    Debug.WriteLine("RegisterDialog shown successfully");

                    // Hide LoginDialog
                    this.Hide();
                    Debug.WriteLine("LoginDialog hidden");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error showing RegisterDialog: {ex.Message}");
                    MessageBox.Show($"Error opening registration dialog: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }), System.Windows.Threading.DispatcherPriority.Normal);
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            var hasRegisterDialog = Application.Current.Windows.OfType<RegisterDialog>().Any();
            var hasMainWindow = Application.Current.Windows.OfType<MainWindow>().Any();

            if (hasRegisterDialog || (hasMainWindow && SharedData.startFlag == 2))
            {
                e.Cancel = true;
                this.Hide();
                return;
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            try
            {
                // Stop monitoring
                StopStatusMonitoring();

                // Cancel all async operations
                _cancellationTokenSource?.Cancel();

                try
                {
                    _keyboardHook?.Dispose();
                    _keyboardHook = null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error disposing keyboard hook in Window_Closed: {ex.Message}");
                }

                // Clean up connections
                _ = Task.Run(async () =>
                {
                    await CleanupConnection();
                });

                // Dispose locks
                _connectionLock?.Dispose();
                _initializationLock?.Dispose();
                _cancellationTokenSource?.Dispose();

                var orphanedRegisterDialog = Application.Current.Windows.OfType<RegisterDialog>().FirstOrDefault();
                if (orphanedRegisterDialog != null)
                {
                    Debug.WriteLine("Cleaning up orphaned RegisterDialog");
                    orphanedRegisterDialog.Close();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error disposing resources: {ex.Message}");
            }

            if (Application.Current.MainWindow == this)
            {
                _mainWindow?.Close();
                _mainWindow = null;
            }
        }

        public static async Task RunAsync()
        {
            if (!await _connectionLock.WaitAsync(5000))
            {
                Debug.WriteLine("Could not acquire connection lock, another connection attempt in progress");
                return;
            }

            try
            {
                if (SharedData.startFlag != 2)
                {
                    Debug.WriteLine($"Not in logged-in state (startFlag={SharedData.startFlag}), aborting");
                    return;
                }

                if (_isReconnecting)
                {
                    Debug.WriteLine("Reconnection already in progress, skipping...");
                    return;
                }

                _isReconnecting = true;

                await CleanupConnection();

                await Task.Delay(1000);

                if (SharedData.startFlag != 2)
                {
                    Debug.WriteLine("State changed during cleanup, aborting");
                    return;
                }

                var wsClient = new ChannelAwareWebsocket();
                ws = await wsClient.ConnectToChannelAsync(channelName, userId, forceNewSession: true);
                ws.SslConfiguration.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;
                ws.SslConfiguration.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;

                ws.OnOpen += Ws_OnOpen;
                ws.OnMessage += Ws_OnMessage;
                ws.OnError += async (sender, e) =>
                {
                    Debug.WriteLine($"WebSocket Error: {e.Message}");
                    await CleanupConnection();
                };
                ws.OnClose += async (sender, e) =>
                {
                    Debug.WriteLine($"WebSocket Closed: {e.Code} - {e.Reason}");
                    if (e.Code != 1000) // Not normal closure
                    {
                        Debug.WriteLine($"Abnormal WebSocket closure, may need reconnection");
                    }
                };

                ws.Connect();

                // Wait for connection with timeout
                var timeout = DateTime.Now.AddSeconds(10);
                while (ws.ReadyState == WebSocketState.Connecting && DateTime.Now < timeout)
                {
                    await Task.Delay(100);
                }

                if (ws.ReadyState != WebSocketState.Open)
                {
                    throw new Exception("Failed to connect WebSocket within timeout");
                }

                Debug.WriteLine("WebSocket connected successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WebSocket connection error: {ex.Message}");
                await CleanupConnection();
                throw;
            }
            finally
            {
                _isReconnecting = false;
                _connectionLock.Release();
            }
        }
        private static void Ws_OnOpen(object sender, EventArgs e)
        {

            var joinedMsg = new
            {
                type = "join",
                body = new
                {
                    channelName,
                    userId
                }
            };
            ws.Send(Newtonsoft.Json.JsonConvert.SerializeObject(joinedMsg));
        }
        private static void Ws_OnMessage(object sender, MessageEventArgs e)
        {
            Debug.WriteLine($"WebSocket Message Received: {e.Data}");
            var message = Newtonsoft.Json.Linq.JObject.Parse(e.Data);
            Debug.WriteLine($"Received message: {message}");
            string type = message["type"]?.ToString();
            var body = message["body"];

            switch (type)
            {
                case "joined":
                    // WPF client joined channel
                    var users = body["users"];
                    if (users != null)
                    {
                        foreach (var user in users)
                        {
                            var userIdStr = user.ToString();
                            if (userIdStr.StartsWith("web"))
                            {
                                _hasWebClient = true;
                                Debug.WriteLine("Web client already in channel");
                            }
                        }
                    }

                    // Only initialize if web client is present
                    if (_hasWebClient)
                    {
                        InitialPeerConnection();
                    }
                    else
                    {
                        Debug.WriteLine("Waiting for web client to join...");
                    }
                    break;

                case "user_joined":
                    // Another user joined
                    var joinedUserId = body["userId"]?.ToString();
                    Debug.WriteLine($"User {joinedUserId} joined channel");

                    if (joinedUserId != null && joinedUserId.StartsWith("web"))
                    {
                        _hasWebClient = true;

                        // Clean up any existing connection first
                        if (pc != null)
                        {
                            Debug.WriteLine("Cleaning up old connection before initializing new one...");
                            _ = Task.Run(async () =>
                            {
                                await CleanupPeerConnectionOnly();
                                await Task.Delay(500); // Wait for cleanup
                                await InitialPeerConnection();
                            });
                        }
                        else
                        {
                            // No existing connection, initialize directly
                            InitialPeerConnection();
                        }
                    }
                    break;

                case "user_left":
                    // User left channel
                    var leftUserId = body["userId"]?.ToString();
                    Debug.WriteLine($"User {leftUserId} left channel");

                    if (leftUserId != null && leftUserId.StartsWith("web"))
                    {
                        _hasWebClient = false;
                        Debug.WriteLine("Web client disconnected - cleaning up peer connection");

                        _ = Task.Run(async () =>
                        {
                            await CleanupPeerConnectionOnly();
                            Debug.WriteLine("Ready for next web client connection");
                        });
                    }
                    break;

                case "offer_sdp_recieved":
                    HandleOffer(body);
                    break;

                case "ice_candidate_recieved":
                    HandleIceCandidate(body);
                    break;

                default:
                    break;
            }
        }

        private static async Task CleanupPeerConnectionOnly()
        {
            if (_isCleaningUp)
            {
                Debug.WriteLine("Cleanup already in progress, waiting...");
                int waitCount = 0;
                while (_isCleaningUp && waitCount < 50)
                {
                    await Task.Delay(100);
                    waitCount++;
                }
                return;
            }

            _isCleaningUp = true;
            Debug.WriteLine("Starting peer connection cleanup...");

            try
            {
                // 1. Stop video source
                if (_videoSource != null)
                {
                    try
                    {
                        await _videoSource.PauseVideo();
                        await Task.Delay(100);
                        await _videoSource.CloseVideo();
                        _videoSource.Dispose();
                        Debug.WriteLine("Video source cleaned up");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error closing video: {ex.Message}");
                    }
                    finally
                    {
                        _videoSource = null;
                    }
                }

                // 2. Stop audio source
                if (_audioSource != null)
                {
                    try
                    {
                        await _audioSource.CloseAudio();
                        Debug.WriteLine("Audio source cleaned up");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error closing audio: {ex.Message}");
                    }
                    finally
                    {
                        _audioSource = null;
                    }
                }

                // 3. Close peer connection
                if (pc != null)
                {
                    try
                    {
                        // Remove event handlers
                        try
                        {
                            pc.onicecandidate -= OnIceCandidate;
                            pc.onconnectionstatechange -= OnConnectionStateChange;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Warning: Could not remove event handlers: {ex.Message}");
                        }

                        pc.Close("cleanup_peer_only");
                        await Task.Delay(100);
                        pc.Dispose();
                        Debug.WriteLine("Peer connection cleaned up");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error closing peer connection: {ex.Message}");
                    }
                    finally
                    {
                        pc = null;
                    }
                }

                // 4. Clear ICE buffer
                iceBuffer.Clear();

                Debug.WriteLine("Peer connection cleanup completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during peer cleanup: {ex.Message}");
            }
            finally
            {
                _isCleaningUp = false;
            }
        }

        private static async Task InitialPeerConnection()
        {
            try
            {
                // Dispose existing connection if any
                if (pc != null)
                {
                    await CleanupPeerConnectionOnly();
                    await Task.Delay(500);
                }

                var config = new RTCConfiguration
                {
                    iceServers = new List<RTCIceServer>
                {
                    new RTCIceServer { urls = "stun:stun.l.google.com:19302" },
                    new RTCIceServer { urls = "stun:stun1.l.google.com:19302" }
                },
                    iceTransportPolicy = RTCIceTransportPolicy.all,
                    bundlePolicy = RTCBundlePolicy.max_bundle
                };

                pc = new RTCPeerConnection(config);
                if (pc == null)
                {
                    throw new Exception("Failed to initialize peer connection");
                }

                // Set up data channel with proper cleanup
                pc.ondatachannel += (dc) =>
                {
                    if (dc == null) return;

                    dc.onclose += () =>
                    {
                        Debug.WriteLine("Data channel closed");
                        // Cleanup
                        dc.onmessage -= null;
                        dc.onerror -= null;
                    };

                    dc.onerror += (error) => Debug.WriteLine($"Data channel error: {error}");

                    dc.onmessage += (dataChannel, protocol, data) =>
                    {
                        try
                        {
                            ProcessControlMessage(data);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error processing control message: {ex.Message}");
                        }
                    };
                };

                // Initialize FFmpeg
                SIPSorceryMedia.FFmpeg.FFmpegInit.Initialise(
                    SIPSorceryMedia.FFmpeg.FfmpegLogLevelEnum.AV_LOG_VERBOSE,
                    ffmpegLibFullPath,
                    _logger);

                // Initialize video
                _videoSource = new DxgiScreenCaptureSource(new FFmpegVideoEncoder());
                _videoSource.SetFrameRate(20);

                MediaStreamTrack track = new MediaStreamTrack(
                    _videoSource.GetVideoSourceFormats(),
                    MediaStreamStatusEnum.SendOnly);
                pc.addTrack(track);

                _videoSource.OnVideoSourceRawSample += MesasureTestPatternSourceFrameRate;
                _videoSource.OnVideoSourceEncodedSample += pc.SendVideo;
                pc.OnVideoFormatsNegotiated += (formats) =>
                    _videoSource.SetVideoSourceFormat(formats.First());

                // Initialize audio
                _audioSource = new AudioSource(new AudioEncoder(includeOpus: false));
                _audioSource.RestrictFormats(x => x.FormatName == "PCMU");
                _audioSource.OnAudioSourceEncodedSample += pc.SendAudio;

                MediaStreamTrack audioTrack = new MediaStreamTrack(
                    _audioSource.GetAudioSourceFormats(),
                    MediaStreamStatusEnum.SendOnly);
                pc.addTrack(audioTrack);
                pc.OnAudioFormatsNegotiated += (audioFormats) =>
                    _audioSource.SetAudioSourceFormat(audioFormats.First());

                // Event handlers
                pc.onicecandidate += OnIceCandidate;
                pc.onconnectionstatechange += OnConnectionStateChange;
                pc.oniceconnectionstatechange += (state) =>
                {
                    Debug.WriteLine($"🧊 ICE connection state changed to: {state}");
                };

                Debug.WriteLine("✅ Peer connection initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Error initializing peer connection: {ex.Message}");
                await CleanupConnection();
                throw;
            }
        }

        private static void ProcessControlMessage(byte[] data)
        {
            _inputSimulator = _inputSimulator ?? new InputSimulator();
            using var channelData = JsonDocument.Parse(data);
            string messageType = channelData.RootElement.GetProperty("type").GetString();
            if (channelData.RootElement.TryGetProperty("data", out JsonElement virtualEventData))
            {
                Debug.WriteLine($"Data channel message received: {messageType}");
                switch (messageType)
                {
                    case "mouse_move":
                        HandleMouseMove(virtualEventData);
                        break;
                    case "mouse_up":
                        HandleMouseUp(virtualEventData);
                        break;
                    case "mouse_down":
                        HandleMouseDown(virtualEventData);
                        break;
                    case "key_down":
                        HandleKeyDown(virtualEventData);
                        break;
                    case "key_up":
                        HandleKeyUp(virtualEventData);
                        break;
                }
            }
        }

        private static void HandleMouseMove(JsonElement data)
        {
            double normX = data.GetProperty("x").GetDouble();
            double normY = data.GetProperty("y").GetDouble();
            int x = (int)(normX * SystemParameters.PrimaryScreenWidth);
            int y = (int)(normY * SystemParameters.PrimaryScreenHeight);
            MoveCursor(x, y);
        }

        private static void HandleMouseUp(JsonElement data)
        {
            int buttonPosition = data.GetProperty("button").GetInt32();
            if (buttonPosition == 0)
                _inputSimulator.Mouse.LeftButtonUp();
            else if (buttonPosition == 2)
                _inputSimulator.Mouse.RightButtonUp();
        }

        private static void HandleMouseDown(JsonElement data)
        {
            int buttonPosition = data.GetProperty("button").GetInt32();
            if (buttonPosition == 0)
                _inputSimulator.Mouse.LeftButtonDown();
            else if (buttonPosition == 2)
                _inputSimulator.Mouse.RightButtonDown();
        }

        private static void HandleKeyDown(JsonElement data)
        {
            string key = data.GetProperty("key").GetString();
            if (!string.IsNullOrEmpty(key))
            {
                if (KeyMappings.TryGetValue(key, out VirtualKeyCode virtualKey))
                {
                    _inputSimulator.Keyboard.KeyDown(virtualKey);
                }
                else
                {
                    Debug.WriteLine($"Key mapping not found for: {key}");
                }
            }
        }

        private static void HandleKeyUp(JsonElement data)
        {
            string key = data.GetProperty("key").GetString();
            if (!string.IsNullOrEmpty(key))
            {
                if (KeyMappings.TryGetValue(key, out VirtualKeyCode virtualKey))
                {
                    _inputSimulator.Keyboard.KeyUp(virtualKey);
                }
                else if (key.Length == 1 && !IsModifierKey(key))
                {
                    _inputSimulator.Keyboard.TextEntry(key);
                }
                else
                {
                    Debug.WriteLine($"Key mapping not found for: {key}");
                }
            }
        }

        private static bool IsModifierKey(string key)
        {
            return key == "Shift" || key == "Control" || key == "Alt" || key == "Meta";
        }

        public static void MoveCursor(int x, int y)
        {
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            int absX = (int)(x * 65535 / screenWidth);
            int absY = (int)(y * 65535 / screenHeight);
            _inputSimulator.Mouse.MoveMouseTo(absX, absY);
        }

        private static void MesasureTestPatternSourceFrameRate(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
        {
            if (_startTime == DateTime.MinValue)
            {
                _startTime = DateTime.Now;
            }

            _frameCount++;

            if (DateTime.Now.Subtract(_startTime).TotalSeconds > 5)
            {
                double fps = _frameCount / DateTime.Now.Subtract(_startTime).TotalSeconds;
                _logger.LogDebug($"Frame rate {fps:0.##}fps.");
                _startTime = DateTime.Now;
                _frameCount = 0;
            }
        }

        private static void OnIceConnectionStateChange(RTCIceConnectionState state)
        {
            Debug.WriteLine($"🧊 ICE connection state changed to: {state}");
        }

        private async static void OnConnectionStateChange(RTCPeerConnectionState state)
        {
            Debug.WriteLine($"🔗 Connection state changed to: {state}");

            try
            {
                if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.disconnected)
                {
                    Debug.WriteLine("Connection failed/disconnected - cleaning up");

                    // Stop media sources immediately
                    if (_audioSource != null)
                    {
                        await _audioSource.PauseAudio();
                    }
                    if (_videoSource != null)
                    {
                        await _videoSource.PauseVideo();
                    }

                    // Don't do full cleanup here - let the reconnection logic handle it
                }
                else if (state == RTCPeerConnectionState.closed)
                {
                    Debug.WriteLine("Connection closed - stopping media sources");
                    if (_audioSource != null)
                        await _audioSource.CloseAudio();
                    if (_videoSource != null)
                    {
                        await _videoSource.CloseVideo();
                        _videoSource.Dispose();
                    }
                }
                else if (state == RTCPeerConnectionState.connected)
                {
                    Debug.WriteLine("Connection established - starting media sources");

                    // Initialize media sources if not already done
                    if (_audioSource != null)
                    {
                        try
                        {
                            await _audioSource.StartAudio();
                            Debug.WriteLine("Audio started successfully");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error starting audio: {ex.Message}");
                        }
                    }

                    if (_videoSource != null)
                    {
                        try
                        {
                            await _videoSource.StartVideo();
                            Debug.WriteLine("Video started successfully");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error starting video: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Error in connection state change handler: {ex.Message}");
            }
        }

        private static void OnIceCandidate(RTCIceCandidate candidate)
        {
            try
            {
                if (candidate != null && ws != null && ws.ReadyState == WebSocketState.Open)
                {
                    var candidateData = new
                    {
                        candidate = "candidate:" + candidate.candidate,
                        address = candidate.address,
                        component = candidate.component,
                        foundation = candidate.foundation,
                        priority = candidate.priority,
                        protocol = candidate.protocol,
                        relatedAddress = candidate.relatedAddress,
                        relatedPort = candidate.relatedPort,
                        sdpMLineIndex = candidate.sdpMLineIndex,
                        sdpMid = candidate.sdpMid,
                        tcpType = candidate.tcpType,
                        type = candidate.type,
                        usernameFragment = candidate.usernameFragment
                    };

                    Debug.Write($"Ready to send candidate: {candidateData}");

                    var msg = new
                    {
                        type = "send_ice_candidate",
                        body = new
                        {
                            channelName,
                            userId,
                            candidate = candidateData
                        }
                    };

                    var jsonSettings = new JsonSerializerSettings();
                    jsonSettings.Converters.Add(new IPAddressConverter());

                    var jsonMessage = Newtonsoft.Json.JsonConvert.SerializeObject(msg, Formatting.Indented, jsonSettings);
                    ws.Send(jsonMessage);
                    Debug.WriteLine($"ICE candidate sent: {candidate}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sending ICE candidate: {ex.Message}");
            }
        }

        private static async Task HandleOffer(dynamic body)
        {
            try
            {
                // Validate we have a peer connection
                if (pc == null)
                {
                    Debug.WriteLine("No peer connection available, initializing...");
                    await InitialPeerConnection();
                    await Task.Delay(500);

                    if (pc == null)
                    {
                        Debug.WriteLine("ERROR: Failed to initialize peer connection");
                        return;
                    }
                }

                // Check peer connection state
                if (pc.connectionState == RTCPeerConnectionState.closed ||
                    pc.connectionState == RTCPeerConnectionState.failed)
                {
                    Debug.WriteLine($"Peer connection in bad state: {pc.connectionState}, reinitializing...");
                    await CleanupPeerConnectionOnly();
                    await Task.Delay(500);
                    await InitialPeerConnection();
                    await Task.Delay(500);
                }

                Debug.WriteLine("Received offer from server.");

                // Parse body as JObject
                var bodyObj = body as Newtonsoft.Json.Linq.JObject;
                if (bodyObj == null)
                {
                    Debug.WriteLine("ERROR: Body is not a JObject");
                    return;
                }

                // Extract SDP - it could be nested in body["sdp"] or directly in body
                string sdp = null;
                var sdpData = bodyObj["sdp"];

                if (sdpData != null)
                {
                    if (sdpData is Newtonsoft.Json.Linq.JObject sdpObj)
                    {
                        sdp = sdpObj["sdp"]?.ToString();
                    }
                    else if (sdpData is Newtonsoft.Json.Linq.JValue)
                    {
                        sdp = sdpData.ToString();
                    }
                }

                if (string.IsNullOrEmpty(sdp))
                {
                    Debug.WriteLine("ERROR: No valid SDP found in offer");
                    return;
                }

                Debug.WriteLine($"SDP received: {sdp?.Substring(0, Math.Min(100, sdp.Length))}...");

                var offerDesc = new RTCSessionDescriptionInit
                {
                    sdp = sdp,
                    type = RTCSdpType.offer
                };

                // Set remote description
                pc.setRemoteDescription(offerDesc);
                Debug.WriteLine("✅ Remote description set successfully");

                // Process buffered ICE candidates
                if (iceBuffer.Count > 0)
                {
                    Debug.WriteLine($"Processing {iceBuffer.Count} buffered ICE candidates");
                    foreach (var candidate in iceBuffer)
                    {
                        try
                        {
                            pc.addIceCandidate(candidate);
                            Debug.WriteLine("✅ Added buffered ICE candidate");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"❌ Error adding buffered ICE candidate: {ex.Message}");
                        }
                    }
                    iceBuffer.Clear();
                }

                // Create and send answer
                var answer = pc.createAnswer();
                await pc.setLocalDescription(answer);
                Debug.WriteLine("✅ Answer created and local description set");

                // IMPORTANT: Send answer with correct format
                var answerMsg = new
                {
                    type = "send_answer",
                    body = new
                    {
                        channelName,
                        userId,
                        sdp = answer // Send the answer object directly, not nested
                    }
                };

                var jsonMessage = Newtonsoft.Json.JsonConvert.SerializeObject(answerMsg);
                ws.Send(jsonMessage);
                Debug.WriteLine("✅ Answer sent to server");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Error handling offer: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                // Try to recover by reinitializing
                await CleanupPeerConnectionOnly();
                await Task.Delay(500);
                Debug.WriteLine("Attempting to reinitialize after error...");
            }
        }
        private static async Task HandleIceCandidate(dynamic body)
        {
            try
            {
                Debug.WriteLine($"Received ICE candidate data: {body}");

                // Parse body as JObject for safer access
                var bodyObj = body as Newtonsoft.Json.Linq.JObject;
                if (bodyObj == null)
                {
                    Debug.WriteLine("Body is not a valid JObject");
                    return;
                }

                // ICE candidate data is inside body["candidate"]
                var candidateObj = bodyObj["candidate"] as Newtonsoft.Json.Linq.JObject;
                if (candidateObj == null)
                {
                    Debug.WriteLine("candidate field is missing or not a JObject");
                    return;
                }

                // Extract fields from the candidate object
                string candidate = candidateObj["candidate"]?.ToString();
                string sdpMid = candidateObj["sdpMid"]?.ToString();
                ushort? sdpMLineIndex = candidateObj["sdpMLineIndex"]?.Value<ushort?>();
                string usernameFragment = candidateObj["usernameFragment"]?.ToString();

                Debug.WriteLine($"Extracted - candidate: {candidate}");
                Debug.WriteLine($"Extracted - sdpMid: {sdpMid}");
                Debug.WriteLine($"Extracted - sdpMLineIndex: {sdpMLineIndex}");
                Debug.WriteLine($"Extracted - usernameFragment: {usernameFragment}");

                if (string.IsNullOrEmpty(candidate))
                {
                    Debug.WriteLine("Invalid or empty candidate string");
                    return;
                }

                var candidateInit = new RTCIceCandidateInit
                {
                    candidate = candidate,
                    sdpMid = sdpMid,
                    sdpMLineIndex = sdpMLineIndex ?? 0,
                    usernameFragment = usernameFragment
                };

                if (pc != null && pc.currentRemoteDescription != null)
                {
                    pc.addIceCandidate(candidateInit);
                    Debug.WriteLine("✅ ICE candidate added successfully");
                }
                else
                {
                    // Buffer the candidate until remote description is set
                    iceBuffer.Add(candidateInit);
                    Debug.WriteLine("📦 ICE candidate buffered (remote description not set yet)");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Error handling ICE candidate: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private static async Task CleanupConnection()
        {
            if (_isCleaningUp)
            {
                Debug.WriteLine("Cleanup already in progress, waiting...");
                int waitCount = 0;
                while (_isCleaningUp && waitCount < 50)
                {
                    await Task.Delay(100);
                    waitCount++;
                }
                return;
            }

            _isCleaningUp = true;
            Debug.WriteLine("Starting cleanup...");

            try
            {
                if (ws != null && ws.ReadyState == WebSocketState.Open)
                {
                    try
                    {
                        var quitMsg = new
                        {
                            type = "quit",
                            body = new { channelName, userId }
                        };

                        ws.Send(Newtonsoft.Json.JsonConvert.SerializeObject(quitMsg));

                        // Wait longer to ensure message is sent and processed
                        await Task.Delay(1000); // INCREASED from 200ms

                        Debug.WriteLine("Quit message sent successfully");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error sending quit message: {ex.Message}");
                    }
                }
                // 1. Stop and dispose video source
                if (_videoSource != null)
                {
                    try
                    {
                        _videoSource.OnVideoSourceRawSample -= MesasureTestPatternSourceFrameRate;
                        if (pc != null)
                        {
                            _videoSource.OnVideoSourceEncodedSample -= pc.SendVideo;
                        }

                        await _videoSource.PauseVideo();
                        await Task.Delay(200);
                        await _videoSource.CloseVideo();
                        _videoSource.Dispose();
                        Debug.WriteLine("Video source cleaned up");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error closing video: {ex.Message}");
                    }
                    finally
                    {
                        _videoSource = null;
                    }
                }

                // 2. Stop and dispose audio source
                if (_audioSource != null)
                {
                    try
                    {
                        // Unsubscribe events
                        if (pc != null)
                        {
                            _audioSource.OnAudioSourceEncodedSample -= pc.SendAudio;
                        }

                        await _audioSource.PauseAudio();
                        await Task.Delay(100);
                        await _audioSource.CloseAudio();
                        Debug.WriteLine("Audio source cleaned up");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error closing audio: {ex.Message}");
                    }
                    finally
                    {
                        _audioSource = null;
                    }
                }

                // 3. Close peer connection
                if (pc != null)
                {
                    try
                    {
                        // Unsubscribe ALL events
                        pc.onicecandidate -= OnIceCandidate;
                        pc.onconnectionstatechange -= OnConnectionStateChange;
                        pc.oniceconnectionstatechange -= OnIceConnectionStateChange;
                        pc.OnVideoFormatsNegotiated -= null;
                        pc.OnAudioFormatsNegotiated -= null;
                        pc.ondatachannel -= null;

                        pc.Close("cleanup");
                        await Task.Delay(200);
                        pc.Dispose();
                        Debug.WriteLine("Peer connection cleaned up");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error closing peer connection: {ex.Message}");
                    }
                    finally
                    {
                        pc = null;
                    }
                }

                // 4. Close WebSocket
                if (ws != null)
                {
                    try
                    {
                        // Unsubscribe events
                        ws.OnOpen -= Ws_OnOpen;
                        ws.OnMessage -= Ws_OnMessage;

                        if (ws.ReadyState == WebSocketState.Open)
                        {
                            var quitMsg = new
                            {
                                type = "quit",
                                body = new { channelName, userId }
                            };
                            ws.Send(Newtonsoft.Json.JsonConvert.SerializeObject(quitMsg));
                            await Task.Delay(200);
                            ws.Close(CloseStatusCode.Normal, "Client cleanup");
                        }
                        Debug.WriteLine("WebSocket closed");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error closing WebSocket: {ex.Message}");
                    }
                    finally
                    {
                        ws = null;
                    }
                }

                // 5. Clear buffers
                iceBuffer.Clear();

                // 6. Force GC collection untuk free memory
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                Debug.WriteLine("Cleanup completed successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during cleanup: {ex.Message}");
            }
            finally
            {
                _isCleaningUp = false;
            }
        }

        private static readonly Dictionary<string, VirtualKeyCode> KeyMappings = new()
{
    // Modifier keys
    { "Shift", VirtualKeyCode.SHIFT },
    { "Control", VirtualKeyCode.CONTROL },
    { "Alt", VirtualKeyCode.MENU },
    { "Meta", VirtualKeyCode.LWIN }, // Windows key
    
    // Arrow keys
    { "ArrowUp", VirtualKeyCode.UP },
    { "ArrowDown", VirtualKeyCode.DOWN },
    { "ArrowLeft", VirtualKeyCode.LEFT },
    { "ArrowRight", VirtualKeyCode.RIGHT },
    
    // Function keys
    { "F1", VirtualKeyCode.F1 },
    { "F2", VirtualKeyCode.F2 },
    { "F3", VirtualKeyCode.F3 },
    { "F4", VirtualKeyCode.F4 },
    { "F5", VirtualKeyCode.F5 },
    { "F6", VirtualKeyCode.F6 },
    { "F7", VirtualKeyCode.F7 },
    { "F8", VirtualKeyCode.F8 },
    { "F9", VirtualKeyCode.F9 },
    { "F10", VirtualKeyCode.F10 },
    { "F11", VirtualKeyCode.F11 },
    { "F12", VirtualKeyCode.F12 },
    
    // Special keys
    { "Enter", VirtualKeyCode.RETURN },
    { "Space", VirtualKeyCode.SPACE },
    { "Tab", VirtualKeyCode.TAB },
    { "Escape", VirtualKeyCode.ESCAPE },
    { "Backspace", VirtualKeyCode.BACK },
    { "Delete", VirtualKeyCode.DELETE },
    { "Home", VirtualKeyCode.HOME },
    { "End", VirtualKeyCode.END },
    { "PageUp", VirtualKeyCode.PRIOR },
    { "PageDown", VirtualKeyCode.NEXT },
    { "Insert", VirtualKeyCode.INSERT },
    { "CapsLock", VirtualKeyCode.CAPITAL },
    { "NumLock", VirtualKeyCode.NUMLOCK },
    { "ScrollLock", VirtualKeyCode.SCROLL },
    { "Pause", VirtualKeyCode.PAUSE },
    { "PrintScreen", VirtualKeyCode.SNAPSHOT },
    
    // Numbers
    { "0", VirtualKeyCode.VK_0 },
    { "1", VirtualKeyCode.VK_1 },
    { "2", VirtualKeyCode.VK_2 },
    { "3", VirtualKeyCode.VK_3 },
    { "4", VirtualKeyCode.VK_4 },
    { "5", VirtualKeyCode.VK_5 },
    { "6", VirtualKeyCode.VK_6 },
    { "7", VirtualKeyCode.VK_7 },
    { "8", VirtualKeyCode.VK_8 },
    { "9", VirtualKeyCode.VK_9 },
    
    // Letters
    { "a", VirtualKeyCode.VK_A }, { "A", VirtualKeyCode.VK_A },
    { "b", VirtualKeyCode.VK_B }, { "B", VirtualKeyCode.VK_B },
    { "c", VirtualKeyCode.VK_C }, { "C", VirtualKeyCode.VK_C },
    { "d", VirtualKeyCode.VK_D }, { "D", VirtualKeyCode.VK_D },
    { "e", VirtualKeyCode.VK_E }, { "E", VirtualKeyCode.VK_E },
    { "f", VirtualKeyCode.VK_F }, { "F", VirtualKeyCode.VK_F },
    { "g", VirtualKeyCode.VK_G }, { "G", VirtualKeyCode.VK_G },
    { "h", VirtualKeyCode.VK_H }, { "H", VirtualKeyCode.VK_H },
    { "i", VirtualKeyCode.VK_I }, { "I", VirtualKeyCode.VK_I },
    { "j", VirtualKeyCode.VK_J }, { "J", VirtualKeyCode.VK_J },
    { "k", VirtualKeyCode.VK_K }, { "K", VirtualKeyCode.VK_K },
    { "l", VirtualKeyCode.VK_L }, { "L", VirtualKeyCode.VK_L },
    { "m", VirtualKeyCode.VK_M }, { "M", VirtualKeyCode.VK_M },
    { "n", VirtualKeyCode.VK_N }, { "N", VirtualKeyCode.VK_N },
    { "o", VirtualKeyCode.VK_O }, { "O", VirtualKeyCode.VK_O },
    { "p", VirtualKeyCode.VK_P }, { "P", VirtualKeyCode.VK_P },
    { "q", VirtualKeyCode.VK_Q }, { "Q", VirtualKeyCode.VK_Q },
    { "r", VirtualKeyCode.VK_R }, { "R", VirtualKeyCode.VK_R },
    { "s", VirtualKeyCode.VK_S }, { "S", VirtualKeyCode.VK_S },
    { "t", VirtualKeyCode.VK_T }, { "T", VirtualKeyCode.VK_T },
    { "u", VirtualKeyCode.VK_U }, { "U", VirtualKeyCode.VK_U },
    { "v", VirtualKeyCode.VK_V }, { "V", VirtualKeyCode.VK_V },
    { "w", VirtualKeyCode.VK_W }, { "W", VirtualKeyCode.VK_W },
    { "x", VirtualKeyCode.VK_X }, { "X", VirtualKeyCode.VK_X },
    { "y", VirtualKeyCode.VK_Y }, { "Y", VirtualKeyCode.VK_Y },
    { "z", VirtualKeyCode.VK_Z }, { "Z", VirtualKeyCode.VK_Z }
};
    }

    public class IPAddressConverter : JsonConverter<IPAddress>
    {
        public override void WriteJson(JsonWriter writer, IPAddress value, Newtonsoft.Json.JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString()); // Serialize IPAddress to its string representation
        }

        public override IPAddress ReadJson(JsonReader reader, Type objectType, IPAddress existingValue, bool hasExistingValue, Newtonsoft.Json.JsonSerializer serializer)
        {
            var s = (string)reader.Value;
            return IPAddress.Parse(s); // Deserialize from string back to IPAddress
        }
    }

    public class DxgiScreenCaptureSource : IVideoSource, IDisposable
    {
        private const int VIDEO_SAMPLING_RATE = 90000;
        private const int MAXIMUM_FRAMES_PER_SECOND = 60;
        private const int DEFAULT_FRAMES_PER_SECOND = 30;
        private const int MINIMUM_FRAMES_PER_SECOND = 1;
        private const int TIMER_DISPOSE_WAIT_MILLISECONDS = 1000;
        private const int VP8_SUGGESTED_FORMAT_ID = 96;
        private const int H264_SUGGESTED_FORMAT_ID = 100;

        public static readonly List<VideoFormat> SupportedFormats = new List<VideoFormat>
        {
            new VideoFormat(VideoCodecsEnum.VP8, VP8_SUGGESTED_FORMAT_ID, VIDEO_SAMPLING_RATE),
            new VideoFormat(VideoCodecsEnum.H264, H264_SUGGESTED_FORMAT_ID, VIDEO_SAMPLING_RATE, "packetization-mode=1")
        };

        private int _frameSpacing;
        private System.Threading.Timer _captureTimer;
        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;
        private bool _isMaxFrameRate;
        private int _frameCount;
        private IVideoEncoder _videoEncoder;
        private MediaFormatManager<VideoFormat> _formatManager;

        private Factory1 _factory;
        private Adapter1 _adapter;
        private SharpDX.Direct3D11.Device _device;
        private Output _output;
        private Output1 _output1;
        private Texture2D _desktopTexture;
        private Texture2D _stagingTexture;
        private OutputDuplication _duplication;
        private Rectangle _captureArea;

        public event RawVideoSampleDelegate OnVideoSourceRawSample;
        public event RawVideoSampleFasterDelegate OnVideoSourceRawSampleFaster;
        public event EncodedSampleDelegate OnVideoSourceEncodedSample;
        public event SourceErrorDelegate OnVideoSourceError;

        public DxgiScreenCaptureSource(IVideoEncoder encoder = null)
        {
            if (encoder != null)
            {
                _videoEncoder = encoder;
                _formatManager = new MediaFormatManager<VideoFormat>(SupportedFormats);
            }

            try
            {
                _factory = new Factory1();
                _adapter = _factory.GetAdapter1(0);
                _device = new SharpDX.Direct3D11.Device(_adapter);

                _output = _adapter.GetOutput(0);
                _output1 = _output.QueryInterface<Output1>();

                var rawBounds = _output.Description.DesktopBounds;
                _captureArea = new System.Drawing.Rectangle(rawBounds.Left, rawBounds.Top, rawBounds.Right - rawBounds.Left, rawBounds.Bottom - rawBounds.Top);

                var width = _captureArea.Width;
                var height = _captureArea.Height;

                var duplication = _output1.DuplicateOutput(_device);

                var desktopTextureDesc = new Texture2DDescription
                {
                    CpuAccessFlags = CpuAccessFlags.None,
                    BindFlags = BindFlags.RenderTarget,
                    Format = Format.B8G8R8A8_UNorm,
                    Width = width,
                    Height = height,
                    OptionFlags = ResourceOptionFlags.None,
                    MipLevels = 1,
                    ArraySize = 1,
                    SampleDescription = { Count = 1, Quality = 0 },
                    Usage = ResourceUsage.Default
                };

                _desktopTexture = new Texture2D(_device, desktopTextureDesc);

                var stagingTextureDesc = new Texture2DDescription
                {
                    CpuAccessFlags = CpuAccessFlags.Read,
                    BindFlags = BindFlags.None,
                    Format = Format.B8G8R8A8_UNorm,
                    Width = width,
                    Height = height,
                    OptionFlags = ResourceOptionFlags.None,
                    MipLevels = 1,
                    ArraySize = 1,
                    SampleDescription = { Count = 1, Quality = 0 },
                    Usage = ResourceUsage.Staging
                };

                _stagingTexture = new Texture2D(_device, stagingTextureDesc);

                _duplication = duplication;

                _captureTimer = new System.Threading.Timer(CaptureAndProcessFrame, null, Timeout.Infinite, Timeout.Infinite);
                _frameSpacing = 1000 / DEFAULT_FRAMES_PER_SECOND;
            }
            catch (Exception ex)
            {
                OnVideoSourceError?.Invoke($"Failed to initialize DXGI screen capture: {ex.Message}");
                Dispose();
            }
        }

        public void RestrictFormats(Func<VideoFormat, bool> filter) => _formatManager.RestrictFormats(filter);
        public List<VideoFormat> GetVideoSourceFormats() => _formatManager.GetSourceFormats();
        public void SetVideoSourceFormat(VideoFormat videoFormat) => _formatManager.SetSelectedFormat(videoFormat);
        public List<VideoFormat> GetVideoSinkFormats() => _formatManager.GetSourceFormats();
        public void SetVideoSinkFormat(VideoFormat videoFormat) => _formatManager.SetSelectedFormat(videoFormat);

        public void ForceKeyFrame() => _videoEncoder?.ForceKeyFrame();
        public bool HasEncodedVideoSubscribers() => OnVideoSourceEncodedSample != null;

        public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat) =>
            throw new NotImplementedException("The DXGI screen capture source does not offer any encoding services for external sources.");

        public void ExternalVideoSourceRawSampleFaster(uint durationMilliseconds, RawImage rawImage) =>
            throw new NotImplementedException("The DXGI screen capture source does not offer any encoding services for external sources.");

        public Task<bool> InitialiseVideoSourceDevice() =>
            Task.FromResult(true);

        public bool IsVideoSourcePaused() => _isPaused;

        public void SetFrameRate(int framesPerSecond)
        {
            if (framesPerSecond < MINIMUM_FRAMES_PER_SECOND || framesPerSecond > MAXIMUM_FRAMES_PER_SECOND)
            {
                Debug.WriteLine("{FramesPerSecond} frames per second not in the allowed range of {MinimumFramesPerSecond} to {MaximumFramesPerSecond}, ignoring.", framesPerSecond, MINIMUM_FRAMES_PER_SECOND, MAXIMUM_FRAMES_PER_SECOND);
            }
            else
            {
                _frameSpacing = 1000 / framesPerSecond;

                if (_isStarted)
                {
                    _captureTimer.Change(0, _frameSpacing);
                }
            }
        }

        public void SetMaxFrameRate(bool isMaxFrameRate)
        {
            if (_isMaxFrameRate != isMaxFrameRate)
            {
                _isMaxFrameRate = isMaxFrameRate;

                if (_isStarted)
                {
                    if (_isMaxFrameRate)
                    {
                        _captureTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        GenerateMaxFrames();
                    }
                    else
                    {
                        _captureTimer.Change(0, _frameSpacing);
                    }
                }
            }
        }

        public Task PauseVideo()
        {
            _isPaused = true;
            _captureTimer.Change(Timeout.Infinite, Timeout.Infinite);
            return Task.CompletedTask;
        }

        public Task ResumeVideo()
        {
            _isPaused = false;
            _captureTimer.Change(0, _frameSpacing);
            return Task.CompletedTask;
        }

        public Task StartVideo()
        {
            if (!_isStarted)
            {
                _isStarted = true;
                if (_isMaxFrameRate)
                {
                    GenerateMaxFrames();
                }
                else
                {
                    _captureTimer.Change(0, _frameSpacing);
                }
            }
            return Task.CompletedTask;
        }

        public Task CloseVideo()
        {
            if (!_isClosed)
            {
                _isClosed = true;

                ManualResetEventSlim mre = new ManualResetEventSlim();
                _captureTimer?.Dispose(mre.WaitHandle);
                return Task.Run(() => mre.Wait(TIMER_DISPOSE_WAIT_MILLISECONDS));
            }
            return Task.CompletedTask;
        }

        private void GenerateMaxFrames()
        {
            DateTime lastGenerateTime = DateTime.Now;

            while (!_isClosed && _isMaxFrameRate)
            {
                _frameSpacing = Convert.ToInt32(DateTime.Now.Subtract(lastGenerateTime).TotalMilliseconds);
                CaptureAndProcessFrame(null);
                lastGenerateTime = DateTime.Now;
            }
        }

        private void CaptureAndProcessFrame(object state)
        {
            lock (_captureTimer)
            {
                if (_isClosed || (OnVideoSourceRawSample == null && OnVideoSourceEncodedSample == null))
                {
                    return;
                }

                _frameCount++;

                // Track whether we successfully acquired a frame
                bool frameAcquired = false;
                Texture2D texture2D = null;

                try
                {
                    // Acquire frame with timeout
                    var result = _duplication.TryAcquireNextFrame(100, out var frameInfo, out var desktopResource);

                    if (result.Success)
                    {
                        frameAcquired = true;

                        // Only process if we actually got a frame update
                        if (frameInfo.TotalMetadataBufferSize > 0 || frameInfo.LastPresentTime > 0)
                        {
                            texture2D = desktopResource.QueryInterface<Texture2D>();
                            _device.ImmediateContext.CopyResource(texture2D, _stagingTexture);

                            var dataBox = _device.ImmediateContext.MapSubresource(_stagingTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);

                            var width = _captureArea.Width;
                            var height = _captureArea.Height;
                            var rawPixelData = new byte[width * height * 4];
                            var dataPointer = dataBox.DataPointer;
                            var rowPitch = dataBox.RowPitch;

                            if (rowPitch == width * 4)
                            {
                                System.Runtime.InteropServices.Marshal.Copy(dataPointer, rawPixelData, 0, rawPixelData.Length);
                            }
                            else
                            {
                                for (int y = 0; y < height; y++)
                                {
                                    System.Runtime.InteropServices.Marshal.Copy(dataPointer + y * rowPitch, rawPixelData, y * width * 4, width * 4);
                                }
                            }

                            _device.ImmediateContext.UnmapSubresource(_stagingTexture, 0);

                            // Convert BGRA to I420 for encoding
                            var stride = width * 4;
                            var i420Buffer = PixelConverter.BGRAtoI420(rawPixelData, width, height, stride, 1);

                            if (OnVideoSourceRawSample != null)
                            {
                                OnVideoSourceRawSample.Invoke((uint)_frameSpacing, width, height, rawPixelData, VideoPixelFormatsEnum.Bgra);
                            }

                            if (_videoEncoder != null && OnVideoSourceEncodedSample != null && !_formatManager.SelectedFormat.IsEmpty())
                            {
                                var encodedBuffer = _videoEncoder.EncodeVideo(width, height, i420Buffer, VideoPixelFormatsEnum.I420, _formatManager.SelectedFormat.Codec);

                                if (encodedBuffer != null)
                                {
                                    uint fps = (_frameSpacing > 0) ? 1000 / (uint)_frameSpacing : DEFAULT_FRAMES_PER_SECOND;
                                    uint durationRtpTS = VIDEO_SAMPLING_RATE / fps;
                                    OnVideoSourceEncodedSample.Invoke(durationRtpTS, encodedBuffer);
                                }
                            }
                        }
                    }
                }
                catch (SharpDX.SharpDXException ex)
                {
                    // Handle specific DXGI errors
                    if (ex.ResultCode.Code == SharpDX.DXGI.ResultCode.WaitTimeout.Code)
                    {
                        // Don't log to reduce spam
                    }
                    else if (ex.ResultCode.Code == SharpDX.DXGI.ResultCode.AccessDenied.Code ||
                             ex.ResultCode.Code == SharpDX.DXGI.ResultCode.AccessLost.Code)
                    {
                        Debug.WriteLine("DXGI Access Lost. Recreating output duplication.");
                        RecreateDuplicationOutput();
                        frameAcquired = false;
                    }
                    else
                    {
                        Debug.WriteLine($"DXGI error during capture: {ex.ResultCode.Code:X} - {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Unexpected error during screen capture: {ex.Message}");
                }
                finally
                {
                    texture2D?.Dispose();

                    if (frameAcquired)
                    {
                        try
                        {
                            _duplication.ReleaseFrame();
                        }
                        catch (SharpDX.SharpDXException ex)
                        {
                            // If release fails, need to recreate duplication
                            if (ex.ResultCode.Code == SharpDX.DXGI.ResultCode.InvalidCall.Code)
                            {
                                Debug.WriteLine("Invalid ReleaseFrame call - recreating duplication");
                                RecreateDuplicationOutput();
                            }
                            else
                            {
                                Debug.WriteLine($"Error releasing frame: {ex.ResultCode.Code:X}");
                            }
                        }
                    }
                }

                if (_frameCount == int.MaxValue)
                {
                    _frameCount = 0;
                }
            }
        }

        private void RecreateDuplicationOutput()
        {
            try
            {
                // Dispose old duplication
                _duplication?.Dispose();

                // Small delay to ensure resources are freed
                System.Threading.Thread.Sleep(50);

                // Create new duplication
                _duplication = _output1.DuplicateOutput(_device);
                Debug.WriteLine("Output duplication recreated successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error recreating duplication: {ex.Message}");
                OnVideoSourceError?.Invoke($"Failed to recreate screen capture: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _isClosed = true;
            _captureTimer?.Dispose();
            _duplication?.Dispose();
            _stagingTexture?.Dispose();
            _desktopTexture?.Dispose();
            _output1?.Dispose();
            _output?.Dispose();
            _adapter?.Dispose();
            _device?.Dispose();
            _factory?.Dispose();
            _videoEncoder?.Dispose();
        }
    }

    public class AudioSource : IAudioSource
    {
        public const int AUDIO_SAMPLE_PERIOD_MILLISECONDS_DEFAULT = 20;
        public const int AUDIO_SAMPLE_PERIOD_MILLISECONDS_MIN = 20;
        public const int AUDIO_SAMPLE_PERIOD_MILLISECONDS_MAX = 500;

        private MediaFormatManager<AudioFormat> _audioFormatManager;
        private IAudioEncoder _audioEncoder;
        private bool _isStarted;
        private bool _isClosed;

        private WasapiLoopbackCapture _capture;

        public int AudioSamplePeriodMilliseconds { get; set; } = 20;

        public event EncodedSampleDelegate OnAudioSourceEncodedSample;
        public event Action<EncodedAudioFrame> OnAudioSourceEncodedFrameReady;
        public event SourceErrorDelegate OnAudioSourceError;
        public event SourceErrorDelegate OnAudioSinkError;

        [Obsolete("This audio source only produces encoded samples. Do not subscribe to this event.")]
        public event RawAudioSampleDelegate OnAudioSourceRawSample
        {
            add { }
            remove { }
        }

        public AudioSource()
        {
            _audioEncoder = new AudioEncoder();
            _audioFormatManager = new MediaFormatManager<AudioFormat>(_audioEncoder.SupportedFormats);
        }

        public AudioSource(IAudioEncoder audioEncoder, AudioSourceOptions audioOptions = null)
        {
            _audioEncoder = audioEncoder;
            _audioFormatManager = new MediaFormatManager<AudioFormat>(_audioEncoder.SupportedFormats);
        }

        public void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample)
        {
            EncodeAndSend(sample, (int)samplingRate);
        }

        public bool HasEncodedAudioSubscribers()
        {
            return OnAudioSourceEncodedSample != null;
        }

        public bool IsAudioSourcePaused()
        {
            return false;
        }

        public void RestrictFormats(Func<AudioFormat, bool> filter)
        {
            _audioFormatManager.RestrictFormats(filter);
        }

        public List<AudioFormat> GetAudioSourceFormats()
        {
            return _audioFormatManager.GetSourceFormats();
        }

        public void SetAudioSourceFormat(AudioFormat audioFormat)
        {
            _audioFormatManager.SetSelectedFormat(audioFormat);
        }

        public Task CloseAudio()
        {
            if (!_isClosed)
            {
                _isClosed = true;
                _capture?.StopRecording();
                _capture?.Dispose();
            }
            return Task.CompletedTask;
        }

        public Task StartAudio()
        {
            if (_audioFormatManager.SelectedFormat.IsEmpty())
            {
                throw new ApplicationException("The sending format for the Audio Source has not been set. Cannot start source.");
            }

            if (!_isStarted)
            {
                _isStarted = true;
                InitialiseLoopbackCapture();
            }
            return Task.CompletedTask;
        }

        public Task PauseAudio()
        {
            // Stop the capture to "pause"
            _capture?.StopRecording();
            return Task.CompletedTask;
        }

        public Task ResumeAudio()
        {
            // Restart the capture to "resume"
            _capture?.StartRecording();
            return Task.CompletedTask;
        }
        private void InitialiseLoopbackCapture()
        {
            if (_isClosed) return;

            _capture = new WasapiLoopbackCapture();

            _capture.WaveFormat = new WaveFormat(_audioFormatManager.SelectedFormat.ClockRate, 16, 1);

            _capture.DataAvailable += (s, e) =>
            {
                short[] pcm = new short[e.BytesRecorded / 2];
                System.Buffer.BlockCopy(e.Buffer, 0, pcm, 0, e.BytesRecorded);

                EncodeAndSend(pcm, _audioFormatManager.SelectedFormat.ClockRate);
            };

            _capture.StartRecording();
            Debug.WriteLine("Loopback audio capture started.");
        }

        private void EncodeAndSend(short[] pcm, int pcmSampleRate)
        {
            if (pcm.Length != 0)
            {
                byte[] array = _audioEncoder.EncodeAudio(pcm, _audioFormatManager.SelectedFormat);
                uint durationRtpUnits = (uint)AudioSamplePeriodMilliseconds.ToRtpUnits(_audioFormatManager.SelectedFormat.RtpClockRate);

                OnAudioSourceEncodedSample?.Invoke(durationRtpUnits, array);
                OnAudioSourceEncodedFrameReady?.Invoke(new EncodedAudioFrame(-1, _audioFormatManager.SelectedFormat, (uint)AudioSamplePeriodMilliseconds, array));
            }
        }
    }
}