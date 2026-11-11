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
using System.Windows.Shapes;

namespace HomeNet.Parsing
{
    public class HomeNetParser
    {
        private readonly CssParser _cssParser = new CssParser();

        private string GetUserAgent()
        {
            string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(2) ?? "0.1";
            return $"HomeNet/{version}";
        }

        public void BuildFlowDocumentFromHtml(string htmlContent, FlowDocument flowDoc, Action<string, Dictionary<string, string>, string> onNavigate, string basePageUrl = "")
        {
            flowDoc.Blocks.Clear();

            List<CssRule> storedCssRules = _cssParser.ParseStyleTags(htmlContent);
            FetchExternalStylesheetsSafely(htmlContent, basePageUrl, storedCssRules);

            string cleanedHtml = Regex.Replace(htmlContent, @"<(style|script|noscript)[^>]*>.*?</\1>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            cleanedHtml = Regex.Replace(cleanedHtml, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);

            Section rootSection = new Section();
            ApplyBodyStyles(storedCssRules, rootSection, flowDoc);

            // If this is a DuckDuckGo search results page, parse the specialized components in order
            if (cleanedHtml.Contains("result-link") || cleanedHtml.Contains("next_form"))
            {
                var orderedElements = new List<(int Index, Action RenderAction)>();

                // 1. Capture Forms (Search bar and pagination forms)
                foreach (Match formMatch in Regex.Matches(cleanedHtml, @"<form\s+([^>]*?)>(.*?)</form>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
                {
                    string formAttrs = formMatch.Groups[1].Value;
                    string formInner = formMatch.Groups[2].Value;
                    int matchIndex = formMatch.Index;

                    string formAction = ExtractAttribute(formAttrs, "action");
                    string formMethod = ExtractAttribute(formAttrs, "method").ToLower();
                    string targetFormUrl = basePageUrl;

                    if (!string.IsNullOrEmpty(formAction))
                    {
                        if (Uri.TryCreate(new Uri(basePageUrl), formAction, out Uri resolvedFormUri))
                        {
                            targetFormUrl = resolvedFormUri.ToString();
                        }
                    }

                    if (formAttrs.Contains("next_form") || formInner.Contains("navbutton"))
                    {
                        Match submitValMatch = Regex.Match(formInner, @"<input[^>]+value\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))[^>]*>", RegexOptions.IgnoreCase);
                        string btnText = submitValMatch.Groups[1].Success ? submitValMatch.Groups[1].Value : "Next Page >";

                        List<(string Name, string Type, TextBox Box, CheckBox CheckBoxElement)> pageElements = new List<(string, string, TextBox, CheckBox)>();
                        MatchCollection hiddenInputs = Regex.Matches(formInner, @"<input[^>]+type\s*=\s*['""]hidden['""][^>]*>", RegexOptions.IgnoreCase);

                        foreach (Match hid in hiddenInputs)
                        {
                            string hAttrs = hid.Value;
                            string hName = ExtractAttribute(hAttrs, "name");
                            string hVal = ExtractAttribute(hAttrs, "value");
                            if (!string.IsNullOrEmpty(hName))
                            {
                                TextBox hiddenBox = new TextBox { Text = hVal };
                                pageElements.Add((hName, "text", hiddenBox, null));
                            }
                        }

                        orderedElements.Add((matchIndex, () =>
                        {
                            Button nextBtn = new Button
                            {
                                Content = System.Net.WebUtility.HtmlDecode(btnText),
                                Width = 140,
                                Height = 32,
                                Margin = new Thickness(0, 10, 0, 10)
                            };

                            nextBtn.Click += (s, args) =>
                            {
                                SubmitForm(formAction, string.IsNullOrEmpty(formMethod) ? "post" : formMethod, targetFormUrl, pageElements, onNavigate);
                            };

                            rootSection.Blocks.Add(new BlockUIContainer(nextBtn));
                        }
                        ));
                    }
                    else if (formInner.Contains("type=\"text\"") || formInner.Contains("type='text'") || formInner.Contains("name=\"q\""))
                    {
                        orderedElements.Add((matchIndex, () =>
                        {
                            Section formSec = new Section();
                            formSec.Margin = new Thickness(10, 8, 10, 8);

                            List<(string Name, string Type, TextBox Box, CheckBox CheckBoxElement)> formElements = new List<(string, string, TextBox, CheckBox)>();
                            MatchCollection inputMatches = Regex.Matches(formInner, @"<input(?:\s+([^>]*))?/?>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

                            foreach (Match inMatch in inputMatches)
                            {
                                string inAttrs = inMatch.Groups[1].Value;
                                string inType = ExtractAttribute(inAttrs, "type").ToLower();
                                string val = ExtractAttribute(inAttrs, "value");
                                string inputName = ExtractAttribute(inAttrs, "name");

                                if (string.IsNullOrEmpty(inType) || inType == "text" || inType == "search")
                                {
                                    TextBox inputTextBox = new TextBox
                                    {
                                        Text = !string.IsNullOrEmpty(val) ? val : string.Empty,
                                        Width = 350,
                                        Height = 36,
                                        VerticalContentAlignment = VerticalAlignment.Center,
                                        Padding = new Thickness(8, 2, 8, 2),
                                        Margin = new Thickness(0, 4, 0, 4)
                                    };

                                    if (!string.IsNullOrEmpty(inputName))
                                    {
                                        formElements.Add((inputName, "text", inputTextBox, null));
                                    }

                                    inputTextBox.KeyDown += (s, args) =>
                                    {
                                        if (args.Key == Key.Enter)
                                        {
                                            SubmitForm(formAction, string.IsNullOrEmpty(formMethod) ? "get" : formMethod, targetFormUrl, formElements, onNavigate);
                                        }
                                    };

                                    formSec.Blocks.Add(new BlockUIContainer(inputTextBox));
                                }
                                else if (inType == "submit")
                                {
                                    Button submitBtn = new Button
                                    {
                                        Content = !string.IsNullOrEmpty(val) ? val : "Submit",
                                        Width = 100,
                                        Height = 30,
                                        Margin = new Thickness(0, 4, 0, 4)
                                    };

                                    submitBtn.Click += (s, args) =>
                                    {
                                        SubmitForm(formAction, string.IsNullOrEmpty(formMethod) ? "get" : formMethod, targetFormUrl, formElements, onNavigate);
                                    };

                                    formSec.Blocks.Add(new BlockUIContainer(submitBtn));
                                }
                            }

                            rootSection.Blocks.Add(formSec);
                        }
                        ));
                    }
                }

                MatchCollection zeroClickMatch = Regex.Matches(cleanedHtml, @"Zero-click info:.*?<a[^>]+href\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))[^>]*>(.*?)</a>.*?</td>\s*</tr>\s*<tr>\s*<td>(.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                foreach (Match zc in zeroClickMatch)
                {
                    int zcIndex = zc.Index;
                    string wikiHref = zc.Groups[1].Success ? zc.Groups[1].Value : zc.Groups[2].Value;
                    string wikiTitle = CleanText(zc.Groups[4].Value);
                    string wikiBody = CleanText(zc.Groups[5].Value);

                    orderedElements.Add((zcIndex, () =>
                    {
                        Section zcSec = new Section
                        {
                            Background = new SolidColorBrush(Color.FromRgb(35, 35, 40)),
                            Padding = new Thickness(14),
                            Margin = new Thickness(0, 10, 0, 16)
                        };

                        Paragraph zcP = new Paragraph();
                        zcP.Inlines.Add(new Run("Zero-click info: ") { FontWeight = FontWeights.Bold, Foreground = Brushes.Orange });

                        Hyperlink zcLink = new Hyperlink(new Run(wikiTitle))
                        {
                            NavigateUri = !string.IsNullOrEmpty(wikiHref) ? new Uri(wikiHref, UriKind.RelativeOrAbsolute) : null,
                            FontWeight = FontWeights.Bold
                        };
                        zcLink.RequestNavigate += (sender, args) => { if (args.Uri != null) { onNavigate?.Invoke(args.Uri.ToString(), null, "GET"); args.Handled = true; } };
                        zcP.Inlines.Add(zcLink);
                        zcSec.Blocks.Add(zcP);

                        if (!string.IsNullOrEmpty(wikiBody))
                        {
                            Paragraph bodyP = new Paragraph(new Run(wikiBody))
                            {
                                FontSize = 13,
                                Foreground = Brushes.WhiteSmoke,
                                Margin = new Thickness(0, 6, 0, 0)
                            };
                            zcSec.Blocks.Add(bodyP);
                        }

                        rootSection.Blocks.Add(zcSec);
                    }
                    ));
                }

                string resultRowPattern = @"<td[^>]*valign\s*=\s*[""']top[""'][^>]*>(.*?)</td>\s*<td>\s*<a[^>]+href\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))[^>]*class\s*=\s*[""']result-link[""'][^>]*>(.*?)</a>\s*</td>(?:.*?<td[^>]*class\s*=\s*[""']result-snippet[""'][^>]*>(.*?)</td>)?(?:.*?<span[^>]*class\s*=\s*[""']link-text[""'][^>]*>(.*?)</span>)?";
                MatchCollection resultMatches = Regex.Matches(cleanedHtml, resultRowPattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

                if (resultMatches.Count > 0)
                {
                    foreach (Match resMatch in resultMatches)
                    {
                        int resIndex = resMatch.Index;
                        string numberText = CleanText(resMatch.Groups[1].Value).Replace(".", "");
                        string href = resMatch.Groups[2].Success ? resMatch.Groups[2].Value :
                                       resMatch.Groups[3].Success ? resMatch.Groups[3].Value :
                                       resMatch.Groups[4].Value;
                        string titleText = CleanText(resMatch.Groups[5].Value);
                        string snippetText = resMatch.Groups[6].Success ? CleanText(resMatch.Groups[6].Value) : string.Empty;
                        string linkTextDomain = resMatch.Groups[7].Success ? CleanText(resMatch.Groups[7].Value) : string.Empty;

                        orderedElements.Add((resIndex, () =>
                        {
                            if (!string.IsNullOrEmpty(titleText))
                            {
                                Paragraph titleP = new Paragraph();
                                titleP.Margin = new Thickness(0, 14, 0, 2);
                                titleP.FontSize = 15;

                                string displayNum = !string.IsNullOrEmpty(numberText) ? $"{numberText}. " : "";

                                Hyperlink hyperlink = new Hyperlink(new Run($"{displayNum}{titleText}"))
                                {
                                    NavigateUri = !string.IsNullOrEmpty(href) ? new Uri(href, UriKind.RelativeOrAbsolute) : null,
                                    FontWeight = FontWeights.Bold
                                };

                                hyperlink.RequestNavigate += (sender, args) =>
                                {
                                    if (args.Uri != null)
                                    {
                                        onNavigate?.Invoke(args.Uri.ToString(), null, "GET");
                                        args.Handled = true;
                                    }
                                };

                                titleP.Inlines.Add(hyperlink);
                                rootSection.Blocks.Add(titleP);
                            }

                            if (!string.IsNullOrEmpty(snippetText))
                            {
                                Paragraph snippetP = new Paragraph(new Run(snippetText))
                                {
                                    FontSize = 13,
                                    Margin = new Thickness(20, 0, 0, 2),
                                    Foreground = Brushes.LightGray
                                };
                                rootSection.Blocks.Add(snippetP);
                            }

                            if (!string.IsNullOrEmpty(linkTextDomain))
                            {
                                Paragraph domainP = new Paragraph(new Run(linkTextDomain))
                                {
                                    FontSize = 12,
                                    Margin = new Thickness(20, 0, 0, 10),
                                    Foreground = Brushes.Gray
                                };
                                rootSection.Blocks.Add(domainP);
                            }
                        }
                        ));
                    }
                }

                orderedElements.Sort((a, b) => a.Index.CompareTo(b.Index));
                foreach (var element in orderedElements)
                {
                    element.RenderAction();
                }

                flowDoc.Blocks.Add(rootSection);
                return;
            }

            // Standard general HTML parsing for other websites (Wikipedia, portfolios, etc.)
            string processedHtml = Regex.Replace(cleanedHtml, @"\r?\n\s*", " ");

            string pattern = @"<(h[1-6]|p|li|button|div|section|header|footer|article|nav|form|ol)(?:\s+([^>]*))?>(.*?)</\1>|<img[^>]+src\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))[^>]*>";
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
                bool hasRenderedSearchForm = false;

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
                                                onNavigate?.Invoke(args.Uri.ToString(), null, "GET");
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

                        if (tagName == "form")
                        {
                            if (hasRenderedSearchForm)
                            {
                                continue;
                            }
                            hasRenderedSearchForm = true;

                            Dictionary<string, string> divStyles = _cssParser.ResolveStylesForElement(storedCssRules, tagName, className, idName);

                            string formAction = ExtractAttribute(attributes, "action");
                            string formMethod = ExtractAttribute(attributes, "method").ToLower();
                            string targetFormUrl = basePageUrl;

                            if (!string.IsNullOrEmpty(formAction))
                            {
                                if (Uri.TryCreate(new Uri(basePageUrl), formAction, out Uri resolvedFormUri))
                                {
                                    targetFormUrl = resolvedFormUri.ToString();
                                }
                            }

                            Section containerSec = new Section();
                            if (divStyles.TryGetValue("background-color", out string divBg))
                            {
                                Brush bgBrush = ConvertCssColor(divBg);
                                if (bgBrush != null) containerSec.Background = bgBrush;
                            }
                            containerSec.Margin = new Thickness(20, 20, 20, 20);
                            containerSec.Padding = new Thickness(16);

                            List<(string Name, string Type, TextBox Box, CheckBox CheckBoxElement)> formElements = new List<(string, string, TextBox, CheckBox)>();

                            MatchCollection inputMatches = Regex.Matches(innerHtml, @"<input(?:\s+([^>]*))?/?>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                            foreach (Match inMatch in inputMatches)
                            {
                                string inAttrs = inMatch.Groups[1].Value;
                                string inType = ExtractAttribute(inAttrs, "type").ToLower();
                                string val = ExtractAttribute(inAttrs, "value");
                                string inputName = ExtractAttribute(inAttrs, "name");

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

                                    if (!string.IsNullOrEmpty(inputName))
                                    {
                                        formElements.Add((inputName, "text", inputTextBox, null));
                                    }

                                    inputTextBox.KeyDown += (s, args) =>
                                    {
                                        if (args.Key == Key.Enter)
                                        {
                                            SubmitForm(formAction, formMethod, targetFormUrl, formElements, onNavigate);
                                        }
                                    };

                                    containerSec.Blocks.Add(new BlockUIContainer(inputTextBox));
                                }
                                else if (inType == "checkbox")
                                {
                                    CheckBox checkBox = new CheckBox
                                    {
                                        Content = !string.IsNullOrEmpty(inputName) ? inputName : "Option",
                                        Margin = new Thickness(0, 6, 0, 6),
                                        FontSize = 14
                                    };

                                    if (inAttrs.Contains("checked"))
                                    {
                                        checkBox.IsChecked = true;
                                    }

                                    if (!string.IsNullOrEmpty(inputName))
                                    {
                                        formElements.Add((inputName, "checkbox", null, checkBox));
                                    }

                                    containerSec.Blocks.Add(new BlockUIContainer(checkBox));
                                }
                                else if (inType == "submit")
                                {
                                    Button submitBtn = new Button
                                    {
                                        Content = !string.IsNullOrEmpty(val) ? val : "Submit",
                                        Width = 100,
                                        Height = 30,
                                        Margin = new Thickness(0, 8, 0, 8)
                                    };

                                    submitBtn.Click += (s, args) =>
                                    {
                                        SubmitForm(formAction, formMethod, targetFormUrl, formElements, onNavigate);
                                    };

                                    containerSec.Blocks.Add(new BlockUIContainer(submitBtn));
                                }
                            }

                            ParseContainerContent(innerHtml, containerSec, storedCssRules, onNavigate, basePageUrl);

                            if (containerSec.Blocks.Count > 0)
                            {
                                rootSection.Blocks.Add(containerSec);
                                renderedAnyBlock = true;
                            }
                            continue;
                        }

                        if (tagName == "div" || tagName == "section" || tagName == "article" || tagName == "nav")
                        {
                            Section containerSec = new Section();
                            Dictionary<string, string> containerStyles = _cssParser.ResolveStylesForElement(storedCssRules, tagName, className, idName);
                            if (containerStyles.TryGetValue("background-color", out string cBg))
                            {
                                Brush bgBrush = ConvertCssColor(cBg);
                                if (bgBrush != null) containerSec.Background = bgBrush;
                            }

                            ParseContainerContent(innerHtml, containerSec, storedCssRules, onNavigate, basePageUrl);

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
                    else
                    {
                        // Standalone <img> tag match branch
                        string imgSrc = match.Groups[4].Success ? match.Groups[4].Value :
                                       match.Groups[5].Success ? match.Groups[5].Value :
                                       match.Groups[6].Value;

                        if (!string.IsNullOrEmpty(imgSrc))
                        {
                            imgSrc = ResolveImageUrl(imgSrc, basePageUrl);
                            string finalImgSrc = imgSrc;

                            try
                            {
                                using (HttpClient client = new HttpClient())
                                {
                                    client.Timeout = TimeSpan.FromSeconds(5);
                                    client.DefaultRequestHeaders.UserAgent.ParseAdd(GetUserAgent());

                                    byte[] imageBytes = client.GetByteArrayAsync(finalImgSrc).Result;

                                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        try
                                        {
                                            BitmapImage bitmap = new BitmapImage();
                                            using (MemoryStream ms = new MemoryStream(imageBytes))
                                            {
                                                bitmap.BeginInit();
                                                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                                bitmap.StreamSource = ms;
                                                bitmap.EndInit();
                                                bitmap.Freeze();
                                            }

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
                                        catch (Exception exInner)
                                        {
                                            Console.WriteLine($"[Bitmap Creation Error] '{finalImgSrc}': {exInner.Message}");
                                        }
                                    });
                                }
                            }
                            catch (Exception exNet)
                            {
                                Console.WriteLine($"[Image Download Error] '{finalImgSrc}': {exNet.Message}");
                            }
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

        private string ResolveImageUrl(string imgSrc, string basePageUrl)
        {
            if (string.IsNullOrWhiteSpace(imgSrc)) return string.Empty;
            imgSrc = imgSrc.Trim();

            // 1. Handle protocol-relative URLs (e.g., "//example.com/image.png")
            if (imgSrc.StartsWith("//"))
            {
                string scheme = "https"; // Default fallback
                if (Uri.TryCreate(basePageUrl, UriKind.Absolute, out Uri baseUriObj))
                {
                    scheme = baseUriObj.Scheme;
                }
                imgSrc = scheme + ":" + imgSrc;
            }

            // 2. If the image URL is already an absolute URI, validate and return it
            if (Uri.TryCreate(imgSrc, UriKind.Absolute, out Uri absoluteUri))
            {
                return absoluteUri.AbsoluteUri;
            }

            // 3. Resolve relative URLs against the base page URL
            if (!string.IsNullOrEmpty(basePageUrl) && Uri.TryCreate(basePageUrl, UriKind.Absolute, out Uri baseUri))
            {
                // Separate path from query string or fragment so we don't mangle them
                string pathPart = imgSrc;
                string queryAndFragment = string.Empty;

                int queryIndex = imgSrc.IndexOf('?');
                if (queryIndex >= 0)
                {
                    pathPart = imgSrc.Substring(0, queryIndex);
                    queryAndFragment = imgSrc.Substring(queryIndex);
                }
                else
                {
                    int fragIndex = imgSrc.IndexOf('#');
                    if (fragIndex >= 0)
                    {
                        pathPart = imgSrc.Substring(0, fragIndex);
                        queryAndFragment = imgSrc.Substring(fragIndex);
                    }
                }

                // Escape each path segment individually to handle spaces, parentheses, 
                // and special characters safely while keeping directory separators (/) intact.
                var pathSegments = pathPart.Split('/');
                for (int i = 0; i < pathSegments.Length; i++)
                {
                    pathSegments[i] = Uri.EscapeDataString(pathSegments[i]);
                }

                string cleanedPath = string.Join("/", pathSegments);
                string finalRelativeString = cleanedPath + queryAndFragment;

                // Safely combine base URI with the escaped relative path
                if (Uri.TryCreate(baseUri, finalRelativeString, out Uri resolvedUri))
                {
                    return resolvedUri.AbsoluteUri;
                }
            }

            return imgSrc.Replace(" ", "%20");
        }

        private void SubmitForm(
            string formAction,
            string formMethod,
            string targetFormUrl,
            List<(string Name, string Type, TextBox Box, CheckBox CheckBoxElement)> formElements,
            Action<string, Dictionary<string, string>, string> onNavigate)
        {
            if (string.IsNullOrEmpty(targetFormUrl)) return;

            Dictionary<string, string> formData = new Dictionary<string, string>();
            List<string> queryParams = new List<string>();

            foreach (var element in formElements)
            {
                if (element.Box != null)
                {
                    string val = element.Box.Text.Trim();
                    if (!string.IsNullOrEmpty(element.Name))
                    {
                        formData[element.Name] = val;
                        queryParams.Add($"{Uri.EscapeDataString(element.Name)}={Uri.EscapeDataString(val)}");
                    }
                }
                else if (element.Type == "checkbox" && element.CheckBoxElement != null)
                {
                    if (element.CheckBoxElement.IsChecked == true)
                    {
                        string checkVal = !string.IsNullOrEmpty(element.CheckBoxElement.Tag?.ToString()) ? element.CheckBoxElement.Tag.ToString() : "on";
                        formData[element.Name] = checkVal;
                        queryParams.Add($"{Uri.EscapeDataString(element.Name)}={Uri.EscapeDataString(checkVal)}");
                    }
                }
            }

            if (string.IsNullOrEmpty(formMethod) || formMethod == "get")
            {
                string separator = targetFormUrl.Contains("?") ? "&" : "?";
                string submitUrl = queryParams.Count > 0 ? $"{targetFormUrl}{separator}{string.Join("&", queryParams)}" : targetFormUrl;

                if (Uri.IsWellFormedUriString(submitUrl, UriKind.Absolute))
                {
                    onNavigate?.Invoke(submitUrl, null, "GET");
                }
            }
            else if (formMethod == "post")
            {
                if (Uri.IsWellFormedUriString(targetFormUrl, UriKind.Absolute))
                {
                    onNavigate?.Invoke(targetFormUrl, formData, "POST");
                }
            }
        }

        private void ParseContainerContent(string htmlSnippet, Section containerSection, List<CssRule> rules, Action<string, Dictionary<string, string>, string> onNavigate, string basePageUrl)
        {
            // If the container is primarily a list of links (like Wikipedia language bars or nav menus), render them as buttons
            MatchCollection linkMatches = Regex.Matches(htmlSnippet, @"<a\s+([^>]*?)>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            if (linkMatches.Count > 3 && !htmlSnippet.Contains("<p>") && !htmlSnippet.Contains("<h1>"))
            {
                StackPanel flexPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 4, 0, 4)
                };

                AddFlexChildContent(htmlSnippet, flexPanel, onNavigate);
                if (flexPanel.Children.Count > 0)
                {
                    containerSection.Blocks.Add(new BlockUIContainer(flexPanel));
                    return;
                }
            }

            // Standard fallback for text containers
            string pattern = @"<(h[1-6]|p)(?:\s+([^>]*))?>(.*?)</\1>";
            MatchCollection matches = Regex.Matches(htmlSnippet, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            if (matches.Count > 0)
            {
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
            else
            {
                Paragraph p = new Paragraph();
                p.FontSize = 14;
                p.Margin = new Thickness(0, 0, 0, 12);
                AddInlineContent(htmlSnippet, p, onNavigate);
                if (p.Inlines.Count > 0)
                {
                    containerSection.Blocks.Add(p);
                }
            }

            // Parse <img> tags nested inside containers
            MatchCollection imgMatches = Regex.Matches(htmlSnippet, @"<img[^>]+src\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))[^>]*>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match imgMatch in imgMatches)
            {
                string imgSrc = imgMatch.Groups[1].Success ? imgMatch.Groups[1].Value :
                               imgMatch.Groups[2].Success ? imgMatch.Groups[2].Value :
                               imgMatch.Groups[3].Value;

                if (!string.IsNullOrEmpty(imgSrc))
                {
                    imgSrc = ResolveImageUrl(imgSrc, basePageUrl);
                    string finalImgSrc = imgSrc;

                    try
                    {
                        using (HttpClient client = new HttpClient())
                        {
                            client.Timeout = TimeSpan.FromSeconds(5);
                            client.DefaultRequestHeaders.UserAgent.ParseAdd(GetUserAgent());

                            byte[] imageBytes = client.GetByteArrayAsync(finalImgSrc).Result;

                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                try
                                {
                                    BitmapImage bitmap = new BitmapImage();
                                    using (MemoryStream ms = new MemoryStream(imageBytes))
                                    {
                                        bitmap.BeginInit();
                                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                        bitmap.StreamSource = ms;
                                        bitmap.EndInit();
                                        bitmap.Freeze();
                                    }

                                    System.Windows.Controls.Image wpfImage = new System.Windows.Controls.Image
                                    {
                                        Source = bitmap,
                                        MaxWidth = 400,
                                        MaxHeight = 300,
                                        Stretch = Stretch.Uniform,
                                        HorizontalAlignment = HorizontalAlignment.Left,
                                        Margin = new Thickness(0, 8, 0, 8)
                                    };

                                    containerSection.Blocks.Add(new BlockUIContainer(wpfImage));
                                }
                                catch (Exception exInner)
                                {
                                    Console.WriteLine($"[Container Bitmap Error] '{finalImgSrc}': {exInner.Message}");
                                }
                            });
                        }
                    }
                    catch (Exception exNet)
                    {
                        Console.WriteLine($"[Container Image Download Error] '{finalImgSrc}': {exNet.Message}");
                    }
                }
            }
        }

        private void AddFlexChildContent(string innerHtml, StackPanel flexPanel, Action<string, Dictionary<string, string>, string> onNavigate)
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
                            onNavigate?.Invoke(targetUrl, null, "GET");
                        }
                    };
                }

                flexPanel.Children.Add(btn);
            }
        }

        private void AddInlineContent(string htmlSnippet, Paragraph p, Action<string, Dictionary<string, string>, string> onNavigate)
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
                        onNavigate?.Invoke(args.Uri.ToString(), null, "GET");
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
                    Match match = Regex.Matches(cssColor, @"rgba?\s*\(\s*(\d+),\s*(\d+),\s*(\d+)(?:,\s*[\d\.]+)?\s*\)")[0];
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