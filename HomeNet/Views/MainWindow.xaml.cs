using HomeNet.Networking;
using HomeNet.Parsing;
using System;
using System.Collections.Generic;
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
        private ScrollViewer _cachedScrollViewer = null;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private BrowserTabState ActiveTab
        {
            get
            {
                if (browserTabControl?.SelectedItem is TabItem selectedItem)
                {
                    return selectedItem.Tag as BrowserTabState;
                }
                return null;
            }
        }

        private FlowDocumentScrollViewer ActiveViewer
        {
            get
            {
                if (browserTabControl?.SelectedItem is TabItem selectedItem)
                {
                    return selectedItem.Content as FlowDocumentScrollViewer;
                }
                return null;
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (browserTabControl.Items.Count == 0)
            {
                CreateNewTabWithHomeMenu();
            }
        }

        private void newTabButton_Click(object sender, RoutedEventArgs e)
        {
            CreateNewTabWithHomeMenu();
        }

        private void TabCloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                DependencyObject curr = button;
                while (curr != null && !(curr is TabItem))
                {
                    curr = VisualTreeHelper.GetParent(curr);
                }

                if (curr is TabItem tabItem)
                {
                    CloseTab(tabItem);
                }
            }
        }

        private void CloseTab(TabItem tabItem)
        {
            int index = browserTabControl.Items.IndexOf(tabItem);
            if (index != -1)
            {
                // 1. Invalidate scroll viewer cache so GC can collect the closed tab
                InvalidateScrollViewerCache();

                // 2. Clear viewer document and tags to sever references
                if (tabItem.Content is FlowDocumentScrollViewer viewer)
                {
                    viewer.Document = null;
                }
                tabItem.Tag = null;
                tabItem.Content = null;

                browserTabControl.Items.Remove(tabItem);

                // 3. Fallback to a new home tab if all tabs are closed
                if (browserTabControl.Items.Count == 0)
                {
                    CreateNewTabWithHomeMenu();
                }
            }
        }

        private void UpdateCurrentTabContent(string htmlContent, string url, BrowserTabState targetTab = null)
        {
            var tab = targetTab ?? ActiveTab;
            if (tab == null) return;

            string pageTitle = ExtractHtmlTitle(htmlContent);
            if (string.IsNullOrWhiteSpace(pageTitle) || pageTitle.Contains("System.Windows"))
            {
                pageTitle = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "Webpage";
            }

            tab.Title = pageTitle;
            tab.CurrentUrl = url;

            if (browserTabControl.SelectedItem is TabItem selectedTabItem && selectedTabItem.Tag == tab)
            {
                selectedTabItem.Header = pageTitle.Length > 20 ? pageTitle.Substring(0, 17) + "..." : pageTitle;
            }
        }

        private string ExtractHtmlTitle(string html)
        {
            try
            {
                int startIndex = html.IndexOf("<title>", StringComparison.OrdinalIgnoreCase);
                if (startIndex != -1)
                {
                    startIndex += 7;
                    int endIndex = html.IndexOf("</title>", startIndex, StringComparison.OrdinalIgnoreCase);
                    if (endIndex != -1)
                    {
                        string title = html.Substring(startIndex, endIndex - startIndex).Trim();
                        if (!string.IsNullOrEmpty(title) && !title.Contains("System.Windows"))
                        {
                            return title;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private TabItem CreateNewTabWithHomeMenu()
        {
            var tabState = new BrowserTabState
            {
                Title = "Home"
            };

            var tabItem = new TabItem
            {
                Header = "Home",
                Tag = tabState
            };

            var viewer = new FlowDocumentScrollViewer
            {
                Document = tabState.Document
            };
            viewer.PreviewMouseWheel += documentViewer_PreviewMouseWheel;

            tabItem.Content = viewer;
            browserTabControl.Items.Add(tabItem);
            browserTabControl.SelectedItem = tabItem;

            LoadHomeMenuIntoTab(tabState);
            return tabItem;
        }

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeWindow_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void LoadHomeMenuIntoTab(BrowserTabState tabState)
        {
            tabState.Document.Blocks.Clear();
            tabState.Document.Background = Brushes.White;
            tabState.Document.Foreground = Brushes.Black;
            tabState.Title = "Home";

            Paragraph welcomeP = new Paragraph();
            welcomeP.FontSize = 20;
            welcomeP.FontWeight = FontWeights.Bold;
            welcomeP.Margin = new Thickness(0, 20, 0, 10);
            welcomeP.Inlines.Add(new Run("Welcome to HomeNet"));
            tabState.Document.Blocks.Add(welcomeP);

            Paragraph descP = new Paragraph();
            descP.FontSize = 14;
            descP.Margin = new Thickness(0, 0, 0, 20);
            descP.Inlines.Add(new Run("Your custom C# and WPF web browser. Quick links:"));
            tabState.Document.Blocks.Add(descP);

            Paragraph linkP = new Paragraph();
            linkP.FontSize = 14;
            linkP.Margin = new Thickness(20, 0, 0, 0);

            Hyperlink searchLink = new Hyperlink(new Run("DuckDuckGo Lite - Recommended Search Engine\n"));
            searchLink.Command = new Commands.NavigationCommand("https://html.duckduckgo.com/lite/", async target => await LoadPageAsync(target));
            linkP.Inlines.Add(searchLink);

            Hyperlink wikiLink = new Hyperlink(new Run("\nWikipedia — The Free Encyclopedia\n"));
            wikiLink.Command = new Commands.NavigationCommand("https://www.wikipedia.org/", async target => await LoadPageAsync(target));
            linkP.Inlines.Add(wikiLink);

            Hyperlink svendLink = new Hyperlink(new Run("\nSvend's portfolio - Most Simple Test Case\n"));
            svendLink.Command = new Commands.NavigationCommand("https://svend233.github.io/", async target => await LoadPageAsync(target));
            linkP.Inlines.Add(svendLink);

            tabState.Document.Blocks.Add(linkP);
            tabState.CurrentUrl = string.Empty;
            searchBar.Text = string.Empty;
        }

        private void browserTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // TAB HIBERNATION & RAM CONSERVATION:
            // Unload document rendering trees for inactive tabs, restore active tab.
            foreach (var item in browserTabControl.Items)
            {
                if (item is TabItem tabItem && tabItem.Tag is BrowserTabState tabState)
                {
                    if (tabItem.Content is FlowDocumentScrollViewer viewer)
                    {
                        if (tabItem == browserTabControl.SelectedItem)
                        {
                            // Wake up active tab: re-attach document if it was hibernated
                            if (viewer.Document == null)
                            {
                                viewer.Document = tabState.Document;
                            }
                        }
                        else
                        {
                            // Hibernate inactive tab: detach document to free visual tree RAM
                            viewer.Document = null;
                        }
                    }
                }
            }

            // Invalidate scroll cache for the newly active viewer
            InvalidateScrollViewerCache();

            if (ActiveTab != null)
            {
                searchBar.Text = ActiveTab.CurrentUrl ?? string.Empty;
                UpdateNavigationButtons();
            }
        }

        private async void searchButton_Click(object sender, RoutedEventArgs e)
        {
            await ProcessOmniboxInputAsync(searchBar.Text);
        }

        private async void searchBar_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await ProcessOmniboxInputAsync(searchBar.Text);
            }
        }

        private async Task ProcessOmniboxInputAsync(string input)
        {
            input = input.Trim();
            if (string.IsNullOrEmpty(input)) return;

            string targetUrl = string.Empty;
            string method = "GET";
            Dictionary<string, string> postData = null;

            if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                targetUrl = input;
            }
            else if (input.Contains(".") && !input.Contains(" ") && !input.EndsWith("."))
            {
                targetUrl = "https://" + input;
            }
            else
            {
                targetUrl = "https://html.duckduckgo.com/lite/";
                method = "POST";
                postData = new Dictionary<string, string>
                {
                    { "q", input }
                };
            }

            await LoadPageAsync(targetUrl, postData, method);
        }

        private async void backButton_Click(object sender, RoutedEventArgs e)
        {
            var tab = ActiveTab;
            if (tab != null && tab.HistoryIndex > 0)
            {
                tab.HistoryIndex--;
                HistoryEntry entry = tab.History[tab.HistoryIndex];
                await LoadPageAsync(entry.Url, entry.PostData, entry.Method, true, tab);
            }
        }

        private async void forwardButton_Click(object sender, RoutedEventArgs e)
        {
            var tab = ActiveTab;
            if (tab != null && tab.HistoryIndex < tab.History.Count - 1)
            {
                tab.HistoryIndex++;
                HistoryEntry entry = tab.History[tab.HistoryIndex];
                await LoadPageAsync(entry.Url, entry.PostData, entry.Method, true, tab);
            }
        }

        private async Task LoadPageAsync(string url, Dictionary<string, string> postData = null, string method = "GET", bool isHistoryNavigation = false, BrowserTabState targetTab = null)
        {
            var tab = targetTab ?? ActiveTab;
            if (tab == null)
            {
                var newTab = CreateNewTabWithHomeMenu();
                tab = newTab.Tag as BrowserTabState;
            }

            try
            {
                InvalidateScrollViewerCache();

                searchButton.IsEnabled = false;
                searchBar.IsEnabled = false;
                loadingProgressBar.Visibility = Visibility.Visible;
                searchBar.Text = url;
                tab.CurrentUrl = url;

                HomeNetEngine engine = new HomeNetEngine(
                    statusMessage => renderFieldStatus(statusMessage),
                    newUrl => {
                        searchBar.Text = newUrl;
                        tab.CurrentUrl = newUrl;
                    }
                );

                string rawResponse = await engine.FetchPageAsync(url, postData, method);

                int headerBodySplit = rawResponse.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                string htmlBody = headerBodySplit >= 0 ? rawResponse.Substring(headerBodySplit + 4) : rawResponse;

                HomeNetParser parser = new HomeNetParser();

                parser.BuildFlowDocumentFromHtml(htmlBody, tab.Document, async (clickedUrl, formData, httpMethod) =>
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

                    await LoadPageAsync(targetUrl, formData, httpMethod, false, tab);
                }, url);

                UpdateCurrentTabContent(htmlBody, url, tab);

                if (!isHistoryNavigation)
                {
                    if (tab.HistoryIndex < tab.History.Count - 1 && tab.HistoryIndex >= 0)
                    {
                        tab.History.RemoveRange(tab.HistoryIndex + 1, tab.History.Count - (tab.HistoryIndex + 1));
                    }

                    tab.History.Add(new HistoryEntry
                    {
                        Url = url,
                        Method = method,
                        PostData = postData
                    });
                    tab.HistoryIndex = tab.History.Count - 1;
                }

                UpdateNavigationButtons();
            }
            catch (Exception ex)
            {
                tab.Document.Blocks.Clear();
                tab.Document.Blocks.Add(new Paragraph(new Run($"Network Error: {ex.Message}")));
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
            var viewer = ActiveViewer;
            if (viewer != null)
            {
                if (_cachedScrollViewer == null)
                {
                    _cachedScrollViewer = GetVisualChild<ScrollViewer>(viewer);
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
            var tab = ActiveTab;
            if (tab != null)
            {
                backButton.IsEnabled = tab.HistoryIndex > 0;
                forwardButton.IsEnabled = tab.HistoryIndex < tab.History.Count - 1;
            }
            else
            {
                backButton.IsEnabled = false;
                forwardButton.IsEnabled = false;
            }
        }

        private void renderFieldStatus(string status)
        {
            // Status bar removed; can be used for logging or left silent.
        }

        private void homeButton_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveTab != null)
            {
                LoadHomeMenuIntoTab(ActiveTab);
                if (browserTabControl.SelectedItem is TabItem selectedTabItem)
                {
                    selectedTabItem.Header = "Home";
                }
            }
        }
    }
}