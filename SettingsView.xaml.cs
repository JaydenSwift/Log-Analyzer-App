using System.ComponentModel;
using System.Windows.Controls;

namespace Log_Analyzer_App
{
    /// <summary>
    /// Interaction logic for SettingsView.xaml
    /// </summary>
    public partial class SettingsView : UserControl, INotifyPropertyChanged
    {
        // Property backing field is initialized using the current theme state
        private bool _isDarkModeEnabled = ThemeManager.IsDarkModeEnabled();

        /// <summary>
        /// Gets or sets a value indicating whether Dark Mode is enabled.
        /// Updates the application theme when set.
        /// </summary>
        public bool IsDarkModeEnabled
        {
            get => _isDarkModeEnabled;
            set
            {
                if (_isDarkModeEnabled != value)
                {
                    _isDarkModeEnabled = value;
                    ThemeManager.ApplyTheme(value); // Apply the new theme
                    OnPropertyChanged(nameof(IsDarkModeEnabled));
                }
            }
        }

        public SettingsView()
        {
            InitializeComponent();
            this.DataContext = this; // Set DataContext for binding
        }

        // --- INotifyPropertyChanged Implementation ---
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}