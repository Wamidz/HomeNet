using System.IO;
using System.Threading.Tasks;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Net.Sockets;
using System.Net.Security;

namespace HomeNet
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void searchButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string input = searchBar.Text.Trim();
                if (!input.StartsWith("http://") && !input.StartsWith("https://"))
                {
                    input = "https://" + input;
                }

                Uri targetUri = new Uri(input);
                string uniformResourceLocator = targetUri.Host;
                int targetPort = targetUri.Port;

                renderField.Text = $"Resolving DNS for {uniformResourceLocator}...";
                IPAddress[] resolvedAddresses = await Dns.GetHostAddressesAsync(uniformResourceLocator);
                IPAddress targetIP = resolvedAddresses[0];

                renderField.Text = $"The numeric IP is: {targetIP}";
                await Task.Delay(1000);

                using (TcpClient client = new TcpClient())
                {
                    renderField.Text = $"Connecting to {targetIP} on port {targetPort}...";
                    await client.ConnectAsync(targetIP, targetPort);

                    Stream networkStream = client.GetStream();

                    if (targetPort == 443)
                    {
                        renderField.Text = "Port 443 detected. Initiating secure SSL/TLS Handshake...";
                        await Task.Delay(500);

                        SslStream secureStream = new SslStream(networkStream, false);
                        await secureStream.AuthenticateAsClientAsync(uniformResourceLocator);

                        networkStream = secureStream;
                        renderField.Text = "Secure HTTPS connection established.";
                    }
                    else
                    {
                        renderField.Text = "Port 80 detected. Proceeding with unsecure HTTP connection...";
                    }
                    await Task.Delay(1000);

                    string httpRequest = "GET / HTTP/1.1\r\n" +
                                         $"Host: {uniformResourceLocator}\r\n" +
                                         "User-Agent: HomeNet/0.1\r\n" +
                                         "Connection: close\r\n\r\n";

                    byte[] requestBytes = Encoding.ASCII.GetBytes(httpRequest);
                    await networkStream.WriteAsync(requestBytes, 0, requestBytes.Length);

                    renderField.Text = "HTTP packet sent. Awaiting server response...";
                    await Task.Delay(1000);

                    StringBuilder responseBuilder = new StringBuilder();
                    byte[] buffer = new byte[4096];
                    int bytesRead;

                    while ((bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        string chunkText = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                        responseBuilder.Append(chunkText);
                    }

                    renderField.Text = responseBuilder.ToString();
                }
            }
            catch (Exception ex)
            {
                renderField.Text = $"Network Error: {ex.Message}";
            }

        }
    }
}