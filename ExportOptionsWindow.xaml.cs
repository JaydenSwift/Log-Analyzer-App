using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Log_Analyzer_App
{
    // Enum to clearly define the export output format
    public enum ExportFormat
    {
        CSV,
        TXT,
        JSON // Added JSON format
    }

    /// <summary>
    /// Interaction logic for ExportWindow.xaml
    /// Modal dialog to select export options.
    /// </summary>
    public partial class ExportWindow : Window, INotifyPropertyChanged
    {
        // --- Properties for DataContext Binding ---

        // The list of columns, shared from the main view (LogAnalyzerView)
        public ObservableCollection<ColumnModel> ColumnControls { get; set; } = new ObservableCollection<ColumnModel>();

        // Property to hold the user's selected format (e.g., "CSV" or "TXT")
        private string _selectedFormat;
        public string SelectedFormat
        {
            get => _selectedFormat;
            set { _selectedFormat = value; OnPropertyChanged(nameof(SelectedFormat)); }
        }

        // Output property that the caller will read on DialogResult = true
        public ExportFormat FinalFormat { get; private set; }
        public ObservableCollection<ColumnModel> FinalColumns { get; private set; }

        // --- Constructor ---

        public ExportWindow(ObservableCollection<ColumnModel> availableColumns)
        {
            InitializeComponent();
            this.DataContext = this;

            // Deep clone the column models so changes in the modal don't affect the main view 
            // until the user clicks 'Export'.
            foreach (var col in availableColumns)
            {
                ColumnControls.Add(new ColumnModel
                {
                    Header = col.Header,
                    FieldName = col.FieldName,
                    IsVisible = col.IsVisible // Preserve initial visibility state
                });
            }

            // Select CSV as the default format
            FormatComboBox.SelectedIndex = 0;
        }

        // --- Event Handlers ---

        /// <summary>
        /// Handles the 'Select All' checkbox.
        /// </summary>
        private void SelectAll_Checked(object sender, RoutedEventArgs e)
        {
            foreach (var col in ColumnControls)
            {
                col.IsVisible = true;
            }
        }

        /// <summary>
        /// Handles the 'Deselect All' checkbox.
        /// </summary>
        private void SelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (var col in ColumnControls)
            {
                col.IsVisible = false;
            }
        }

        /// <summary>
        /// Handles the 'Export' button click. Validates input and sets the DialogResult to true.
        /// </summary>
        private void Export_Click(object sender, RoutedEventArgs e)
        {
            // 1. Validate: Ensure at least one column is selected
            if (!ColumnControls.Any(c => c.IsVisible))
            {
                MessageBox.Show("Please select at least one column to export.", "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. Determine the final format
            if (SelectedFormat?.Contains("CSV") == true)
            {
                FinalFormat = ExportFormat.CSV;
            }
            else if (SelectedFormat?.Contains("TXT") == true)
            {
                FinalFormat = ExportFormat.TXT;
            }
            else if (SelectedFormat?.Contains("JSON") == true)
            {
                FinalFormat = ExportFormat.JSON;
            }
            else
            {
                MessageBox.Show("Please select an export file format.", "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 3. Store final choices
            FinalColumns = ColumnControls;

            this.DialogResult = true;
            this.Close();
        }

        /// <summary>
        /// Closes the dialog without exporting.
        /// </summary>
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        // --- INotifyPropertyChanged Implementation ---
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}