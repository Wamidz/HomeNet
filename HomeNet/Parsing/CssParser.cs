using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace HomeNet.Parsing
{
    public class CssParser
    {
        public List<CssRule> ParseStyleTags(string htmlContent)
        {
            List<CssRule> rules = new List<CssRule>();
            MatchCollection styleMatches = Regex.Matches(htmlContent, @"<style[^>]*>(.*?)</style>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            foreach (Match match in styleMatches)
            {
                rules.AddRange(ParseCssString(match.Groups[1].Value));
            }

            return rules;
        }

        public List<CssRule> ParseCssString(string cssText)
        {
            List<CssRule> rules = new List<CssRule>();
            cssText = Regex.Replace(cssText, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

            MatchCollection ruleMatches = Regex.Matches(cssText, @"([^{]+)\s*\{\s*([^}]+)\s*\}", RegexOptions.Singleline);

            foreach (Match match in ruleMatches)
            {
                string selectorGroup = match.Groups[1].Value.Trim();
                string body = match.Groups[2].Value.Trim();

                string[] individualSelectors = selectorGroup.Split(',');

                foreach (string sel in individualSelectors)
                {
                    CssRule rule = new CssRule { Selector = sel.Trim().ToLower() };

                    string[] declarations = body.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string decl in declarations)
                    {
                        string[] parts = decl.Split(':');
                        if (parts.Length == 2)
                        {
                            rule.Properties[parts[0].Trim().ToLower()] = parts[1].Trim();
                        }
                    }

                    rules.Add(rule);
                }
            }

            return rules;
        }

        public Dictionary<string, string> ResolveStylesForElement(List<CssRule> rules, string tagName, string className, string idName)
        {
            Dictionary<string, string> resolvedProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            tagName = tagName?.ToLower() ?? string.Empty;
            idName = idName?.ToLower() ?? string.Empty;

            List<string> classes = new List<string>();
            if (!string.IsNullOrEmpty(className))
            {
                classes.AddRange(className.ToLower().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            }

            foreach (var rule in rules)
            {
                string sel = rule.Selector;
                bool isMatch = false;

                if (sel == tagName)
                {
                    isMatch = true;
                }
                else if (sel.StartsWith("#") && sel.Substring(1) == idName)
                {
                    isMatch = true;
                }
                else if (sel.StartsWith(".") && classes.Contains(sel.Substring(1)))
                {
                    isMatch = true;
                }
                else if (sel.Contains(".") && !sel.StartsWith("."))
                {
                    string[] parts = sel.Split('.');
                    if (parts[0] == tagName && classes.Contains(parts[1]))
                    {
                        isMatch = true;
                    }
                }

                if (isMatch)
                {
                    foreach (var prop in rule.Properties)
                    {
                        resolvedProperties[prop.Key] = prop.Value;
                    }
                }
            }

            return resolvedProperties;
        }
    }
}