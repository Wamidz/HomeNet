using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace HomeNet.Parsing
{
    public class HomeNetParser
    {
        private readonly CssParser _cssParser = new CssParser();

        public void BuildFlowDocumentFromHtml(string htmlContent, FlowDocument flowDoc, Action<string> onNavigate, string basePageUrl = "")
        {
            flowDoc.Blocks.Clear();

            List<CssRule> storedCssRules = _cssParser.ParseStyleTags(htmlContent);
            FetchExternalStylesheetsSafely(htmlContent, basePageUrl, storedCssRules);

            string cleanedHtml = Regex.Replace(htmlContent, @"<(style|script|noscript)[^>]*>.*?</\1>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            Section rootSection = new Section();
            ApplyBodyStyles(storedCssRules, rootSection, flowDoc);

            string processedHtml = Regex.Replace(cleanedHtml, @"\r?\n\s*", " ");

            // Added <ol> to the main tag matching pattern
            string pattern = @"<(h[1-6]|p|li|button|div|section|header|footer|article|nav|form|ol)(?:\s+([^>]*))?>(.*?)</\1>|<input(?:\s+([^>]*))?/?>|<img[^>]+src\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))[^>]*>";
            MatchCollection matches = Regex.Matches(processedHtml, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            if (matches.Count == 0)
            {
                Paragraph fallbackP = new Paragraph(new Run(CleanText(cleanedHtml)));
                fallbackP.Foreground = flowDoc.Foreground;
                rootSection.Blocks.Add(fallbackP);
            }
            else
            {
                bool renderedAnyBlock = false;

                foreach (Match match in matches)
                {
                    if (match.Groups[1].Success)
                    {
                        string tagName = match.Groups[1].Value.ToLower();
                        string attributes = match.Groups[2].Value;
                        string innerHtml = match.Groups[3].Value;

                        if (innerHtml.Contains("\"function\":") || innerHtml.Contains("{\"wt\":"))
                        {
                            continue;
                        }

                        string className = ExtractAttribute(attributes, "class");
                        string idName = ExtractAttribute(attributes, "id");

                        bool isFlexContainer = tagName == "header" || tagName == "nav" || (!string.IsNullOrEmpty(className) && className.Contains("meny"));

                        if ((tagName == "div" || tagName == "section" || tagName == "article" || tagName == "nav" || tagName == "header" || tagName == "form" || tagName == "ol")
                            && string.IsNullOrEmpty(className) && string.IsNullOrEmpty(idName) && !innerHtml.Contains("<a ") && !innerHtml.Contains("<input") && !isFlexContainer)
                        {
                            continue;
                        }

                        // Handle <header> specifically
                        if (tagName == "header")
                        {
                            Dictionary<string, string> headerStyles = _cssParser.ResolveStylesForElement(storedCssRules, tagName, className, idName);

                            Section headerSection = new Section();
                            if (headerStyles.TryGetValue("background-color", out string headBg))
                            {
                                Brush bgBrush = ConvertCssColor(headBg);
                                if (bgBrush != null) headerSection.Background = bgBrush;
                            }
                            headerSection.Margin = new Thickness(0, 0, 0, 20);
                            headerSection.Padding = new Thickness(16);

                            Match titleMatch = Regex.Match(innerHtml, @"<h[1-6][^>]*>(.*?)</h[1-6]>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                            if (titleMatch.Success)
                            {
                                Paragraph titleP = new Paragraph();
                                titleP.FontSize = 24;
                                titleP.FontWeight = FontWeights.Bold;
                                titleP.TextAlignment = TextAlignment.Center;
                                titleP.Margin = new Thickness(0, 0, 0, 12);
                                AddInlineContent(titleMatch.Groups[1].Value, titleP, onNavigate);
                                headerSection.Blocks.Add(titleP);
                            }

                            if (innerHtml.Contains("<a "))
                            {
                                StackPanel flexPanel = new StackPanel
                                {
                                    Orientation = Orientation.Horizontal,
                                    HorizontalAlignment = HorizontalAlignment.Center,
                                    VerticalAlignment = VerticalAlignment.Center
                                };

                                AddFlexChildContent(innerHtml, flexPanel, onNavigate);

                                if (flexPanel.Children.Count > 0)
                                {
                                    headerSection.Blocks.Add(new BlockUIContainer(flexPanel));
                                }
                            }

                            rootSection.Blocks.Add(headerSection);
                            renderedAnyBlock = true;
                            continue;
                        }

                        // Handle <ol> / <ul> search results lists
                        if (tagName == "ol" || tagName == "ul")
                        {
                            Section listSection = new Section();
                            listSection.Margin = new Thickness(24, 10, 24, 10);

                            string itemPattern = @"<(?:li|article)[^>]*>(.*?)</(?:li|article)>";
                            MatchCollection itemMatches = Regex.Matches(innerHtml, itemPattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

                            foreach (Match itemMatch in itemMatches)
                            {
                                string itemContent = itemMatch.Groups[1].Value;

                                Paragraph itemP = new Paragraph();
                                itemP.Margin = new Thickness(0, 0, 0, 16);
                                itemP.FontSize = 14;

                                Match linkMatch = Regex.Match(itemContent, @"<a\s+([^>]*?)>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                                if (linkMatch.Success)
                                {
                                    string linkAttrs = linkMatch.Groups[1].Value;
                                    string linkText = CleanText(linkMatch.Groups[2].Value);
                                    string href = ExtractAttribute(linkAttrs, "href");

                                    if (!string.IsNullOrEmpty(linkText))
                                    {
                                        Hyperlink hyperlink = new Hyperlink(new Run(linkText))
                                        {
                                            NavigateUri = !string.IsNullOrEmpty(href) ? new Uri(href, UriKind.RelativeOrAbsolute) : null,
                                            FontSize = 16,
                                            FontWeight = FontWeights.Bold
                                        };

                                        hyperlink.RequestNavigate += (sender, args) =>
                                        {
                                            if (args.Uri != null)
                                            {
                                                onNavigate?.Invoke(args.Uri.ToString());
                                                args.Handled = true;
                                            }
                                        };

                                        itemP.Inlines.Add(hyperlink);
                                        itemP.Inlines.Add(new Run("\n"));
                                    }
                                }

                                string plainSnippet = CleanText(itemContent);
                                if (!string.IsNullOrEmpty(plainSnippet))
                                {
                                    itemP.Inlines.Add(new Run(plainSnippet));
                                }

                                if (itemP.Inlines.Count > 0)
                                {
                                    listSection.Blocks.Add(itemP);
                                }
                            }

                            if (listSection.Blocks.Count > 0)
                            {
                                rootSection.Blocks.Add(listSection);
                                renderedAnyBlock = true;
                            }
                            continue;
                        }

                        // Handle <form> / search containers
                        if (tagName == "form" || (tagName == "div" || tagName == "section") && (!string.IsNullOrEmpty(className) || !string.IsNullOrEmpty(idName) || innerHtml.Contains("<input")))
                        {
                            Dictionary<string, string> divStyles = _cssParser.ResolveStylesForElement(storedCssRules, tagName, className, idName);

                            Section containerSec = new Section();
                            if (divStyles.TryGetValue("background-color", out string divBg))
                            {
                                Brush bgBrush = ConvertCssColor(divBg);
                                if (bgBrush != null) containerSec.Background = bgBrush;
                            }
                            containerSec.Margin = new Thickness(20, 20, 20, 20);
                            containerSec.Padding = new Thickness(16);

                            MatchCollection inputMatches = Regex.Matches(innerHtml, @"<input(?:\s+([^>]*))?/?>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                            foreach (Match inMatch in inputMatches)
                            {
                                string inAttrs = inMatch.Groups[1].Value;
                                string inType = ExtractAttribute(inAttrs, "type").ToLower();
                                string val = ExtractAttribute(inAttrs, "value");

                                if (string.IsNullOrEmpty(inType) || inType == "text" || inType == "search")
                                {
                                    TextBox inputTextBox = new TextBox
                                    {
                                        Text = !string.IsNullOrEmpty(val) ? val : string.Empty,
                                        Width = 350,
                                        Height = 36,
                                        VerticalContentAlignment = VerticalAlignment.Center,
                                        Padding = new Thickness(8, 2, 8, 2),
                                        Margin = new Thickness(0, 8, 0, 8)
                                    };

                                    inputTextBox.KeyDown += (s, args) =>
                                    {
                                        if (args.Key == Key.Enter && s is TextBox tb)
                                        {
                                            string query = tb.Text.Trim();
                                            if (!string.IsNullOrEmpty(query))
                                            {
                                                string searchUrl = $"https://duckduckgo.com/?q={Uri.EscapeDataString(query)}";
                                                onNavigate?.Invoke(searchUrl);
                                            }
                                        }
                                    };

                                    containerSec.Blocks.Add(new BlockUIContainer(inputTextBox));
                                }
                            }

                            ParseContainerContent(innerHtml, containerSec, storedCssRules, onNavigate);

                            if (containerSec.Blocks.Count > 0)
                            {
                                rootSection.Blocks.Add(containerSec);
                                renderedAnyBlock = true;
                            }
                            continue;
                        }

                        Paragraph p = new Paragraph();

                        if (tagName.StartsWith("h") && tagName.Length > 1 && char.IsDigit(tagName[1]))
                        {
                            if (int.TryParse(tagName.Substring(1), out int level))
                            {
                                p.FontSize = Math.Max(12, 26 - (level * 3));
                                p.FontWeight = FontWeights.Bold;
                                p.Margin = new Thickness(0, 16, 0, 8);
                            }
                        }
                        else if (tagName == "li")
                        {
                            p.FontSize = 14;
                            p.Margin = new Thickness(24, 0, 0, 4);
                            p.Inlines.Add(new Run("• "));
                        }
                        else
                        {
                            p.FontSize = 14;
                            p.Margin = new Thickness(0, 0, 0, 12);
                        }

                        Dictionary<string, string> styles = _cssParser.ResolveStylesForElement(storedCssRules, tagName, className, idName);
                        ApplyStylesToParagraph(styles, p);

                        if (isFlexContainer && innerHtml.Contains("<a "))
                        {
                            StackPanel flexPanel = new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                HorizontalAlignment = p.TextAlignment == TextAlignment.Center ? HorizontalAlignment.Center :
                                                      p.TextAlignment == TextAlignment.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin = p.Margin,
                                Background = p.Background
                            };

                            AddFlexChildContent(innerHtml, flexPanel, onNavigate);

                            if (flexPanel.Children.Count > 0)
                            {
                                rootSection.Blocks.Add(new BlockUIContainer(flexPanel));
                                renderedAnyBlock = true;
                            }
                        }
                        else
                        {
                            AddInlineContent(innerHtml, p, onNavigate);

                            if (p.Inlines.Count > 0 || p.Background != null)
                            {
                                rootSection.Blocks.Add(p);
                                renderedAnyBlock = true;
                            }
                        }
                    }
                    else if (match.Groups[4].Success)
                    {
                        string attributes = match.Groups[4].Value;
                        string inputType = ExtractAttribute(attributes, "type").ToLower();
                        string inputValue = ExtractAttribute(attributes, "value");

                        if (string.IsNullOrEmpty(inputType) || inputType == "text" || inputType == "search")
                        {
                            TextBox inputTextBox = new TextBox
                            {
                                Text = !string.IsNullOrEmpty(inputValue) ? inputValue : string.Empty,
                                Width = 350,
                                Height = 36,
                                VerticalContentAlignment = VerticalAlignment.Center,
                                Padding = new Thickness(8, 2, 8, 2),
                                Margin = new Thickness(0, 12, 0, 12)
                            };

                            inputTextBox.KeyDown += (s, args) =>
                            {
                                if (args.Key == Key.Enter && s is TextBox tb)
                                {
                                    string query = tb.Text.Trim();
                                    if (!string.IsNullOrEmpty(query))
                                    {
                                        string searchUrl = $"https://duckduckgo.com/?q={Uri.EscapeDataString(query)}";
                                        onNavigate?.Invoke(searchUrl);
                                    }
                                }
                            };

                            rootSection.Blocks.Add(new BlockUIContainer(inputTextBox));
                            renderedAnyBlock = true;
                        }
                    }
                    else
                    {
                        string imgSrc = match.Groups[5].Success ? match.Groups[5].Value :
                                       match.Groups[6].Success ? match.Groups[6].Value :
                                       match.Groups[7].Value;

                        if (!string.IsNullOrEmpty(imgSrc))
                        {
                            if (imgSrc.StartsWith("//")) imgSrc = "https:" + imgSrc;
                            else if (!imgSrc.StartsWith("http://") && !imgSrc.StartsWith("https://") && !string.IsNullOrEmpty(basePageUrl))
                            {
                                if (Uri.TryCreate(new Uri(basePageUrl), imgSrc, out Uri resolvedUri))
                                {
                                    imgSrc = resolvedUri.ToString();
                                }
                            }

                            try
                            {
                                BitmapImage bitmap = new BitmapImage();
                                bitmap.BeginInit();
                                bitmap.UriSource = new Uri(imgSrc, UriKind.Absolute);
                                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                bitmap.EndInit();

                                System.Windows.Controls.Image wpfImage = new System.Windows.Controls.Image
                                {
                                    Source = bitmap,
                                    MaxWidth = 400,
                                    MaxHeight = 300,
                                    Stretch = Stretch.Uniform,
                                    HorizontalAlignment = HorizontalAlignment.Left,
                                    Margin = new Thickness(0, 8, 0, 8)
                                };

                                rootSection.Blocks.Add(new BlockUIContainer(wpfImage));
                                renderedAnyBlock = true;
                            }
                            catch { }
                        }
                    }
                }

                if (!renderedAnyBlock)
                {
                    Paragraph fallbackP = new Paragraph(new Run(CleanText(cleanedHtml)));
                    fallbackP.Foreground = flowDoc.Foreground;
                    rootSection.Blocks.Add(fallbackP);
                }
            }

            flowDoc.Blocks.Add(rootSection);
        }

        private void ParseContainerContent(string htmlSnippet, Section containerSection, List<CssRule> rules, Action<string> onNavigate)
        {
            string pattern = @"<(h[1-6]|p)(?:\s+([^>]*))?>(.*?)</\1>";
            MatchCollection matches = Regex.Matches(htmlSnippet, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                string tag = match.Groups[1].Value.ToLower();
                string attrs = match.Groups[2].Value;
                string innerText = match.Groups[3].Value;

                Paragraph innerParagraph = new Paragraph();
                innerParagraph.TextAlignment = TextAlignment.Center;
                innerParagraph.Margin = new Thickness(0, 6, 0, 8);

                string cls = ExtractAttribute(attrs, "class");
                string id = ExtractAttribute(attrs, "id");
                Dictionary<string, string> elementStyles = _cssParser.ResolveStylesForElement(rules, tag, cls, id);

                if (tag.StartsWith("h"))
                {
                    innerParagraph.FontSize = 24;
                    innerParagraph.FontWeight = FontWeights.Bold;
                }
                else
                {
                    innerParagraph.FontSize = 16;
                }

                ApplyStylesToParagraph(elementStyles, innerParagraph);
                AddInlineContent(innerText, innerParagraph, onNavigate);

                if (innerParagraph.Inlines.Count > 0)
                {
                    containerSection.Blocks.Add(innerParagraph);
                }
            }
        }

        private void AddFlexChildContent(string innerHtml, StackPanel flexPanel, Action<string> onNavigate)
        {
            string linkPattern = @"<a\s+([^>]*?)>(.*?)</a>";
            MatchCollection linkMatches = Regex.Matches(innerHtml, linkPattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            foreach (Match linkMatch in linkMatches)
            {
                string linkAttrs = linkMatch.Groups[1].Value;
                string linkText = CleanText(linkMatch.Groups[2].Value);
                string href = ExtractAttribute(linkAttrs, "href");

                Button btn = new Button
                {
                    Content = linkText,
                    Margin = new Thickness(4, 0, 4, 0),
                    Padding = new Thickness(8, 4, 8, 4)
                };

                if (!string.IsNullOrEmpty(href))
                {
                    btn.Tag = href;
                    btn.Click += (s, e) =>
                    {
                        if (s is Button b && b.Tag is string targetUrl)
                        {
                            onNavigate?.Invoke(targetUrl);
                        }
                    };
                }

                flexPanel.Children.Add(btn);
            }
        }

        private void AddInlineContent(string htmlSnippet, Paragraph p, Action<string> onNavigate)
        {
            string linkPattern = @"<a\s+([^>]*?)>(.*?)</a>";
            int lastIndex = 0;

            foreach (Match match in Regex.Matches(htmlSnippet, linkPattern, RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                if (match.Index > lastIndex)
                {
                    string plainText = CleanText(htmlSnippet.Substring(lastIndex, match.Index - lastIndex));
                    if (!string.IsNullOrEmpty(plainText))
                    {
                        p.Inlines.Add(new Run(plainText));
                    }
                }

                string linkAttrs = match.Groups[1].Value;
                string linkText = CleanText(match.Groups[2].Value);
                string href = ExtractAttribute(linkAttrs, "href");

                Hyperlink hyperlink = new Hyperlink(new Run(linkText))
                {
                    NavigateUri = !string.IsNullOrEmpty(href) ? new Uri(href, UriKind.RelativeOrAbsolute) : null
                };

                hyperlink.RequestNavigate += (sender, args) =>
                {
                    if (args.Uri != null)
                    {
                        onNavigate?.Invoke(args.Uri.ToString());
                        args.Handled = true;
                    }
                };

                p.Inlines.Add(hyperlink);
                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < htmlSnippet.Length)
            {
                string remainingText = CleanText(htmlSnippet.Substring(lastIndex));
                if (!string.IsNullOrEmpty(remainingText))
                {
                    p.Inlines.Add(new Run(remainingText));
                }
            }
        }

        private void ApplyBodyStyles(List<CssRule> rules, Section section, FlowDocument flowDoc)
        {
            Dictionary<string, string> bodyStyles = _cssParser.ResolveStylesForElement(rules, "body", "", "");

            flowDoc.Background = Brushes.White;
            section.Background = Brushes.White;
            flowDoc.Foreground = Brushes.Black;

            if (bodyStyles.TryGetValue("background-color", out string bg))
            {
                Brush brush = ConvertCssColor(bg);
                if (brush != null)
                {
                    flowDoc.Background = brush;
                    section.Background = brush;
                }
            }

            if (bodyStyles.TryGetValue("color", out string color))
            {
                Brush brush = ConvertCssColor(color);
                if (brush != null) flowDoc.Foreground = brush;
            }
        }

        private void ApplyStylesToParagraph(Dictionary<string, string> styles, Paragraph p)
        {
            if (styles.TryGetValue("color", out string color))
            {
                Brush brush = ConvertCssColor(color);
                if (brush != null) p.Foreground = brush;
            }

            if (styles.TryGetValue("background-color", out string bg))
            {
                Brush brush = ConvertCssColor(bg);
                if (brush != null) p.Background = brush;
            }

            if (styles.TryGetValue("text-align", out string align))
            {
                if (align.Equals("center", StringComparison.OrdinalIgnoreCase)) p.TextAlignment = TextAlignment.Center;
                else if (align.Equals("right", StringComparison.OrdinalIgnoreCase)) p.TextAlignment = TextAlignment.Right;
                else if (align.Equals("left", StringComparison.OrdinalIgnoreCase)) p.TextAlignment = TextAlignment.Left;
            }
        }

        private Brush ConvertCssColor(string cssColor)
        {
            cssColor = cssColor.Trim().ToLower();

            if (cssColor.StartsWith("#"))
            {
                try
                {
                    return (Brush)new BrushConverter().ConvertFromString(cssColor);
                }
                catch { return null; }
            }

            if (cssColor.StartsWith("rgb"))
            {
                try
                {
                    Match match = Regex.Match(cssColor, @"rgba?\s*\(\s*(\d+),\s*(\d+),\s*(\d+)(?:,\s*[\d\.]+)?\s*\)");
                    if (match.Success)
                    {
                        byte r = byte.Parse(match.Groups[1].Value);
                        byte g = byte.Parse(match.Groups[2].Value);
                        byte b = byte.Parse(match.Groups[3].Value);
                        return new SolidColorBrush(Color.FromArgb(255, r, g, b));
                    }
                }
                catch { }
            }

            try
            {
                return (Brush)new BrushConverter().ConvertFromString(cssColor);
            }
            catch
            {
                return null;
            }
        }

        private string ExtractAttribute(string attributes, string attributeName)
        {
            if (string.IsNullOrEmpty(attributes)) return string.Empty;
            MatchCollection matches = Regex.Matches(attributes, $@"{attributeName}\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase);
            if (matches.Count > 0)
            {
                Match lastMatch = matches[matches.Count - 1];
                return lastMatch.Groups[1].Success ? lastMatch.Groups[1].Value :
                       lastMatch.Groups[2].Success ? lastMatch.Groups[2].Value :
                       lastMatch.Groups[3].Value;
            }
            return string.Empty;
        }

        private string CleanText(string html)
        {
            string text = Regex.Replace(html, @"<[^>]+>", string.Empty);
            return System.Net.WebUtility.HtmlDecode(text).Trim();
        }

        private void FetchExternalStylesheetsSafely(string htmlContent, string basePageUrl, List<CssRule> rules)
        {
            MatchCollection linkMatches = Regex.Matches(htmlContent, @"<link[^>]+rel\s*=\s*['""]stylesheet['""][^>]*>", RegexOptions.IgnoreCase);
            foreach (Match match in linkMatches)
            {
                string linkTag = match.Value;
                string href = ExtractAttribute(linkTag, "href");
                if (!string.IsNullOrEmpty(href) && !string.IsNullOrEmpty(basePageUrl))
                {
                    try
                    {
                        if (Uri.TryCreate(new Uri(basePageUrl), href, out Uri resolvedUri))
                        {
                            using (HttpClient client = new HttpClient())
                            {
                                client.Timeout = TimeSpan.FromSeconds(3);
                                string cssText = client.GetStringAsync(resolvedUri).Result;
                                rules.AddRange(_cssParser.ParseCssString(cssText));
                            }
                        }
                    }
                    catch { }
                }
            }
        }
    }
}