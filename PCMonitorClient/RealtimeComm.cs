using dotenv.net;
using Newtonsoft.Json;
using Supabase.Realtime;
using Supabase.Realtime.Models;
using System.Diagnostics;
using MessageBox = System.Windows.MessageBox;

namespace PCMonitorClient
{
    public class RealtimeMessenger
    {
        private readonly Supabase.Client _supabase;
        private RealtimeChannel _channel;
        private RealtimeBroadcast<CommandBroadcast> _broadcast;

        private static string SUPABASE_URL;

        public RealtimeMessenger()
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
            var key = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJyb2xlIjoiYW5vbiIsImlzcyI6InN1cGFiYXNlIiwiaWF0IjoxNzU2Mjg3MDk5LCJleHAiOjE5MTM5NjcwOTl9.m-Fwhllu4DfPG7xP_u-k9ciL0C_ZluS59tOmu9zNzXE";
            var options = new Supabase.SupabaseOptions
            {
                AutoConnectRealtime = true
            };
            _supabase = new Supabase.Client(SUPABASE_URL, key, options);
            _supabase.InitializeAsync();
            _supabase.Realtime.ConnectAsync();
        }

        public async Task InitializeAsync(string channelName = "any")
        {
            _channel = _supabase.Realtime.Channel(channelName);
            _broadcast = _channel.Register<CommandBroadcast>();
            _broadcast.AddBroadcastEventHandler(async (sender, baseBroadcast) =>
            {
                var response = _broadcast.Current();
                if (response == null) return;
                var command = response.Payload["command"].ToString();
                //System.Windows.MessageBox.Show(command, response.Event);
                if (response.Event == "answer")
                {
                    Debug.WriteLine("Recieve event message");
                    if (command == null) return;
                    else if (command == "shutdown")
                    {
                        Process.Start("shutdown", "/s /t 0");
                    }
                    else if (command == "restart")
                    {
                        Process.Start("shutdown", "/r /t 0");
                    }
                    else if (command == "lock")
                    {
                        SharedData.logoutFlag = true;
                    }
                    else if (command == "unlock")
                    {
                        SharedData.startFlag = 1;
                    }
                }
            });
            await _channel.Subscribe();
            var channels = _supabase.Realtime.Subscriptions;
            Debug.WriteLine(channels.ToString());
        }

        public void DisconnectAsync()
        {
            if (_channel != null)
            {
                _channel.Unsubscribe();
            }
            _supabase.Realtime.Disconnect();
        }
    }

    public class CommandBroadcast : BaseBroadcast
    {
        [JsonProperty("command")]
        public required string Command { get; set; }
    }
}