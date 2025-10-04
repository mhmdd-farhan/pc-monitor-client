using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using dotenv.net;
using MessageBox = System.Windows.MessageBox;

namespace PCMonitorClient
{
    /// <summary>
    /// Interaction logic for Register.xaml
    /// </summary>
    public partial class Register : Window
    {
        private KeyboardHook _keyboardHook;
        private static string SUPABASE_URL;
        public Register()
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
            _keyboardHook = new KeyboardHook();
            InitializeComponent();
        }

        private void registerBtn_Click(object sender, RoutedEventArgs e)
        {
            //var data = new PCData { PCName = pcNameBox.Text, SiteID = int.Parse(siteIDBox.Text) };
            //string jsonString = JsonSerializer.Serialize(data);

            LoginAsync();
        }

        public async Task LoginAsync()
        {
            // Edge function URL
            string login_pc_url = $"{SUPABASE_URL}/functions/v1/login-pc";

            // API key or token (if needed)
            string apiKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InJ1YW5ld3licXhyZGZ2cmR5ZXFyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3Mzg1NDU3MzAsImV4cCI6MjA1NDEyMTczMH0.Sy_h_BHoN23rzRFpVc9ARN2wimJ8lRPEVh_hpw_7tlY";

            // Data to include in the request (e.g., JSON)
            var requestData = new
            {
                pc_name = pcNameBox.Text,
                site_code = siteCodeBox.Text
            };
            // Serialize to JSON string
            string jsonData = JsonSerializer.Serialize(requestData);

            registerBtn.IsEnabled = false;

            // Create HttpClient instance
            using (HttpClient client = new HttpClient())
            {
                // Add Authorization header (if needed)
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                // Create HttpContent with JSON data
                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

                try
                {
                    // Send POST request
                    HttpResponseMessage response = await client.PostAsync(login_pc_url, content);
                    response.EnsureSuccessStatusCode();

                    // Read and output the response
                    var responseBody = await response.Content.ReadAsStringAsync();
 
                    var jsonDoc = JsonDocument.Parse(responseBody);
                    int pcID = 0;
                    int siteID = 0;
                    string siteName = "";
                    if (jsonDoc.RootElement.TryGetProperty("pcData", out JsonElement pcElement))
                    {
                        pcID = int.Parse(pcElement.GetProperty("id").ToString());
                        siteID = int.Parse(pcElement.GetProperty("site_id").ToString());
                    }
                    if (jsonDoc.RootElement.TryGetProperty("siteData", out JsonElement siteElement))
                    {
                        siteName = siteElement.GetProperty("sitename").ToString();
                    }
                    var pcData = new
                    {
                        pc_id = pcID,
                        pc_name = pcNameBox.Text,
                        site_id = siteID,
                        site_code = siteCodeBox.Text,
                        site_name = siteName
                    };
                    jsonData = JsonSerializer.Serialize(pcData);
                    byte[] encryptedBytes = EncryptStringToBytes_Aes(jsonData, SharedData.key, SharedData.iv);

                    File.WriteAllBytes("Settings.dll", encryptedBytes);
                    
                    MessageBox.Show(Properties.Resources.msgSuccess);

                    this.Close();
                }
                catch (HttpRequestException hre)
                {
                    MessageBox.Show(Properties.Resources.msgFaild);
                }
                finally
                {
                    registerBtn.IsEnabled = true;
                }
            }
        }

        public static byte[] EncryptStringToBytes_Aes(string plainText, byte[] Key, byte[] IV)
        {
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Key;
                aesAlg.IV = IV;

                ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                    {
                        swEncrypt.Write(plainText);
                    }
                    return msEncrypt.ToArray();
                }
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (File.Exists("Settings.dll") == false) e.Cancel = true;
            _keyboardHook.Dispose();
        }
    }
    public class PCData
    {
        public required int pc_id { get; set; }
        public required string pc_name { get; set; }

        public required string site_name { get; set; }
        public required int site_id { get; set; }
        public required string site_code { get; set; }
    }
}