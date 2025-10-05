using dotenv.net;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using WebSocketSharp;
using MessageBox = System.Windows.MessageBox;

namespace PCMonitorClient
{
    internal class ChannelAwareWebsocket
    {

        private static string[] instanceEndpoints;
        private static string httpUrl;
        private static readonly HttpClient httpClient = new HttpClient();
        private static CookieContainer cookieContainer = new CookieContainer();

        public ChannelAwareWebsocket()
        {
            try
            {
                DotEnv.Load();
                var envVars = DotEnv.Read();
                instanceEndpoints = [envVars["WSS_URL"] ?? ""];
                httpUrl = envVars["HTTP_SERVER_URL"] ?? "";
                if (string.IsNullOrEmpty(instanceEndpoints[0]) || string.IsNullOrEmpty(httpUrl))
                {
                    MessageBox.Show("supabase url or wss url not found");
                }
            }
            catch
            {
                Debug.WriteLine("No .env file found");
            }
        }

        private int HashString(string str)
        {
            int hash = 0;
            for (int i = 0; i < str.Length; i++)
            {
                int charCode = str[i];
                hash = ((hash << 5) - hash) + charCode;
                hash = hash & hash;
            }
            return Math.Abs(hash);
        }

        private async Task<string> PrepareSession(string channelName, string userId)
        {
            try
            {
                var requestData = new
                {
                    channelName = channelName,
                    userId = userId
                };

                var json = JsonSerializer.Serialize(requestData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var handler = new HttpClientHandler
                {
                    UseCookies = true,
                    CookieContainer = cookieContainer
                };

                using (var client = new HttpClient(handler))
                {
                    var response = await client.PostAsync($"{httpUrl}/api/prepare-session", content);

                    if (!response.IsSuccessStatusCode)
                    {
                        Debug.WriteLine($"Failed to prepare session: {response.StatusCode}");
                        return null;
                    }

                    var responseBody = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"Session prepared: {responseBody}");

                    var cookies = cookieContainer.GetCookies(new Uri(httpUrl));
                    string channelSessionCookie = null;

                    foreach (Cookie cookie in cookies)
                    {
                        Debug.WriteLine($"Cookie received: {cookie.Name}={cookie.Value}");
                        if (cookie.Name == "channel-session")
                        {
                            channelSessionCookie = cookie.Value;
                        }
                    }

                    return channelSessionCookie;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error preparing session: {ex.Message}");
                return null;
            }
        }

        private string GetInstanceForChannel(string channelName)
        {
            var hash = HashString(channelName);
            return instanceEndpoints[0];
        }

        public async Task<WebSocketSharp.WebSocket> ConnectToChannelAsync(string channelName, string userId)
        {
            Debug.WriteLine($"Preparing session for channel: {channelName}");

            var sessionCookie = await PrepareSession(channelName, userId);
            if (string.IsNullOrEmpty(sessionCookie))
            {
                throw new Exception("Failed to prepare session or get cookie");
            }

            await Task.Delay(200);

            var wsUrl = GetInstanceForChannel(channelName);
            Debug.WriteLine($"Connecting to WebSocket: {wsUrl}");
            var ws = new WebSocketSharp.WebSocket(wsUrl);

            ws.SetCookie(new WebSocketSharp.Net.Cookie("channel-session", sessionCookie)
            {
                Domain = new Uri(httpUrl).Host, // Extract domain from HTTP URL
                Path = "/"
            });

            Debug.WriteLine($"Cookie set for WebSocket: channel-session={sessionCookie}");

            return ws;
        }

        public WebSocketSharp.WebSocket ConnectToChannel(string channelName, string userId)
        {
            return ConnectToChannelAsync(channelName, userId).GetAwaiter().GetResult();
        }
    }
}
