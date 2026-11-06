using System.Collections.Generic;

namespace HomeNet.Parsing
{
    public class CssRule
    {
        public string Selector { get; set; }
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    }
}