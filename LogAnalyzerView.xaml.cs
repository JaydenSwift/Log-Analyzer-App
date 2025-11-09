using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using System.ComponentModel;
using System.Collections; // Needed for ICollectionView
using System.Windows.Media; // Explicitly imported for Brush, Color, SolidColorBrush

namespace Log_Analyzer_App
{
    // --- New Model for Dynamic Summary Stats ---
    public class CountItem : INotifyPropertyChanged
    {
        private string _key;
        public string Key
        {
            get => _key;
            set { _key = value; OnPropertyChanged(nameof(Key)); }
        }

        private double _count;
        public double Count
        {
            get => _count;
            set { _count = value; OnPropertyChanged(nameof(Count)); }
        }

        private System.Windows.Media.Brush _color;
        public System.Windows.Media.Brush Color
        {
            get => _color;
            set { _color = value; OnPropertyChanged(nameof(Color)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }


    // --- Log Pattern and Python Response Models (Unchanged) ---
    public class LogPatternDefinition
    {
        public string Pattern { get; set; }
        public string Description { get; set; }
        // The list of user-defined column names corresponding to regex capture groups (Group 1, 2, 3...)
        public List<string> FieldNames { get; set; } = new List<string>();
    }

    public class PythonParserResponse
    {
        public bool Success { get; set; }
        public List<LogEntry> Data { get; set; }
        public string Error { get; set; }
    }

    public class PythonSuggesterResponse
    {
        public bool Success { get; set; }
        public LogPatternDefinition Data { get; set; }
        public string Error { get; set; }
    }


    /// <summary>
    /// Static class to hold the application's persistent log data.
    /// </summary>
    public static class LogDataStore
    {
        // Persistent collection of log entries
        public static ObservableCollection<LogEntry> LogEntries { get; } = new ObservableCollection<LogEntry>();
        // Persistent file path
        public static string CurrentFilePath { get; set; } = "No log file loaded.";
        public static string SelectedFileForParsing { get; set; } = null;
        public static string OriginalFilePath { get; set; } = null; // Store original path for parsing reuse

        // NEW: Centralized pattern definition store
        public static LogPatternDefinition CurrentPatternDefinition { get; set; }

        // Default pattern definition (now redundant as it's defined in Python, but kept as a fallback)
        public static LogPatternDefinition DefaultPatternDefinition = new LogPatternDefinition
        {
            Pattern = @"^\[(.*?)\]\s*(INFO|WARN|ERROR):\s*(.*)$",
            Description = "Default Pattern: Captures [Timestamp], Level (INFO|WARN|ERROR), and Message.",
            FieldNames = new List<string> { "Timestamp", "Level", "Message" }
        };

        // NEW: Stores the first non-empty line of the selected file for preview
        public static string FirstLogLine { get; set; } = string.Empty;

        // NEW: Stores the result of parsing the first line with the current pattern
        public static LogEntry ParsedFirstLine { get; set; } = new LogEntry();

        // MODIFIED: ObservableCollection for dynamic summary statistics binding
        public static ObservableCollection<CountItem> SummaryCounts { get; } = new ObservableCollection<CountItem>();

        // NEW: Static property to hold the *last used* field name for LogAnalyzerView
        public static string LastStatsFieldName { get; set; } = "Level";

        // Predefined color brushes for common levels
        private static readonly Dictionary<string, System.Windows.Media.Brush> LevelColors = new Dictionary<string, System.Windows.Media.Brush>(StringComparer.OrdinalIgnoreCase)
        {
            { "INFO", System.Windows.Media.Brushes.Blue },
            { "WARN", System.Windows.Media.Brushes.Orange },
            { "ERROR", System.Windows.Media.Brushes.Red },
            { "DEBUG", System.Windows.Media.Brushes.Gray },
            { "FATAL", System.Windows.Media.Brushes.DarkRed },
        };

        // NEW: Cache for dynamically generated colors for non-standard keys (ensures consistency)
        private static readonly Dictionary<string, System.Windows.Media.Brush> DynamicKeyColors = new Dictionary<string, System.Windows.Media.Brush>(StringComparer.OrdinalIgnoreCase);

        // Static Random instance for color generation
        private static readonly Random Rnd = new Random();

        static LogDataStore()
        {
            // Initialize the current pattern with the default one
            CurrentPatternDefinition = DefaultPatternDefinition;
        }

        /// <summary>
        /// NEW: Generates a random SolidColorBrush with a relatively high saturation to be visible on white background.
        /// Uses HSL color space for better color distribution and contrast.
        /// </summary>
        private static System.Windows.Media.SolidColorBrush GetRandomBrush()
        {
            // Generate random HSL colors and convert to RGB for better distribution of vivid colors
            // Use Saturation > 0.7 and Lightness around 0.5 to ensure it's not too pale or too dark.
            double hue = Rnd.NextDouble() * 360;
            double saturation = Rnd.NextDouble() * 0.3 + 0.7; // 0.7 to 1.0 (high saturation)
            double lightness = Rnd.NextDouble() * 0.2 + 0.4;  // 0.4 to 0.6 (medium lightness)

            // HSL to RGB conversion
            var color = ColorFromHsl(hue, saturation, lightness);

            return new System.Windows.Media.SolidColorBrush(color);
        }

        /// <summary>
        /// Helper function to convert HSL (0-360, 0-1, 0-1) to System.Windows.Media.Color
        /// </summary>
        private static System.Windows.Media.Color ColorFromHsl(double h, double s, double l)
        {
            double r, g, b;
            if (s == 0)
            {
                r = g = b = l; // achromatic
            }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                r = HueToRgb(p, q, h + 120);
                g = HueToRgb(p, q, h);
                b = HueToRgb(p, q, h - 120);
            }

            return System.Windows.Media.Color.FromRgb(
                (byte)(r * 255),
                (byte)(g * 255),
                (byte)(b * 255)
            );
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 360;
            if (t > 360) t -= 360;
            if (t < 60) return p + (q - p) * t / 60;
            if (t < 180) return q;
            if (t < 240) return p + (q - p) * (240 - t) / 60;
            return p;
        }


        /// <summary>
        /// NEW: Calculates the count for all unique values in a specific field, returning a new collection.
        /// This is used by the ChartViewer for independent analysis.
        /// </summary>
        /// <param name="statsFieldName">The name of the column/field to group by.</param>
        /// <returns>A new ObservableCollection<CountItem> representing the counts.</returns>
        public static ObservableCollection<CountItem> GetDynamicSummaryCounts(string statsFieldName)
        {
            var dynamicCounts = new ObservableCollection<CountItem>();
            string fieldName = statsFieldName;

            if (!LogEntries.Any() || string.IsNullOrEmpty(fieldName))
            {
                return dynamicCounts;
            }

            // Group by the value of the identified statistics field
            var counts = LogEntries
                // Safely try to get the field's value
                .Where(e => e.Fields.ContainsKey(fieldName))
                .Select(e => e.Fields[fieldName].Trim())
                .Where(fieldValue => !string.IsNullOrEmpty(fieldValue) && fieldValue != "N/A")
                // Group by the value (Key is the actual value, e.g., "INFO" or "Thread-1")
                .GroupBy(fieldValue => fieldValue)
                .OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => (double)g.Count());

            // Add grouped results to the ObservableCollection
            foreach (var kvp in counts)
            {
                System.Windows.Media.Brush color;

                // 1. Check Predefined Colors (for common levels like INFO/ERROR)
                if (LevelColors.ContainsKey(kvp.Key))
                {
                    color = LevelColors[kvp.Key];
                }
                // 2. Check Dynamic Cache (for consistency of non-standard keys)
                else if (DynamicKeyColors.ContainsKey(kvp.Key))
                {
                    color = DynamicKeyColors[kvp.Key];
                }
                // 3. Generate New Random Color and Cache it
                else
                {
                    color = GetRandomBrush();
                    DynamicKeyColors[kvp.Key] = color;
                }

                dynamicCounts.Add(new CountItem
                {
                    Key = kvp.Key,
                    Count = kvp.Value,
                    Color = color
                });
            }

            return dynamicCounts;
        }

        /// <summary>
        /// MODIFIED: Calculates the count for all unique values in the designated field 
        /// and updates the static SummaryCounts collection for the Log Analyzer tab.
        /// </summary>
        /// <param name="statsFieldName">The name of the column/field to group by (defaulting to LastStatsFieldName).</param>
        public static void CalculateSummaryCounts(string statsFieldName = null)
        {
            // If the field name is not passed, use the last remembered field name.
            string fieldName = statsFieldName ?? LastStatsFieldName;

            // Clear the existing observable collection
            SummaryCounts.Clear();

            if (!LogEntries.Any() || string.IsNullOrEmpty(fieldName))
            {
                return;
            }

            // Update the last used field name
            LastStatsFieldName = fieldName;

            // Get dynamic counts for the determined field
            var newCounts = GetDynamicSummaryCounts(fieldName);

            // Populate the static collection
            foreach (var item in newCounts)
            {
                SummaryCounts.Add(item);
            }
        }
    }

    // NEW: Model for dynamically binding column visibility
    public class ColumnModel : INotifyPropertyChanged
    {
        public string Header { get; set; }
        public string FieldName { get; set; } // Matches the key in LogEntry.Fields

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; OnPropertyChanged(nameof(IsVisible)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Interaction logic for LogAnalyzerView.xaml
    /// </summary>
    public partial class LogAnalyzerView : UserControl, INotifyPropertyChanged
    {
        private ObservableCollection<LogEntry> _logEntries = LogDataStore.LogEntries;

        // NEW: Collection View Source for Filtering and Sorting
        private CollectionViewSource LogCollectionViewSource { get; set; }

        // NEW: Observable collection to hold the Column Visibility Models
        public ObservableCollection<ColumnModel> ColumnControls { get; set; } = new ObservableCollection<ColumnModel>();

        // FIX: Non-static class to hold preview binding data and implement INotifyPropertyChanged
        public class LogPreviewModel : INotifyPropertyChanged
        {
            private string _firstLogLine;
            // CRITICAL FIX: To notify the UI when the Fields dictionary changes (which drives the dynamic ItemsControl), 
            // we must replace the whole LogEntry object when the pattern changes.
            private LogEntry _parsedFirstLine = new LogEntry();

            public string FirstLogLine
            {
                get => _firstLogLine;
                set { _firstLogLine = value; OnPropertyChanged(nameof(FirstLogLine)); }
            }

            public LogEntry ParsedFirstLine
            {
                get => _parsedFirstLine;
                set { _parsedFirstLine = value; OnPropertyChanged(nameof(ParsedFirstLine)); }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected virtual void OnPropertyChanged(string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }

            // Method to pull data from the static store and update properties
            public void UpdateFromStore(List<string> fieldOrder = null)
            {
                FirstLogLine = LogDataStore.FirstLogLine;

                // Create a new LogEntry instance to ensure deep property change notification
                // and assign the correct field order for derived properties
                // This ensures the ItemsControl on the XAML side (which binds to ParsedFirstLine.Fields) refreshes.
                ParsedFirstLine = new LogEntry
                {
                    FieldOrder = fieldOrder ?? LogDataStore.CurrentPatternDefinition.FieldNames
                };

                // Copy the results over to the INPC instance here for binding.
                foreach (var kvp in LogDataStore.ParsedFirstLine.Fields)
                {
                    ParsedFirstLine.Fields.Add(kvp.Key, kvp.Value);
                }

                // Manually fire property change notification
                OnPropertyChanged(nameof(ParsedFirstLine));
            }
        }

        public LogPreviewModel PreviewModel { get; set; } = new LogPreviewModel();


        // NEW: Property to hold the dynamic Summary Counts for binding
        public ObservableCollection<CountItem> SummaryCounts { get; set; } = LogDataStore.SummaryCounts;

        // NEW: Observable collection to populate the ComboBox with available field names
        private ObservableCollection<string> _availableStatsFields = new ObservableCollection<string>();
        public ObservableCollection<string> AvailableStatsFields
        {
            get => _availableStatsFields;
            set { _availableStatsFields = value; OnPropertyChanged(nameof(AvailableStatsFields)); }
        }

        // NEW: Property to store the currently selected stats field (for TwoWay binding to ComboBox)
        private string _selectedStatsField;
        public string SelectedStatsField
        {
            get => _selectedStatsField;
            set
            {
                if (_selectedStatsField != value)
                {
                    _selectedStatsField = value;
                    LogDataStore.CalculateSummaryCounts(value); // Trigger calculation with the new field
                    OnPropertyChanged(nameof(SummaryCounts)); // Force UI refresh on the ItemsControl
                    OnPropertyChanged(nameof(SelectedStatsField));
                }
            }
        }


        public LogAnalyzerView()
        {
            InitializeComponent();

            this.DataContext = this;

            // --- NEW: Initialize CollectionViewSource for filtering ---
            LogCollectionViewSource = new CollectionViewSource();
            // Assign the raw log data to the source
            LogCollectionViewSource.Source = _logEntries;
            // Add the filter event handler (the grep logic)
            LogCollectionViewSource.Filter += LogFilter;

            // Bind the DataGrid's ItemsSource to the filtered/sorted view
            LogDataGrid.ItemsSource = LogCollectionViewSource.View;
            // ---------------------------------------------------------

            // Since we set AutoGenerateColumns=False in XAML, we only need to call SetupDynamicColumns 
            // when data is loaded, not on AutoGeneratingColumn event.

            // Ensure columns are set up on startup if data already exists (e.g., app restart)
            if (_logEntries.Any())
            {
                InitializeColumnControls(LogDataStore.CurrentPatternDefinition.FieldNames);
                SetupDynamicColumns();
            }

            // NEW: Initialize the field lists on startup
            UpdateAvailableStatsFields(LogDataStore.CurrentPatternDefinition.FieldNames);

            // Update UI with persistent data on initialization
            RefreshView();

            ShowParserStatus(false);
        }

        /// <summary>
        /// NEW: Updates the ComboBox items with the current field names and selects the last used field.
        /// </summary>
        private void UpdateAvailableStatsFields(List<string> fieldNames)
        {
            AvailableStatsFields.Clear();
            foreach (var fieldName in fieldNames)
            {
                AvailableStatsFields.Add(fieldName);
            }
            // If the last used field name is still available, select it, otherwise, default to the first field.
            if (fieldNames.Contains(LogDataStore.LastStatsFieldName))
            {
                SelectedStatsField = LogDataStore.LastStatsFieldName;
            }
            else if (fieldNames.Any())
            {
                // Default to the field named 'Level' if present, or the first field.
                string defaultField = fieldNames.FirstOrDefault(name => name.Equals("Level", StringComparison.OrdinalIgnoreCase));
                SelectedStatsField = defaultField ?? fieldNames[0];
            }
            else
            {
                SelectedStatsField = null; // No fields available
            }
        }

        /// <summary>
        /// NEW: Initializes the ColumnControls collection based on the current parsed fields.
        /// </summary>
        private void InitializeColumnControls(List<string> fieldNames)
        {
            ColumnControls.Clear();
            foreach (var fieldName in fieldNames)
            {
                // Ensure the Header/FieldName is capitalized for better display
                string displayHeader = char.ToUpper(fieldName[0]) + fieldName.Substring(1);
                ColumnControls.Add(new ColumnModel { Header = displayHeader, FieldName = fieldName, IsVisible = true });
            }
        }

        /// <summary>
        /// NEW: Updates the visibility of the corresponding DataGrid column when the checkbox state changes.
        /// </summary>
        private void ColumnVisibility_Toggled(object sender, RoutedEventArgs e)
        {
            // Find the DataGrid column corresponding to the toggled checkbox and update its visibility.
            if (sender is CheckBox checkBox && checkBox.DataContext is ColumnModel model)
            {
                // Find the column by its header (which matches the model's field name for DataGridTextColumn)
                // NOTE: We match against the raw fieldName, not the capitalized Header, because SetupDynamicColumns uses the raw fieldName as the header value.
                var dataGridColumn = LogDataGrid.Columns.FirstOrDefault(
                    c => c.Header.ToString() == model.FieldName
                );

                if (dataGridColumn != null)
                {
                    // Set the WPF column visibility based on the model state
                    dataGridColumn.Visibility = model.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// Grep-like search implementation. Filters the collection based on keyword search.
        /// (Date Range Filter has been removed as requested).
        /// </summary>
        private void LogFilter(object sender, FilterEventArgs e)
        {
            if (!(e.Item is LogEntry logEntry))
            {
                e.Accepted = false;
                return;
            }

            // --- 1. Keyword (Grep) Filter ---
            string searchTerm = SearchTextBox.Text.Trim();
            bool isInverted = InvertFilterCheckBox.IsChecked ?? false;

            // If the search box is empty, the keyword filter is always successful (match=true)
            bool keywordMatch = true;

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                // Prepare keywords for case-insensitive AND search
                string[] keywords = searchTerm
                    .ToLower()
                    .Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                // Reset match assumption to false if we have keywords to check
                keywordMatch = false;

                if (keywords.Any())
                {
                    bool allKeywordsFound = true;
                    foreach (string keyword in keywords)
                    {
                        bool keywordFoundInEntry = false;

                        // Check if the current keyword is found in ANY field
                        foreach (var field in logEntry.Fields)
                        {
                            if (field.Value != null && field.Value.ToLower().Contains(keyword))
                            {
                                keywordFoundInEntry = true;
                                break;
                            }
                        }

                        if (!keywordFoundInEntry)
                        {
                            allKeywordsFound = false;
                            break;
                        }
                    }

                    // The log entry is considered a keyword match if ALL keywords were found.
                    keywordMatch = allKeywordsFound;
                }
                else
                {
                    // If searchTerm was non-empty but produced no keywords (e.g., just spaces),
                    // we stick with keywordMatch = true to not filter anything out unnecessarily.
                    keywordMatch = true;
                }
            }

            // --- Final Acceptance Logic ---
            // If inverted, accept if there was NO match.
            // If not inverted, accept if there WAS a match.
            e.Accepted = isInverted ? !keywordMatch : keywordMatch;
        }


        /// <summary>
        /// Event handler for the SearchTextBox. Refreshes the CollectionViewSource filter 
        /// whenever the search text changes to immediately update the DataGrid.
        /// </summary>
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // This forces the LogFilter method to run again on every item in the collection
            if (LogCollectionViewSource?.View != null)
            {
                LogCollectionViewSource.View.Refresh();
            }
        }

        /// <summary>
        /// NEW: Event handler for the InvertFilterCheckBox.
        /// </summary>
        private void InvertFilter_CheckedOrUnchecked(object sender, RoutedEventArgs e)
        {
            // This forces the LogFilter method to run again, applying the new inversion state
            if (LogCollectionViewSource?.View != null)
            {
                LogCollectionViewSource.View.Refresh();
            }
        }

        // Removed: DateRange_SelectedDateChanged handler (as requested)


        /// <summary>
        /// NEW: Manually creates and configures the DataGrid columns based on the user's current pattern definition.
        /// This method is crucial for dynamic column support.
        /// </summary>
        private void SetupDynamicColumns()
        {
            // Clear all existing columns before setting up the new ones
            LogDataGrid.Columns.Clear();

            List<string> fieldNames = LogDataStore.CurrentPatternDefinition.FieldNames;

            // Check if there are field names to display
            if (!fieldNames.Any())
            {
                // Fallback: If no fields defined (e.g., bad regex), add a generic Message column
                fieldNames = new List<string> { "Message" };
            }

            // NOTE: We rely on the initial ColumnControls collection to set the visibility.

            // Identify the last column for the Auto sizing with large MinWidth
            string lastFieldName = fieldNames.LastOrDefault();

            // Iterate through the user-defined field names in their correct order
            foreach (string fieldName in fieldNames)
            {
                // Create a new TextColumn
                var column = new DataGridTextColumn
                {
                    // The Header must be set to the FieldName for the ColumnVisibility_Toggled handler to work
                    // This is IMPORTANT: we use the raw field name as the Header for lookup
                    Header = fieldName,
                    IsReadOnly = true,
                    // Set the binding path to look into the LogEntry's Fields dictionary
                    Binding = new Binding($"Fields[{fieldName}]")
                };

                // Apply initial visibility from the ColumnControls model if it exists
                var columnModel = ColumnControls.FirstOrDefault(c => c.FieldName == fieldName);
                if (columnModel != null)
                {
                    column.Visibility = columnModel.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                }


                // CRITICAL FIX: Use DataGridLength.Auto for all columns.
                // The DataGrid will size the column to fit the widest content/header.
                column.Width = DataGridLength.Auto;

                // Set fixed minimum widths on predictable fields (Timestamp, Level) 
                // and a very large minimum width on the last column (Message) to ensure 
                // the DataGrid's total width exceeds the viewport, activating the scrollbar
                // and preventing truncation.
                if (fieldName.Contains("Timestamp", StringComparison.OrdinalIgnoreCase))
                {
                    column.MinWidth = 180;
                }
                else if (fieldName.Contains("Level", StringComparison.OrdinalIgnoreCase))
                {
                    column.MinWidth = 80;
                }
                else if (fieldName == lastFieldName)
                {
                    // Large MinWidth on the message column ensures that the content is 
                    // shown without truncation, and the horizontal scrollbar appears 
                    // if the window is small.
                    column.MinWidth = 100;
                }
                // For other short dynamic fields, Auto should suffice, but we'll add a minimal MinWidth.
                else
                {
                    column.MinWidth = 100;
                }

                // Add the column to the DataGrid
                LogDataGrid.Columns.Add(column);
            }
        }


        /// <summary>
        /// Updates the view's display elements (FilePath, Status, Counts) 
        /// using the persistent data in LogDataStore.
        /// </summary>
        private void RefreshView()
        {
            FilePathTextBlock.Text = LogDataStore.CurrentFilePath;

            // Update the pattern display elements (only visible when a file is selected)
            PatternDisplayTextBlock.Text = LogDataStore.CurrentPatternDefinition.Pattern;
            // FIX: Removed duplicate CurrentPatternDefinition access
            PatternDescriptionTextBlock.Text = LogDataStore.CurrentPatternDefinition.Description;

            // Update the dynamic summary statistics display
            UpdateSummaryStatistics();

            // Crucial: Set the state of the parsing panel based on whether a file is selected for parsing
            ShowParserStatus(false);
        }

        /// <summary>
        /// Helper to control visibility of the parsing prompt/status UI
        /// based on the application state.
        /// </summary>
        /// <param name="isParsing">True if the Python process is currently running.</param>
        private void ShowParserStatus(bool isParsing)
        {
            // If currently running the parser (show loading/status text)
            if (isParsing)
            {
                ParsingChoicePanel.Visibility = Visibility.Collapsed;
            }
            // If a file is selected and we need a decision (show choice panel)
            else if (LogDataStore.SelectedFileForParsing != null || LogDataStore.OriginalFilePath != null)
            {
                ParsingChoicePanel.Visibility = Visibility.Visible;
            }
            // If no file is selected/loaded, or parsing is complete and successful
            else
            {
                ParsingChoicePanel.Visibility = Visibility.Collapsed;
                // If log entries exist, show the Run Analysis button for future filtering features
            }
        }

        /// <summary>
        /// Attempts to read the first non-empty line of the file for preview purposes.
        /// </summary>
        private string GetFirstLogLine(string filePath)
        {
            try
            {
                using (var reader = new StreamReader(filePath))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            return line.Trim();
                        }
                    }
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading first line: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Tests the currently suggested pattern against the first line of the log.
        /// </summary>
        private void TestParsingPatternOnFirstLine()
        {
            // Reset the state object in the static store
            LogDataStore.ParsedFirstLine.Fields.Clear();
            LogDataStore.ParsedFirstLine.FieldOrder = LogDataStore.CurrentPatternDefinition.FieldNames;

            string pattern = LogDataStore.CurrentPatternDefinition.Pattern;
            List<string> fieldNames = LogDataStore.CurrentPatternDefinition.FieldNames;

            if (string.IsNullOrWhiteSpace(LogDataStore.FirstLogLine) || string.IsNullOrWhiteSpace(pattern))
            {
                return;
            }

            try
            {
                // NOTE: We rely on the C# Regex object here which requires the user to use regex syntax, 
                // but the final parsing in the Python script uses the 'parse' library. 
                // This is a known limitation when using the regex builder.
                var regex = new Regex(pattern);
                Match match = regex.Match(LogDataStore.FirstLogLine);

                // Group count check: match.Groups.Count is Group 0 (full match) + N capture groups.
                // We compare this to the count of expected field names (N capture groups).
                if (match.Success && match.Groups.Count - 1 == fieldNames.Count && fieldNames.Count > 0)
                {
                    // Match the capture groups (starting from 1) to the user-defined field names
                    for (int i = 0; i < fieldNames.Count; i++)
                    {
                        string fieldName = fieldNames[i];
                        string fieldValue = match.Groups[i + 1].Value.Trim();

                        LogDataStore.ParsedFirstLine.Fields[fieldName] = fieldValue;
                    }
                }
                else
                {
                    // If parsing failed or groups mismatch, set a generic error message
                    LogDataStore.ParsedFirstLine.Fields.Clear();
                    LogDataStore.ParsedFirstLine.Fields["Message"] = "Error: Pattern mismatch or capture groups count is incorrect.";
                }
            }
            catch (ArgumentException)
            {
                LogDataStore.ParsedFirstLine.Fields.Clear();
                LogDataStore.ParsedFirstLine.Fields["Message"] = "Error: Invalid Regex Pattern.";
            }
            catch (Exception)
            {
                LogDataStore.ParsedFirstLine.Fields.Clear();
                LogDataStore.ParsedFirstLine.Fields["Message"] = "Error: Matching failed.";
            }
        }


        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Placeholder for selection logic
        }

        /// <summary>
        /// Handles the click to use the suggested pattern and start parsing.
        /// CRITICAL FIX: Pass isBestEffort=true to allow partial parsing (zero matches is okay here).
        /// </summary>
        private void UseDefaultPattern_Click(object sender, RoutedEventArgs e)
        {
            // The suggested pattern is already stored in LogDataStore.CurrentPatternDefinition 
            // from the LoadFileButton_Click logic.

            // FIX: Restore SelectedFileForParsing from the original path using OriginalFilePath.
            // We use OriginalFilePath here, and then StartParsingWithPattern will clear it in finally block.
            LogDataStore.SelectedFileForParsing = LogDataStore.OriginalFilePath;

            // We just need to trigger the parsing process with the best-effort flag set to true.
            StartParsingWithPattern(isBestEffortParse: true);
        }

        /// <summary>
        /// UPDATED: Handles the click to configure a custom pattern by opening the builder modal.
        /// CRITICAL FIX: isBestEffort is now set to TRUE here as requested.
        /// </summary>
        private void CustomPattern_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(LogDataStore.OriginalFilePath) || string.IsNullOrWhiteSpace(LogDataStore.FirstLogLine))
            {
                MessageBox.Show("Please load a log file first to provide a line sample for the Regex Builder.", "Missing Log File", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Pass the current active pattern definition (which may be the suggested one) to pre-fill the modal
            var builderWindow = new RegexBuilderWindow(
                LogDataStore.FirstLogLine,
                LogDataStore.CurrentPatternDefinition
            );

            // Show the dialog modally and check the result
            if (builderWindow.ShowDialog() == true)
            {
                // The user clicked "Save and Use Pattern"

                // 1. Update persistent store with the new pattern definition
                LogDataStore.CurrentPatternDefinition = builderWindow.FinalPatternDefinition;

                // 2. Re-test and update the UI preview to reflect the chosen custom pattern
                TestParsingPatternOnFirstLine();
                PreviewModel.UpdateFromStore();

                // FIX: Restore SelectedFileForParsing from the original path so the StartParsingWithPattern 
                // logic can find the file.
                LogDataStore.SelectedFileForParsing = LogDataStore.OriginalFilePath;

                // 3. Start parsing immediately with the custom pattern. 
                // We set isBestEffortParse to TRUE as requested, allowing lines that fail to parse
                // to be displayed as [UNPARSED] instead of causing a fatal error.
                StartParsingWithPattern(isBestEffortParse: true);
            }
        }

        /// <summary>
        /// Encapsulates the core parsing logic, triggered after the user confirms the pattern.
        /// </summary>
        /// <param name="isBestEffortParse">True if partial success (0 matches) is acceptable (used by default button).</param>
        private async void StartParsingWithPattern(bool isBestEffortParse)
        {
            string filePath = LogDataStore.SelectedFileForParsing;
            LogPatternDefinition definition = LogDataStore.CurrentPatternDefinition;

            // Safety check
            if (string.IsNullOrWhiteSpace(filePath)) return;

            // 1. Update UI to show processing is starting
            LogDataStore.CurrentFilePath = $"Processing file: {Path.GetFileName(filePath)}";
            ShowParserStatus(true); // Show loading spinner/status text, hide pattern choice
            FilePathTextBlock.Text = LogDataStore.CurrentFilePath; // Update display
            LoadFileButton.IsEnabled = false; // Disable button during processing

            try
            {
                // 2. Execute Python script and get the list of log entries
                // Pass the 'parse' command along with the file details
                List<LogEntry> logEntries = await Task.Run(() => RunPythonParser(
                    "parse", // Command
                    filePath,
                    definition.Pattern,
                    definition.FieldNames,
                    isBestEffortParse // Pass the new flag
                ));

                // 3. Clear static collection and replace with new data
                _logEntries.Clear();

                // IMPORTANT: The Python script may have modified the FieldNames list if it found
                // unnamed fields (like 'unnamed_1'). We must update the C# store before setting up columns.
                if (logEntries.Any())
                {
                    // Use the FieldOrder returned by the first log entry, as this defines the new dynamic columns
                    LogDataStore.CurrentPatternDefinition.FieldNames = logEntries.First().FieldOrder;
                }

                // NEW: Initialize column controls *after* updating the final field names
                InitializeColumnControls(LogDataStore.CurrentPatternDefinition.FieldNames);
                // NEW: Update available stats fields after column list is finalized
                UpdateAvailableStatsFields(LogDataStore.CurrentPatternDefinition.FieldNames);


                // --- NEW COLUMN LOGIC: MUST BE CALLED BEFORE ADDING ENTRIES ---
                SetupDynamicColumns();
                // -------------------------

                foreach (var entry in logEntries)
                {
                    // CRITICAL: Ensure the FieldOrder is set on every entry for the derived properties to work
                    entry.FieldOrder = LogDataStore.CurrentPatternDefinition.FieldNames;
                    _logEntries.Add(entry);
                }

                // 4. Update persistent data and calculate counts
                // Calculate SummaryCounts using the currently selected field in this view
                LogDataStore.CalculateSummaryCounts(SelectedStatsField);
                LogDataStore.CurrentFilePath = $"File Loaded: {Path.GetFileName(filePath)} ({_logEntries.Count} lines) using pattern: {definition.Description}";

                // 5. Final UI update
                PreviewModel.UpdateFromStore();
                RefreshView();

                // NEW: After successful load, ensure the filter is cleared and refreshed
                SearchTextBox.Text = string.Empty;
                LogCollectionViewSource.View.Refresh();
            }
            catch (Exception ex)
            {
                // Handle errors during execution or JSON deserialization
                LogDataStore.CurrentFilePath = $"Error during analysis: {ex.Message}";
                _logEntries.Clear(); // Clear any corrupted data
                LogDataStore.CalculateSummaryCounts(); // Reset counts

                // CRITICAL: Clear columns on failure so they don't show old/corrupted data structure
                LogDataGrid.Columns.Clear();

                RefreshView();
                // Ensure the status reverts to a non-parsing state to re-enable controls
                ShowParserStatus(false);

                // MODIFIED: Only show the error message box if it was NOT a best-effort parse (i.e., custom regex was used).
                // For the default button (best-effort), we fail silently in the UI to prevent interruption.
                if (!isBestEffortParse)
                {
                    MessageBox.Show($"An error occurred during analysis: {ex.Message}", "Python Processing Error (Strict Mode)", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    // For best-effort, just update the status text
                    Console.WriteLine($"Best-effort parsing failed: {ex.Message}");
                }
            }
            finally
            {
                LogDataStore.SelectedFileForParsing = null; // Clear the temporary path
                // Note: LogDataStore.OriginalFilePath remains set for future parsing attempts
                LoadFileButton.IsEnabled = true; // Re-enable Load button
            }
        }

        /// <summary>
        /// Executes the external Python parser script for both 'parse' and 'suggest_pattern' commands.
        /// </summary>
        /// <param name="command">The command to run ('parse' or 'suggest_robust_pattern').</param>
        /// <param name="filePath">The path to the log file.</param>
        /// <param name="logPattern">Lhe custom regex pattern to use for parsing (or null).</param>
        /// <param name="fieldNames">The user-defined names for the capture groups (or null).</param>
        /// <param name="isBestEffortParse">Flag for the 'parse' command to allow zero matches (optional).</param>
        /// <returns>A List of LogEntry objects (for parse) or null.</returns>
        private List<LogEntry> RunPythonParser(string command, string filePath, string logPattern, List<string> fieldNames, bool isBestEffortParse = false)
        {
            // --- FIX: Resolve 'parse' library not found error by explicitly pointing to the VENV python.exe ---
            //
            // The Python script (log_parser.py) relies on the 'parse' library, which is likely installed 
            // only in the project's virtual environment (env). When debugging, the default 'python' 
            // command may point to a global interpreter without this package.
            //
            // We calculate the path to the virtual environment's python.exe assuming the C# executable
            // is in the output directory (e.g., bin/Debug/net8.0-windows/) and the venv is in 
            // the solution root's ParsingScript/env folder.

            string currentDir = AppDomain.CurrentDomain.BaseDirectory;
            // Traverse up three directories from the C# executable (e.g., bin/Debug/net8.0-windows/) 
            // to reach the solution root.
            string solutionRoot = Path.GetFullPath(Path.Combine(currentDir, "..", "..", ".."));

            // Construct the path to the virtual environment's python.exe
            string pythonExecutable = Path.Combine(solutionRoot, "ParsingScript", "env", "Scripts", "python.exe");

            // Fallback: If the virtual environment path is invalid (e.g., on a machine without the venv), 
            // we fall back to the system-wide 'python' executable, which relies on the user having 
            // run 'pip install parse' in their global environment.
            if (!File.Exists(pythonExecutable))
            {
                pythonExecutable = "python";
                // Log this fallback for debugging
                Console.WriteLine("Warning: Virtual environment python.exe not found. Falling back to global 'python' command.");
            }

            // The script path is relative to the C# application's output directory
            string scriptPath = "C_log_parser.py";
            string arguments;

            // FIX: Explicitly include the scriptPath in the arguments for reliable execution.
            if (command == "parse")
            {
                // For parsing, we need all four arguments
                string fieldNamesJson = JsonSerializer.Serialize(fieldNames);
                string escapedLogPattern = logPattern.Replace("\"", "\\\"");
                string escapedFieldNamesJson = fieldNamesJson.Replace("\"", "\\\"");
                string isBestEffortStr = isBestEffortParse ? "true" : "false";

                // CRITICAL FIX: Prepend the script path and append the new isBestEffort flag
                arguments = $"{scriptPath} {command} \"{filePath}\" \"{escapedLogPattern}\" \"{escapedFieldNamesJson}\" {isBestEffortStr}";
            }
            // NEW command name and logic: passing file path for robust suggestion
            else if (command == "suggest_robust_pattern")
            {
                // For suggestion, we only need the file path
                // CRITICAL FIX: Prepend the script path
                arguments = $"{scriptPath} {command} \"{filePath}\"";
            }
            else
            {
                throw new ArgumentException($"Invalid command provided to RunPythonParser: {command}");
            }

            using (Process process = new Process())
            {
                process.StartInfo.FileName = pythonExecutable;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.UseShellExecute = false;
                // We assume the script and the patterns.json file are correctly configured 
                // in the project settings (Copy to Output Directory = Always)
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true; // Don't show the Python console window

                process.Start();

                string jsonOutput = process.StandardOutput.ReadToEnd();
                string errorOutput = process.StandardError.ReadToEnd();

                process.WaitForExit();

                if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(jsonOutput))
                {
                    // Log the error output from Python for debugging
                    Console.WriteLine($"Python Stderr: {errorOutput}");
                    throw new Exception($"Python process failed with exit code {process.ExitCode}. Error: {errorOutput}");
                }

                // Handle response based on command
                if (command == "parse")
                {
                    PythonParserResponse response;
                    try
                    {
                        response = JsonSerializer.Deserialize<PythonParserResponse>(jsonOutput, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    }
                    catch (JsonException ex)
                    {
                        throw new Exception($"Failed to parse JSON output from Python. Raw Output: '{jsonOutput}'. JSON Error: {ex.Message}");
                    }

                    if (!response.Success)
                    {
                        throw new Exception($"Log parsing failed within Python script. Reason: {response.Error}");
                    }
                    return response.Data ?? new List<LogEntry>(); // Return parsed data or empty list
                }
                // NEW command name
                else if (command == "suggest_robust_pattern")
                {
                    PythonSuggesterResponse response;
                    try
                    {
                        response = JsonSerializer.Deserialize<PythonSuggesterResponse>(jsonOutput, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    }
                    catch (JsonException ex)
                    {
                        throw new Exception($"Failed to parse JSON output from Python suggester. Raw Output: '{jsonOutput}'. JSON Error: {ex.Message}");
                    }

                    if (!response.Success)
                    {
                        // Set the default pattern if suggestion failed
                        LogDataStore.CurrentPatternDefinition = LogDataStore.DefaultPatternDefinition;
                        Console.WriteLine($"Pattern suggestion failed: {response.Error}");
                    }
                    else
                    {
                        // Store the successfully suggested pattern
                        LogDataStore.CurrentPatternDefinition = response.Data;
                    }
                    return null; // Return value is irrelevant for this command
                }

                return null;
            }
        }


        /// <summary>
        /// Prompts the user to select a log file, then runs the pattern suggestion logic.
        /// </summary>
        private async void LoadFileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Log Files (*.log, *.txt)|*.log;*.txt|All files (*.*)|*.*";
            openFileDialog.Title = "Select a Log File for Analysis";

            bool? result = openFileDialog.ShowDialog();

            if (result == true)
            {
                // 1. Store the selected file path temporarily AND save the original path
                LogDataStore.SelectedFileForParsing = openFileDialog.FileName;
                LogDataStore.OriginalFilePath = openFileDialog.FileName;

                // 2. Read the first non-empty line of the file (used only for preview)
                LogDataStore.FirstLogLine = GetFirstLogLine(openFileDialog.FileName);

                // --- NEW DYNAMIC PATTERN SUGGESTION (Robust Check) ---
                LoadFileButton.IsEnabled = false; // Disable button during suggestion

                // Run the Python suggester command, passing the full file path for robust checking
                await Task.Run(() => RunPythonParser("suggest_robust_pattern", openFileDialog.FileName, null, null));

                LoadFileButton.IsEnabled = true; // Re-enable button
                // --- END NEW DYNAMIC PATTERN SUGGESTION ---


                // 3. Test the current active pattern (the suggested one) against the first line
                TestParsingPatternOnFirstLine();

                // 4. Set initial status text
                LogDataStore.CurrentFilePath = $"File Selected: {Path.GetFileName(openFileDialog.FileName)}";

                // 5. Update the INPC view model properties.
                PreviewModel.UpdateFromStore();
            }
            // Ensure view is refreshed to show the updated status or pattern choice
            RefreshView();
        }

        /// <summary>
        /// MODIFIED: Updates the summary statistics display using the dynamic CountItem collection.
        /// (This method now just forces the UI to re-render the bound data, as the calculation 
        /// is handled in LogDataStore.CalculateSummaryCounts()).
        /// </summary>
        private void UpdateSummaryStatistics(bool clear = false)
        {
            // The SummaryCounts collection is bound directly in XAML.
            // No action needed here other than relying on binding.
        }

        // --- INotifyPropertyChanged Implementation ---
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}