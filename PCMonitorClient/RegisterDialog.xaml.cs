using dotenv.net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace PCMonitorClient
{
    /// <summary>
    /// Interaction logic for RegisterDialog.xaml
    /// </summary>
    public partial class RegisterDialog : Window
    {
        private bool _isNavigating = false;
        private static string SUPABASE_URL;

        public RegisterDialog()
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

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateUI();
        }

        private void UpdateUI()
        {
            // Hide label except ic number
            labelEmail.Visibility = Visibility.Collapsed;
            labelPassword.Visibility = Visibility.Collapsed;
            labelPhoneNumber.Visibility = Visibility.Collapsed;
            labelSiteName.Visibility = Visibility.Collapsed;
            labelFullName.Visibility = Visibility.Collapsed;
            labelGender.Visibility = Visibility.Collapsed;

            // Hide textbox except ic number
            textEmail.Visibility = Visibility.Collapsed;
            textPassword.Visibility = Visibility.Collapsed;
            textPhoneNumber.Visibility = Visibility.Collapsed;
            textSiteName.Visibility = Visibility.Collapsed;
            textFullName.Visibility = Visibility.Collapsed;
            GenderComboBox.Visibility = Visibility.Collapsed;

            // Hide button except ic Check
            registerBtn.Visibility = Visibility.Collapsed;
            newRegisterBtn.Visibility = Visibility.Collapsed;

            langSelect.Content = Properties.Resources.langSelect;
        }

        private async void checkIcBtn_Click(object sender, RoutedEventArgs e)
        {
            // Validation for IC number
            if (string.IsNullOrWhiteSpace(textIcNumber.Text))
            {
                MessageBox.Show("Please enter your IC number.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Edge function URL
                string register_member_url = $"{SUPABASE_URL}/functions/v1/check-ic-member";
                string apiKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InJ1YW5ld3licXhyZGZ2cmR5ZXFyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3Mzg1NDU3MzAsImV4cCI6MjA1NDEyMTczMH0.Sy_h_BHoN23rzRFpVc9ARN2wimJ8lRPEVh_hpw_7tlY";

                var requestData = new
                {
                    ic_no = textIcNumber.Text
                };

                string jsonData = System.Text.Json.JsonSerializer.Serialize(requestData);

                checkIcButton.IsEnabled = false;

                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

                    try
                    {
                        HttpResponseMessage response = await client.PostAsync(register_member_url, content);
                        response.EnsureSuccessStatusCode();
                        var responseBody = await response.Content.ReadAsStringAsync();
                        try
                        {
                            var jsonDoc = JsonDocument.Parse(responseBody);
                            if (jsonDoc.RootElement.TryGetProperty("isMemberExist", out JsonElement isMemberExist))
                            {
                                bool memberExist = isMemberExist.GetBoolean();

                                if (memberExist)
                                {
                                    // Activate account
                                    MessageBox.Show("IC number is exist, Please proceed to activate your account.");
                                    // Collapse IC number input and check button
                                    labelIcNumber.Visibility = Visibility.Collapsed;
                                    textIcNumber.Visibility = Visibility.Collapsed;
                                    labelGender.Visibility = Visibility.Collapsed;
                                    GenderComboBox.Visibility = Visibility.Collapsed;
                                    checkIcButton.Visibility = Visibility.Collapsed;
                                    labelSiteName.Visibility = Visibility.Collapsed;
                                    textSiteName.Visibility = Visibility.Collapsed;

                                    // Show the other
                                    labelEmail.Visibility = Visibility.Visible;
                                    textEmail.Visibility = Visibility.Visible;
                                    labelPassword.Visibility = Visibility.Visible;
                                    textPassword.Visibility = Visibility.Visible;
                                    labelPhoneNumber.Visibility = Visibility.Visible;
                                    textPhoneNumber.Visibility = Visibility.Visible;
                                    registerBtn.Visibility = Visibility.Visible;
                                } else
                                {
                                    // New register
                                    MessageBox.Show("IC number is not exist, Please proceed to register new member.");
                                    labelIcNumber.Visibility = Visibility.Collapsed;
                                    textIcNumber.Visibility = Visibility.Collapsed;
                                    checkIcButton.Visibility = Visibility.Collapsed;

                                    // Show the other
                                    labelEmail.Visibility = Visibility.Visible;
                                    textEmail.Visibility = Visibility.Visible;
                                    labelPassword.Visibility = Visibility.Visible;
                                    textPassword.Visibility = Visibility.Visible;
                                    labelPhoneNumber.Visibility = Visibility.Visible;
                                    textPhoneNumber.Visibility = Visibility.Visible;
                                    labelSiteName.Visibility = Visibility.Visible;
                                    textSiteName.Visibility = Visibility.Visible;
                                    labelFullName.Visibility = Visibility.Visible;
                                    textFullName.Visibility = Visibility.Visible;
                                    labelGender.Visibility = Visibility.Visible;
                                    GenderComboBox.Visibility = Visibility.Visible;
                                    newRegisterBtn.Visibility = Visibility.Visible;
                                }
                            }

                            if (jsonDoc.RootElement.TryGetProperty("hasEmail", out JsonElement hasEmail))
                            {
                                bool isMemberHasEmail = hasEmail.GetBoolean();

                                if (isMemberHasEmail)
                                {
                                    labelEmail.Visibility = Visibility.Collapsed;
                                    textEmail.Visibility = Visibility.Collapsed;
                                }
                            }

                            if (jsonDoc.RootElement.TryGetProperty("hasMobileNo", out JsonElement hasMobileNo))
                            {
                                bool isMemberHasMobileNo = hasMobileNo.GetBoolean();

                                if (isMemberHasMobileNo)
                                {
                                    labelPhoneNumber.Visibility = Visibility.Collapsed;
                                    textPhoneNumber.Visibility = Visibility.Collapsed;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Something error while checking ic!");
                        }

                    }
                    catch (Exception hre)
                    {
                        MessageBox.Show($"Failed: Something wrong while checking ic!");
                    }
                }
            }
            catch
            {
                MessageBox.Show($"Failed: Something error while checking ic!");
            }
            finally
            {
                checkIcButton.IsEnabled = true;
            }
        }

        private async void newRegisterBtn_Click(object sender, RoutedEventArgs e)
        {
            // Input validation
            if (string.IsNullOrWhiteSpace(textEmail.Text))
            {
                MessageBox.Show("Please enter your email.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(textPassword.Text))
            {
                MessageBox.Show("Please enter your password.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(textIcNumber.Text))
            {
                MessageBox.Show("Please enter your IC number.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(textPhoneNumber.Text))
            {
                MessageBox.Show("Please enter your phone number.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(textSiteName.Text))
            {
                MessageBox.Show("Please enter your site name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(textFullName.Text))
            {
                MessageBox.Show("Please enter your full name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            newRegisterBtn.IsEnabled = false;

            try
            {
                // Edge function URL
                string register_member_url = $"{SUPABASE_URL}/functions/v1/register-new-member";
                string apiKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InJ1YW5ld3licXhyZGZ2cmR5ZXFyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3Mzg1NDU3MzAsImV4cCI6MjA1NDEyMTczMH0.Sy_h_BHoN23rzRFpVc9ARN2wimJ8lRPEVh_hpw_7tlY";

                var selectedItem = GenderComboBox.SelectedItem as ComboBoxItem;
                int genderValue = int.Parse(selectedItem.Tag.ToString());

                var requestData = new
                {
                    email = textEmail.Text,
                    password = textPassword.Text,
                    ic_no = textIcNumber.Text,
                    phone_no = textPhoneNumber.Text,
                    site_name = textSiteName.Text,
                    gender = genderValue,
                    full_name = textFullName.Text
                };

                string jsonData = System.Text.Json.JsonSerializer.Serialize(requestData);

                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

                    try
                    {
                        HttpResponseMessage response = await client.PostAsync(register_member_url, content);
                        var responseBody = await response.Content.ReadAsStringAsync();

                        if (!response.IsSuccessStatusCode)
                        {
                            try
                            {
                                var jsonDoc = JsonDocument.Parse(responseBody);
                                if (jsonDoc.RootElement.TryGetProperty("error", out JsonElement errorElement))
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

                        // Clear input fields
                        textEmail.Clear();
                        textPassword.Clear();
                        textIcNumber.Clear();
                        textPhoneNumber.Clear();
                        textSiteName.Clear();
                        textFullName.Clear();

                        MessageBox.Show("Registration success, You can login now.");

                    }
                    catch (Exception hre)
                    {
                        MessageBox.Show($"Register Failed: Something wrong while register!");
                    }
                }
            }
            catch
            {
                MessageBox.Show($"Register Failed: Something wrong while register!");
            }
            finally
            {
                newRegisterBtn.IsEnabled = true;
            }
        }

        private async void registerBtn_Click(object sender, RoutedEventArgs e)
        {
            // Input validation
            if (string.IsNullOrWhiteSpace(textEmail.Text))
            {
                MessageBox.Show("Please enter your email.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(textPassword.Text))
            {
                MessageBox.Show("Please enter your password.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(textIcNumber.Text))
            {
                MessageBox.Show("Please enter your IC number.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(textPhoneNumber.Text))
            {
                MessageBox.Show("Please enter your phone number.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Disable button to prevent double submission
            registerBtn.IsEnabled = false;

            try
            {
                // Edge function URL
                string register_member_url = $"{SUPABASE_URL}/functions/v1/register-member";
                string apiKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InJ1YW5ld3licXhyZGZ2cmR5ZXFyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3Mzg1NDU3MzAsImV4cCI6MjA1NDEyMTczMH0.Sy_h_BHoN23rzRFpVc9ARN2wimJ8lRPEVh_hpw_7tlY";

                var requestData = new
                {
                    email = textEmail.Text,
                    password = textPassword.Text,
                    ic_number = textIcNumber.Text,
                    phone_number = textPhoneNumber.Text
                };

                string jsonData = System.Text.Json.JsonSerializer.Serialize(requestData);

                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

                    try
                    {
                        HttpResponseMessage response = await client.PostAsync(register_member_url, content);
                        var responseBody = await response.Content.ReadAsStringAsync();

                        if (!response.IsSuccessStatusCode)
                        {
                            try
                            {
                                var jsonDoc = JsonDocument.Parse(responseBody);
                                if (jsonDoc.RootElement.TryGetProperty("error", out JsonElement errorElement))
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

                        // Clear input fields
                        textEmail.Clear();
                        textPassword.Clear();
                        textIcNumber.Clear();
                        textPhoneNumber.Clear();

                        MessageBox.Show("Registration success, You can login now.");

                    }
                    catch (Exception hre)
                    {
                        MessageBox.Show($"Register Failed: Something wrong while register!");
                    }
                }
            }
            catch
            {
                MessageBox.Show($"Register Failed: Something wrong while register!");
            }
            finally
            {
                registerBtn.IsEnabled = true;
            }
        }

        private void loginLink_Click(object sender, RoutedEventArgs e)
        {
            var existingLogin = Application.Current.Windows.OfType<LoginDialog>().FirstOrDefault();

            if (existingLogin != null)
            {
                var hookField = typeof(LoginDialog).GetField("_keyboardHook",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (hookField != null)
                {
                    var hook = hookField.GetValue(existingLogin) as KeyboardHook;
                    if (hook == null)
                    {
                        hook = new KeyboardHook();
                        hookField.SetValue(existingLogin, hook);
                        hook.SetHook();
                    }
                }

                existingLogin.Show();
                existingLogin.Activate();
                this.Close();
            }
            else
            {
                LoginDialog loginDialog = new LoginDialog();
                loginDialog.Show();
                this.Close();
            }
        }

        private void langSelect_Click(object sender, RoutedEventArgs e)
        {
            if (langSelect.IsChecked == true)
            {
                SharedData.curCulture = "ms-MY";
            }
            else
            {
                SharedData.curCulture = "en";
            }
            SetCulture(SharedData.curCulture);
        }

        private void SetCulture(string cultureCode)
        {
            CultureInfo culture = new CultureInfo(cultureCode);
            Thread.CurrentThread.CurrentUICulture = culture;
            UpdateUI();
        }

        private void Window_Closed(object sender, EventArgs e)
        {

        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {

        }
    
    }
}
