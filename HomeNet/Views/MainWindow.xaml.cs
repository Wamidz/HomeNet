using HomeNet.Networking;
using HomeNet.Parsing;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace HomeNet.Views
{
    public partial class MainWindow : Window
    {
        private readonly List<string> _history = new List<string>();
        private int _historyIndex = -1;
        private bool _isNavigatingHistory = false;
        private ScrollViewer _cachedScrollViewer = null;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadHomeMenu();
        }

        private void LoadHomeMenu()
        {
            renderFlowDoc.Blocks.Clear();

            // Explicitly reset to clean light theme defaults for the home menu
            renderFlowDoc.Background = Brushes.White;
            renderFlowDoc.Foreground = Brushes.Black;

            Paragraph welcomeP = new Paragraph();
            welcomeP.FontSize = 20;
            welcomeP.FontWeight = FontWeights.Bold;
            welcomeP.Margin = new Thickness(0, 20, 0, 10);
            welcomeP.Inlines.Add(new Run("Welcome to HomeNet"));
            renderFlowDoc.Blocks.Add(welcomeP);

            Paragraph descP = new Paragraph();
            descP.FontSize = 14;
            descP.Margin = new Thickness(0, 0, 0, 20);
            descP.Inlines.Add(new Run("Your custom C# and WPF web browser. Quick links:"));
            renderFlowDoc.Blocks.Add(descP);

            Paragraph linkP = new Paragraph();
            linkP.FontSize = 14;
            linkP.Margin = new Thickness(20, 0, 0, 0);

            Hyperlink searchLink = new Hyperlink(new Run("DuckDuckGo - Recommended Search Engine\n"));
            searchLink.Command = new Commands.NavigationCommand("https://duckduckgo.com/?q=", async target => await LoadPageAsync(target));
            linkP.Inlines.Add(searchLink);

            Hyperlink wikiLink = new Hyperlink(new Run("\nWikipedia — The Free Encyclopedia\n"));
            wikiLink.Command = new Commands.NavigationCommand("https://www.wikipedia.org/", async target => await LoadPageAsync(target));
            linkP.Inlines.Add(wikiLink);

            Hyperlink svendLink = new Hyperlink(new Run("\nSvend's portfolio - Most Simple Test Case\n"));
            svendLink.Command = new Commands.NavigationCommand("https://svend233.github.io/", async target => await LoadPageAsync(target));
            linkP.Inlines.Add(svendLink);

            renderFlowDoc.Blocks.Add(linkP);
            searchBar.Text = string.Empty;
            Title = "MainWindow";
        }

        private async void searchButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadPageAsync(searchBar.Text.Trim());
        }

        private async void searchBar_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await LoadPageAsync(searchBar.Text.Trim());
            }
        }

        private async void backButton_Click(object sender, RoutedEventArgs e)
        {
            if (_historyIndex > 0)
            {
                _historyIndex--;
                _isNavigatingHistory = true;
                await LoadPageAsync(_history[_historyIndex], true);
                _isNavigatingHistory = false;
                UpdateNavigationButtons();
            }
        }

        private async void forwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (_historyIndex < _history.Count - 1)
            {
                _historyIndex++;
                _isNavigatingHistory = true;
                await LoadPageAsync(_history[_historyIndex], true);
                _isNavigatingHistory = false;
                UpdateNavigationButtons();
            }
        }

        private async Task LoadPageAsync(string url, bool isHistoryNavigation = false)
        {
            try
            {
                InvalidateScrollViewerCache();

                searchButton.IsEnabled = false;
                searchBar.IsEnabled = false;
                loadingProgressBar.Visibility = Visibility.Visible;
                searchBar.Text = url;

                HomeNetEngine engine = new HomeNetEngine(
                    statusMessage => renderFieldStatus(statusMessage),
                    newUrl => searchBar.Text = newUrl
                );

                string rawResponse = await engine.FetchPageAsync(url);
                HomeNetParser parser = new HomeNetParser();

                parser.BuildFlowDocumentFromHtml(rawResponse, renderFlowDoc, async clickedUrl =>
                {
                    if (string.IsNullOrEmpty(clickedUrl) ||
                        clickedUrl.StartsWith("#") ||
                        clickedUrl.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    if (clickedUrl.StartsWith("//"))
                    {
                        clickedUrl = "https:" + clickedUrl;
                    }

                    string targetUrl = clickedUrl;
                    if (!Uri.TryCreate(clickedUrl, UriKind.Absolute, out Uri absoluteUri))
                    {
                        Uri baseUri = new Uri(url);
                        if (Uri.TryCreate(baseUri, clickedUrl, out Uri combinedUri))
                        {
                            targetUrl = combinedUri.ToString();
                        }
                        else
                        {
                            return;
                        }
                    }

                    await LoadPageAsync(targetUrl);
                }, url);

                if (!isHistoryNavigation)
                {
                    if (_historyIndex < _history.Count - 1)
                    {
                        _history.RemoveRange(_historyIndex + 1, _history.Count - (_historyIndex + 1));
                    }

                    _history.Add(url);
                    _historyIndex = _history.Count - 1;
                }

                UpdateNavigationButtons();
                Title = "HomeNet - Done!";
            }
            catch (Exception ex)
            {
                renderFlowDoc.Blocks.Clear();
                renderFlowDoc.Blocks.Add(new Paragraph(new Run($"Network Error: {ex.Message}")));
                Title = "HomeNet - Error";
            }
            finally
            {
                searchButton.IsEnabled = true;
                searchBar.IsEnabled = true;
                loadingProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void documentViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_cachedScrollViewer == null)
            {
                _cachedScrollViewer = GetVisualChild<ScrollViewer>(documentViewer);
            }

            if (_cachedScrollViewer != null)
            {
                double scrollSpeedMultiplier = 1.5;
                double newOffset = _cachedScrollViewer.VerticalOffset - (e.Delta * scrollSpeedMultiplier);

                if (newOffset < 0) newOffset = 0;
                if (newOffset > _cachedScrollViewer.ScrollableHeight) newOffset = _cachedScrollViewer.ScrollableHeight;

                _cachedScrollViewer.ScrollToVerticalOffset(newOffset);
                e.Handled = true;
            }
        }

        private void InvalidateScrollViewerCache()
        {
            _cachedScrollViewer = null;
        }

        private static T GetVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }
                var result = GetVisualChild<T>(child);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        private void UpdateNavigationButtons()
        {
            backButton.IsEnabled = _historyIndex > 0;
            forwardButton.IsEnabled = _historyIndex < _history.Count - 1;
        }

        private void renderFieldStatus(string status)
        {
            Title = $"HomeNet - {status}";
        }

        private void homeButton_Click(object sender, RoutedEventArgs e)
        {
            LoadHomeMenu();
        }
    }
}