using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace PCMonitorClient
{
    /// <summary>
    /// Interaction logic for AlertWindow.xaml
    /// </summary>
    public partial class AlertWindow : Window
    {
        public AlertWindow(string message)
        {
            InitializeComponent();

            this.WindowStartupLocation = WindowStartupLocation.Manual;
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            this.Left = screenWidth - this.Width - 10;
            this.Top = screenHeight - this.Height;

            msgText.Text = message;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3));
            this.BeginAnimation(OpacityProperty, fadeIn);
            var moveUp = new DoubleAnimation(this.Top, this.Top - 50, TimeSpan.FromSeconds(0.3));
            this.BeginAnimation(Window.TopProperty, moveUp);
            //Task.Delay(5);
            //AlertFadeOut();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            AlertFadeOut();
        }

        public void AlertFadeOut()
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.3));
            fadeOut.Completed += (s, a) => this.Close();
            this.BeginAnimation(OpacityProperty, fadeOut);
        }
    }
}
