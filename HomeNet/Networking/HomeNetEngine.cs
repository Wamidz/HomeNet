using System;
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

        public HomeNetEngine(Action<string> statusCallback, Action<string> urlCallback)
        {
            _statusCallback = statusCallback;
            _urlCallback = urlCallback;
        }

        public async Task<string> FetchPageAsync(string inputUrl, int redirectCount = 0)
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

                string httpRequest = $"GET {pathAndQuery} HTTP/1.1\r\n" +
                                     $"Host: {hostname}\r\n" +
                                     "User-Agent: HomeNet/0.1\r\n" +
                                     "Connection: close\r\n\r\n";

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
                            return await FetchPageAsync(redirectUrl, redirectCount + 1);
                        }
                    }
                }

                return fullResponse;
            }
        }
    }
}