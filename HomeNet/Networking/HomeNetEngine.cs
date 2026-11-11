using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace HomeNet.Networking
{
    public class HomeNetEngine
    {
        private readonly Action<string> _statusCallback;
        private readonly Action<string> _urlCallback;
        private readonly Action<string> _htmlCallback;

        public HomeNetEngine(Action<string> statusCallback = null, Action<string> urlCallback = null, Action<string> htmlCallback = null)
        {
            _statusCallback = statusCallback ?? (_ => { });
            _urlCallback = urlCallback ?? (_ => { });
            _htmlCallback = htmlCallback ?? (_ => { });
        }

        private string GetUserAgent()
        {
            string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(2) ?? "0.1";
            return $"HomeNet/{version}";
        }

        public async void LoadUrl(string url)
        {
            try
            {
                string html = await FetchPageAsync(url, null, "GET");
                _htmlCallback(html);
            }
            catch (Exception ex)
            {
                _statusCallback($"Error: {ex.Message}");
            }
        }

        public async void LoadUrlWithPost(string url, Dictionary<string, string> postData)
        {
            try
            {
                string html = await FetchPageAsync(url, postData, "POST");
                _htmlCallback(html);
            }
            catch (Exception ex)
            {
                _statusCallback($"Error: {ex.Message}");
            }
        }

        public async Task<string> FetchPageAsync(string inputUrl, Dictionary<string, string> postData = null, string method = "GET", int redirectCount = 0)
        {
            if (redirectCount > 5)
            {
                throw new Exception("Too many redirects (infinite loop detected).");
            }

            if (inputUrl.StartsWith("//"))
            {
                inputUrl = "https:" + inputUrl;
            }
            else if (!inputUrl.StartsWith("http://") && !inputUrl.StartsWith("https://"))
            {
                inputUrl = "https://" + inputUrl;
            }

            Uri targetUri = new Uri(inputUrl);
            string hostname = targetUri.Host;
            int port = targetUri.Port;
            string pathAndQuery = targetUri.PathAndQuery;

            _statusCallback($"Resolving DNS for {hostname}...");
            IPAddress[] resolvedAddresses = await Dns.GetHostAddressesAsync(hostname);
            IPAddress targetIP = resolvedAddresses[0];

            using (TcpClient client = new TcpClient())
            {
                _statusCallback($"Connecting to {targetIP} on port {port}...");
                await client.ConnectAsync(targetIP, port);

                Stream networkStream = client.GetStream();

                if (port == 443)
                {
                    _statusCallback("Port 443 detected. Initiating secure SSL/TLS Handshake...");
                    SslStream secureStream = new SslStream(networkStream, false);
                    await secureStream.AuthenticateAsClientAsync(hostname);
                    networkStream = secureStream;
                    _statusCallback("Secure HTTPS connection established.");
                }
                else
                {
                    _statusCallback("Port 80 detected. Proceeding with unsecure HTTP connection...");
                }

                string userAgent = GetUserAgent();
                string httpRequest;

                if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) && postData != null)
                {
                    // Build URL-encoded body payload for POST requests
                    StringBuilder bodyBuilder = new StringBuilder();
                    foreach (var kvp in postData)
                    {
                        if (bodyBuilder.Length > 0) bodyBuilder.Append("&");
                        bodyBuilder.Append($"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}");
                    }
                    string bodyContent = bodyBuilder.ToString();

                    httpRequest = $"POST {pathAndQuery} HTTP/1.1\r\n" +
                                  $"Host: {hostname}\r\n" +
                                  $"User-Agent: {userAgent}\r\n" +
                                  "Content-Type: application/x-www-form-urlencoded\r\n" +
                                  $"Content-Length: {Encoding.UTF8.GetByteCount(bodyContent)}\r\n" +
                                  "Connection: close\r\n\r\n" +
                                  bodyContent;
                }
                else
                {
                    httpRequest = $"GET {pathAndQuery} HTTP/1.1\r\n" +
                                  $"Host: {hostname}\r\n" +
                                  $"User-Agent: {userAgent}\r\n" +
                                  "Connection: close\r\n\r\n";
                }

                byte[] requestBytes = Encoding.ASCII.GetBytes(httpRequest);
                await networkStream.WriteAsync(requestBytes, 0, requestBytes.Length);

                _statusCallback("HTTP packet sent. Awaiting server response...");

                StringBuilder responseBuilder = new StringBuilder();
                byte[] buffer = new byte[4096];
                int bytesRead;

                while ((bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    string chunkText = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    responseBuilder.Append(chunkText);
                }

                string fullResponse = responseBuilder.ToString();

                if (fullResponse.StartsWith("HTTP/1.1 301") ||
                    fullResponse.StartsWith("HTTP/1.1 302") ||
                    fullResponse.StartsWith("HTTP/1.1 303") ||
                    fullResponse.StartsWith("HTTP/1.1 307") ||
                    fullResponse.StartsWith("HTTP/1.1 308"))
                {
                    string[] lines = fullResponse.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    foreach (string line in lines)
                    {
                        if (line.StartsWith("Location:", StringComparison.OrdinalIgnoreCase))
                        {
                            string redirectUrl = line.Substring("Location:".Length).Trim();

                            if (Uri.TryCreate(targetUri, redirectUrl, out Uri absoluteRedirectUri))
                            {
                                redirectUrl = absoluteRedirectUri.ToString();
                            }

                            _statusCallback($"Redirect detected! Following to: {redirectUrl}...");
                            _urlCallback(redirectUrl);
                            await Task.Delay(1000);
                            return await FetchPageAsync(redirectUrl, postData, method, redirectCount + 1);
                        }
                    }
                }

                return fullResponse;
            }
        }
    }
}