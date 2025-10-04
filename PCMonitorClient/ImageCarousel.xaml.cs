using dotenv.net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PCMonitorClient.Controls
{
    public partial class ImageCarousel : UserControl
    {
        private List<string> _imageUrls = new List<string>();
        private int _currentIndex = 0;
        private DispatcherTimer _autoSlideTimer;
        private bool _isAutoSlideEnabled = true;
        private const int AUTO_SLIDE_INTERVAL = 5000; // 5 seconds
        private static string SUPABASE_URL;
        private const string API_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InJ1YW5ld3licXhyZGZ2cmR5ZXFyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3Mzg1NDU3MzAsImV4cCI6MjA1NDEyMTczMH0.Sy_h_BHoN23rzRFpVc9ARN2wimJ8lRPEVh_hpw_7tlY";

        public ImageCarousel()
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
            this.Loaded += ImageCarousel_Loaded;
            InitializeAutoSlideTimer();

            LoadPcInfo();
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

        private void LoadPcInfo()
        {
            try
            {
                // Set PC Name
                if (!string.IsNullOrEmpty(SharedData.pcName))
                {
                    PcNameText.Text = SharedData.pcName;
                }
                else
                {
                    PcNameText.Text = "Unknown PC";
                }

                // Set Site Name
                if (!string.IsNullOrEmpty(SharedData.siteName))
                {
                    SiteNameText.Text = SharedData.siteName;
                }
                else
                {
                    SiteNameText.Text = "No Site";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading PC info: {ex.Message}");
                PcNameText.Text = "PC Info";
                SiteNameText.Text = "Unavailable";
            }
        }

        private void InitializeAutoSlideTimer()
        {
            _autoSlideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(AUTO_SLIDE_INTERVAL)
            };
            _autoSlideTimer.Tick += (s, e) => NextImage();
        }

        private async void ImageCarousel_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadImagesFromAPI();
        }

        private async Task LoadImagesFromAPI()
        {
            try
            {
                ShowLoadingState();

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", API_KEY);

                    var requestData = new
                    {
                        site_id = SharedData.siteID
                    };

                    var jsonContent = JsonSerializer.Serialize(requestData);
                    var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                    var response = await client.PostAsync($"{SUPABASE_URL}/functions/v1/get-pc-images", content);
                    response.EnsureSuccessStatusCode();

                    var responseBody = await response.Content.ReadAsStringAsync();
                    var jsonDoc = JsonDocument.Parse(responseBody);

                    if (jsonDoc.RootElement.TryGetProperty("images", out JsonElement imagesElement))
                    {
                        _imageUrls.Clear();
                        foreach (var imageItem in imagesElement.EnumerateArray())
                        {
                            var imageUrl = imageItem.GetString();
                            if (!string.IsNullOrEmpty(imageUrl))
                            {
                                _imageUrls.Add(imageUrl);
                            }
                        }

                        if (_imageUrls.Count > 0)
                        {
                            _currentIndex = 0;
                            await DisplayCurrentImage();
                            SetupCarouselControls();
                            StartAutoSlide();
                        }
                        else
                        {
                            ShowErrorState("No images available for this site");
                        }
                    }
                    else
                    {
                        ShowErrorState("Invalid response format");
                    }
                }
            }
            catch (Exception ex)
            {
                ShowErrorState($"Failed to load images: {ex.Message}");
            }
        }

        private void ShowLoadingState()
        {
            LoadingPanel.Visibility = Visibility.Visible;
            ErrorPanel.Visibility = Visibility.Collapsed;
            PrevButton.Visibility = Visibility.Collapsed;
            NextButton.Visibility = Visibility.Collapsed;
            DotsPanel.Visibility = Visibility.Collapsed;
            CounterPanel.Visibility = Visibility.Collapsed;

            var storyboard = (Storyboard)this.Resources["LoadingAnimation"];
            storyboard.Begin();
        }

        private void ShowErrorState(string message)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            ErrorText.Text = message;
            PrevButton.Visibility = Visibility.Collapsed;
            NextButton.Visibility = Visibility.Collapsed;
            DotsPanel.Visibility = Visibility.Collapsed;
            CounterPanel.Visibility = Visibility.Collapsed;

            var storyboard = (Storyboard)this.Resources["LoadingAnimation"];
            storyboard.Stop();
        }

        private void SetupCarouselControls()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;

            // Show navigation controls only if more than 1 image
            if (_imageUrls.Count > 1)
            {
                PrevButton.Visibility = Visibility.Visible;
                NextButton.Visibility = Visibility.Visible;
                DotsPanel.Visibility = Visibility.Visible;
                CounterPanel.Visibility = Visibility.Visible;

                CreateDotIndicators();
                UpdateCounter();
            }
            else
            {
                PrevButton.Visibility = Visibility.Collapsed;
                NextButton.Visibility = Visibility.Collapsed;
                DotsPanel.Visibility = Visibility.Collapsed;
                CounterPanel.Visibility = Visibility.Collapsed;
            }

            var storyboard = (Storyboard)this.Resources["LoadingAnimation"];
            storyboard.Stop();
        }

        private async Task DisplayCurrentImage()
        {
            if (_imageUrls.Count == 0 || _currentIndex < 0 || _currentIndex >= _imageUrls.Count)
                return;

            try
            {
                var imageUrl = _imageUrls[_currentIndex];
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imageUrl);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                // Add fade animation
                var fadeOut = new DoubleAnimation(1.0, 0.3, TimeSpan.FromMilliseconds(150));
                var fadeIn = new DoubleAnimation(0.3, 1.0, TimeSpan.FromMilliseconds(150));

                fadeOut.Completed += (s, e) =>
                {
                    CurrentImage.Source = bitmap;
                    CurrentImage.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                };

                CurrentImage.BeginAnimation(UIElement.OpacityProperty, fadeOut);

                UpdateDotIndicators();
                UpdateCounter();
            }
            catch (Exception ex)
            {
                // Handle image loading error
                System.Diagnostics.Debug.WriteLine($"Error loading image: {ex.Message}");
            }
        }

        private void CreateDotIndicators()
        {
            DotsPanel.Children.Clear();

            for (int i = 0; i < _imageUrls.Count; i++)
            {
                var dot = new Button();
                dot.Style = (Style)this.Resources["DotIndicatorStyle"];
                dot.Tag = i;
                dot.Click += DotButton_Click;
                DotsPanel.Children.Add(dot);
            }
        }

        private void UpdateDotIndicators()
        {
            for (int i = 0; i < DotsPanel.Children.Count; i++)
            {
                var dot = (Button)DotsPanel.Children[i];
                if (i == _currentIndex)
                {
                    dot.Style = (Style)this.Resources["ActiveDotStyle"];
                }
                else
                {
                    dot.Style = (Style)this.Resources["DotIndicatorStyle"];
                }
            }
        }

        private void UpdateCounter()
        {
            if (_imageUrls.Count > 0)
            {
                CounterText.Text = $"{_currentIndex + 1} / {_imageUrls.Count}";
            }
        }

        private void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            PreviousImage();
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            NextImage();
        }

        private void DotButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button dot && dot.Tag is int index)
            {
                GoToImage(index);
            }
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            _ = LoadImagesFromAPI();
        }

        private async void NextImage()
        {
            if (_imageUrls.Count <= 1) return;

            _currentIndex = (_currentIndex + 1) % _imageUrls.Count;
            await DisplayCurrentImage();
        }

        private async void PreviousImage()
        {
            if (_imageUrls.Count <= 1) return;

            _currentIndex = (_currentIndex - 1 + _imageUrls.Count) % _imageUrls.Count;
            await DisplayCurrentImage();
        }

        private async void GoToImage(int index)
        {
            if (index < 0 || index >= _imageUrls.Count || index == _currentIndex) return;

            _currentIndex = index;
            await DisplayCurrentImage();
        }

        private void StartAutoSlide()
        {
            if (_isAutoSlideEnabled && _imageUrls.Count > 1)
            {
                _autoSlideTimer.Start();
            }
        }

        private void StopAutoSlide()
        {
            _autoSlideTimer.Stop();
        }

        // Public methods for external control
        public void SetAutoSlide(bool enabled)
        {
            _isAutoSlideEnabled = enabled;
            if (enabled)
            {
                StartAutoSlide();
            }
            else
            {
                StopAutoSlide();
            }
        }

        public void SetAutoSlideInterval(int milliseconds)
        {
            _autoSlideTimer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        }

        // Pause auto-slide on mouse enter, resume on mouse leave
        private void ImageCarousel_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            StopAutoSlide();
        }

        private void ImageCarousel_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            StartAutoSlide();
        }

        protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            StopAutoSlide();
        }

        protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            StartAutoSlide();
        }

        // Cleanup
        private void ImageCarousel_Unloaded(object sender, RoutedEventArgs e)
        {
            _autoSlideTimer?.Stop();
        }
    }
}

// Model classes for JSON deserialization
public class ImageResponse
{
    public List<ImageItem> images { get; set; }
}

public class ImageItem
{
    public string image_url { get; set; }
}