using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Documents;

namespace HomeNet.Views
{
    public class HistoryEntry
    {
        public string Url { get; set; }
        public string Method { get; set; } = "GET"; // "GET" or "POST"
        public Dictionary<string, string> PostData { get; set; } // Stores form data for POST history
    }

    public class BrowserTabState : INotifyPropertyChanged
    {
        private string _title = "New Tab";
        public string Title
        {
            get => _title;
            set
            {
                _title = value;
                OnPropertyChanged(nameof(Title));
            }
        }

        private string _currentUrl;
        public string CurrentUrl
        {
            get => _currentUrl;
            set
            {
                _currentUrl = value;
                OnPropertyChanged(nameof(CurrentUrl));
            }
        }

        // Renamed to 'Document' to match MainWindow.xaml.cs calls
        public FlowDocument Document { get; set; } = new FlowDocument();

        // Added history stack and index required by tab navigation
        public List<HistoryEntry> History { get; set; } = new List<HistoryEntry>();
        public int HistoryIndex { get; set; } = -1;

        // PropertyChanged implementation
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}