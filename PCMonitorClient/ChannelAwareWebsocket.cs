using dotenv.net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
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

        // Clear old cookies to ensure fresh session
        private void ClearSessionCookies()
        {
            try
            {
                // Create new cookie container to clear old cookies
                cookieContainer = new CookieContainer();
                Debug.WriteLine("[CLIENT] Session cookies cleared");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CLIENT] Error clearing cookies: {ex.Message}");
            }
        }

        private async Task<SessionResult> PrepareSession(string channelName, string userId, bool forceNew = true)
        {
            try
            {
                // Clear old cookies FIRST to ensure fresh session
                if (forceNew)
                {
                    ClearSessionCookies();
                    Debug.WriteLine("[CLIENT] Forcing new session creation");
                }

                var requestData = new
                {
                    channelName = channelName,
                    userId = userId,
                    forceNew = forceNew  // Tell server to create new session
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
                        Debug.WriteLine($"[CLIENT] Failed to prepare session: {response.StatusCode}");
                        return null;
                    }

                    var responseBody = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"[CLIENT] Session prepared: {responseBody}");

                    // Parse response
                    var responseObject = JsonSerializer.Deserialize<JsonElement>(responseBody);
                    string channelNameFromResponse = responseObject.GetProperty("channelName").GetString();

                    // Get cookies
                    var cookies = cookieContainer.GetCookies(new Uri(httpUrl));
                    string channelSessionCookie = null;

                    foreach (Cookie cookie in cookies)
                    {
                        Debug.WriteLine($"[CLIENT] Cookie received: {cookie.Name}={cookie.Value}");
                        if (cookie.Name == "channel-session")
                        {
                            channelSessionCookie = cookie.Value;
                        }
                    }

                    Debug.WriteLine($"[CLIENT] Extracted channel-session cookie: {channelSessionCookie}");

                    if (string.IsNullOrEmpty(channelSessionCookie) && forceNew)
                    {
                        Debug.WriteLine("[CLIENT] Warning: No session cookie received, may cause connection issues");
                    }

                    return new SessionResult
                    {
                        ChannelSessionCookie = channelSessionCookie,
                        ChannelName = channelNameFromResponse
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CLIENT] Error preparing session: {ex.Message}");
                return null;
            }
        }

        private string GetInstanceForChannel(string channelName)
        {
            var hash = HashString(channelName);
            return instanceEndpoints[0];
        }

        public async Task<WebSocketSharp.WebSocket> ConnectToChannelAsync(
            string channelName,
            string userId,
            bool forceNewSession = true)
        {
            var sessionResult = await PrepareSession(channelName, userId, forceNewSession);

            if (sessionResult == null ||
                string.IsNullOrEmpty(sessionResult.ChannelSessionCookie) ||
                string.IsNullOrEmpty(sessionResult.ChannelName))
            {
                throw new Exception("Failed to prepare session or get cookie");
            }

            Debug.WriteLine($"[CLIENT] Channel from API: {sessionResult.ChannelName}");

            // Wait for session to be fully initialized
            await Task.Delay(500);

            var wsUrl = GetInstanceForChannel(channelName);
            var wsFullUrl = $"{wsUrl}?session={Uri.EscapeDataString(sessionResult.ChannelSessionCookie)}&channel={Uri.EscapeDataString(sessionResult.ChannelName)}";

            Debug.WriteLine($"[CLIENT] Connecting to WebSocket: {wsFullUrl}");

            var ws = new WebSocketSharp.WebSocket(wsFullUrl);

            // Set cookie explicitly
            ws.SetCookie(new WebSocketSharp.Net.Cookie("channel-session", sessionResult.ChannelSessionCookie)
            {
                Domain = new Uri(httpUrl).Host,
                Path = "/",
                HttpOnly = false,
                Expires = DateTime.Now.AddMinutes(60)
            });

            Debug.WriteLine($"[CLIENT] WebSocket cookie host: {new Uri(httpUrl).Host}");
            Debug.WriteLine($"[CLIENT] Cookie set for WebSocket: channel-session={sessionResult.ChannelSessionCookie}");

            return ws;
        }

        public WebSocketSharp.WebSocket ConnectToChannel(string channelName, string userId, bool forceNewSession = true)
        {
            return ConnectToChannelAsync(channelName, userId, forceNewSession).GetAwaiter().GetResult();
        }

        // Method to end session and clear cookies when logging out
        public async Task EndSessionAsync(string channelName, string userId)
        {
            try
            {
                Debug.WriteLine("[CLIENT] Ending session and clearing cookies...");

                // Notify server to invalidate session
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
                    var response = await client.PostAsync($"{httpUrl}/api/end-session", content);

                    if (response.IsSuccessStatusCode)
                    {
                        Debug.WriteLine("[CLIENT] Session ended on server");
                    }
                    else
                    {
                        Debug.WriteLine($"[CLIENT] Failed to end session: {response.StatusCode}");
                    }
                }

                // Clear cookies locally
                ClearSessionCookies();

                Debug.WriteLine("[CLIENT] Session cleanup completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CLIENT] Error ending session: {ex.Message}");
            }
        }
    }

    public class SessionResult
    {
        public string ChannelSessionCookie { get; set; }
        public string ChannelName { get; set; }
    }
}