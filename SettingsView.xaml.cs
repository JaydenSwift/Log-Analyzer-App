using System.ComponentModel;
using System.Windows.Controls;
using System.Windows; // Added for MessageBox and RoutedEventArgs
using Microsoft.Win32; // Added for OpenFileDialog
using System.IO; // Added for Path.GetFileName

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

        // NEW: Property for the custom patterns file path, bound to LogDataStore
        public string CustomPatternsFilePath
        {
            get => LogDataStore.CustomPatternsFilePath;
            set
            {
                if (LogDataStore.CustomPatternsFilePath != value)
                {
                    LogDataStore.CustomPatternsFilePath = value;
                    OnPropertyChanged(nameof(CustomPatternsFilePath));
                }
            }
        }

        public SettingsView()
        {
            InitializeComponent();
            this.DataContext = this; // Set DataContext for binding
        }

        /// <summary>
        /// NEW: Handles the click for the "Browse" button to select a custom patterns.json file.
        /// </summary>
        private void BrowsePath_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "JSON Files (*.json)|*.json";
            openFileDialog.Title = "Select a Custom Pattern JSON File";

            if (openFileDialog.ShowDialog() == true)
            {
                // **REMOVED VALIDATION:**
                // The check for "patterns.json" name is removed. Any .json file is now accepted.

                // Store the selected path in the data store property
                CustomPatternsFilePath = openFileDialog.FileName;
            }
        }

        /// <summary>
        /// NEW: Handles the click for the "Clear" button to revert to the default internal pattern file.
        /// </summary>
        private void ClearPath_Click(object sender, RoutedEventArgs e)
        {
            CustomPatternsFilePath = string.Empty; // Setting to empty string reverts to default internal logic
            MessageBox.Show("Custom pattern file path cleared. The application will now use the default internal patterns.", "Reverted to Default", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // --- INotifyPropertyChanged Implementation ---
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}