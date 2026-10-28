using System.Windows;

namespace HomeNet
{
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
                searchButton.IsEnabled = false;

                HomeNetEngine engine = new HomeNetEngine(
                    statusMessage => renderField.Text = statusMessage,
                    newUrl => searchBar.Text = newUrl
                );

                string webData = await engine.FetchPageAsync(searchBar.Text.Trim());
                renderField.Text = webData;
            }
            catch (Exception ex)
            {
                renderField.Text = $"Network Error: {ex.Message}";
            }
            finally
            {
                searchButton.IsEnabled = true;
            }
        }
    }
}