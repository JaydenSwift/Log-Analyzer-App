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

namespace Log_Analyzer_App
{
    // --- New Model for Storing Pattern and Dynamic Fields ---
    public class LogPatternDefinition
    {
        public string Pattern { get; set; }
        public string Description { get; set; }
        // The list of user-defined column names corresponding to regex capture groups (Group 1, 2, 3...)
        public List<string> FieldNames { get; set; } = new List<string>();
    }

    // --- Model Class for Python JSON output ---
    public class PythonParserResponse
    {
        public bool Success { get; set; }
        // Data is now a list of LogEntry objects (which contain a dynamic dictionary)
        public List<LogEntry> Data { get; set; }
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

        // NEW: Centralized pattern definition store
        public static LogPatternDefinition CurrentPatternDefinition { get; set; }

        // Default pattern definition
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

        // New persistent statistics properties for charts/summary
        public static Dictionary<string, double> SummaryCounts { get; } = new Dictionary<string, double>
        {
            { "INFO", 0 },
            { "WARN", 0 },
            { "ERROR", 0 }
        };

        static LogDataStore()
        {
            // Initialize the current pattern with the default one
            CurrentPatternDefinition = DefaultPatternDefinition;
        }

        /// <summary>
        /// Calculates the count for each log level and stores it in SummaryCounts.
        /// </summary>
        public static void CalculateSummaryCounts()
        {
            // Reset counts
            SummaryCounts["INFO"] = 0;
            SummaryCounts["WARN"] = 0;
            SummaryCounts["ERROR"] = 0;

            // Use the Level property of LogEntry, which is derived from the Fields dictionary
            if (LogEntries.Any())
            {
                // Group by Level and count
                var counts = LogEntries
                    // Check if Level is not null/empty before grouping
                    .Where(e => !string.IsNullOrEmpty(e.Level) && e.Level != "N/A")
                    .GroupBy(e => e.Level.ToUpper()) // Group by uppercase level for robustness
                    .ToDictionary(g => g.Key, g => (double)g.Count());

                // Update SummaryCounts dictionary safely
                if (counts.ContainsKey("INFO")) SummaryCounts["INFO"] = counts["INFO"];
                if (counts.ContainsKey("WARN")) SummaryCounts["WARN"] = counts["WARN"];
                if (counts.ContainsKey("ERROR")) SummaryCounts["ERROR"] = counts["ERROR"];
            }
        }
    }

    /// <summary>
    /// Interaction logic for LogAnalyzerView.xaml
    /// </summary>
    public partial class LogAnalyzerView : UserControl
    {
        private ObservableCollection<LogEntry> _logEntries = LogDataStore.LogEntries;

        // FIX: Non-static class to hold preview binding data and implement INotifyPropertyChanged
        public class LogPreviewModel : INotifyPropertyChanged
        {
            private string _firstLogLine;
            private LogEntry _parsedFirstLine;

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
                ParsedFirstLine = new LogEntry
                {
                    FieldOrder = fieldOrder ?? LogDataStore.CurrentPatternDefinition.FieldNames
                };

                // The TestParsingPatternOnFirstLine() method now correctly populates
                // the Fields dictionary in LogDataStore.ParsedFirstLine.
                // We copy the results over to the INPC instance here for binding.
                ParsedFirstLine.Fields.Clear();
                foreach (var kvp in LogDataStore.ParsedFirstLine.Fields)
                {
                    ParsedFirstLine.Fields.Add(kvp.Key, kvp.Value);
                }

                // Manually fire property change notification
                OnPropertyChanged(nameof(ParsedFirstLine));
            }
        }

        public LogPreviewModel PreviewModel { get; set; } = new LogPreviewModel();


        public LogAnalyzerView()
        {
            InitializeComponent();

            this.DataContext = this;

            // Bind the static ObservableCollection to the DataGrid's ItemsSource
            LogDataGrid.ItemsSource = _logEntries;

            // NEW: Subscribe to the AutoGeneratingColumn event to customize dynamic column creation
            LogDataGrid.AutoGeneratingColumn += LogDataGrid_AutoGeneratingColumn;

            // Update UI with persistent data on initialization
            RefreshView();

            ShowParserStatus(false);
        }

        /// <summary>
        /// Handles the DataGrid's AutoGeneratingColumn event to control which columns are shown 
        /// by canceling internal properties.
        /// </summary>
        private void LogDataGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            string header = e.Column.Header.ToString();

            // Explicitly cancel internal/derived properties that are now hidden via [Browsable(false)] in LogEntry.cs
            // We cancel here because DataGrid.AutoGenerateColumns=True is still set in XAML
            if (header == nameof(LogEntry.Fields) ||
                header == nameof(LogEntry.FieldOrder) ||
                header == nameof(LogEntry.Timestamp) ||
                header == nameof(LogEntry.Level) ||
                header == nameof(LogEntry.Message))
            {
                e.Cancel = true;
                return;
            }
        }

        /// <summary>
        /// NEW: Manually creates and configures the DataGrid columns based on the user's current pattern definition.
        /// </summary>
        private void SetupDynamicColumns()
        {
            // Clear all existing columns before setting up the new ones
            LogDataGrid.Columns.Clear();

            List<string> fieldNames = LogDataStore.CurrentPatternDefinition.FieldNames;

            // Check if there are field names to display
            if (!fieldNames.Any())
            {
                // Add a single "Message" column if no fields are defined (fallback/error state)
                fieldNames = new List<string> { "Message" };
            }

            // Iterate through the user-defined field names in their correct order
            foreach (string fieldName in fieldNames)
            {
                // Create a new TextColumn
                var column = new DataGridTextColumn
                {
                    Header = fieldName,
                    // Give Timestamp/Level a fixed width and Message a star width for better default appearance
                    Width = (fieldName == "Timestamp" || fieldName == "Level")
                        ? DataGridLength.Auto
                        : new DataGridLength(1, DataGridLengthUnitType.Star),
                    IsReadOnly = true,
                    // Set the binding path to look into the LogEntry's Fields dictionary
                    Binding = new Binding($"Fields[{fieldName}]")
                };

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

            if (_logEntries.Any())
            {
                // File is loaded, update status and counts
                PythonStatus.Text = $"Status: Log file ready for analysis. ({_logEntries.Count} entries)";
                UpdateSummaryStatistics();
            }
            else
            {
                // No file loaded
                PythonStatus.Text = "Status: Waiting for log file...";
                UpdateSummaryStatistics(true); // Clear counts
            }

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
                PythonStatus.Visibility = Visibility.Visible;
                ParsingChoicePanel.Visibility = Visibility.Collapsed;
            }
            // If a file is selected and we need a decision (show choice panel)
            else if (LogDataStore.SelectedFileForParsing != null)
            {
                PythonStatus.Visibility = Visibility.Visible;
                ParsingChoicePanel.Visibility = Visibility.Visible;
            }
            // If no file is selected/loaded, or parsing is complete and successful
            else
            {
                PythonStatus.Visibility = Visibility.Visible;
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

        // NEW: Handles the click to use the suggested pattern and start parsing
        private void UseDefaultPattern_Click(object sender, RoutedEventArgs e)
        {
            // Ensure the default pattern is active
            LogDataStore.CurrentPatternDefinition = LogDataStore.DefaultPatternDefinition;

            // Start the parsing process
            StartParsingWithPattern();
        }

        /// <summary>
        /// UPDATED: Handles the click to configure a custom pattern by opening the builder modal.
        /// </summary>
        private void CustomPattern_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(LogDataStore.SelectedFileForParsing) || string.IsNullOrWhiteSpace(LogDataStore.FirstLogLine))
            {
                MessageBox.Show("Please load a log file first to provide a line sample for the Regex Builder.", "Missing Log File", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Pass the current active pattern definition to pre-fill the modal
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

                // 3. Start parsing immediately with the custom pattern
                StartParsingWithPattern();
            }
        }

        /// <summary>
        /// Encapsulates the core parsing logic, triggered after the user confirms the pattern.
        /// </summary>
        private async void StartParsingWithPattern()
        {
            string filePath = LogDataStore.SelectedFileForParsing;
            LogPatternDefinition definition = LogDataStore.CurrentPatternDefinition;

            // Safety check
            if (string.IsNullOrWhiteSpace(filePath)) return;

            // 1. Update UI to show processing is starting
            LogDataStore.CurrentFilePath = $"Processing file: {Path.GetFileName(filePath)}";
            ShowParserStatus(true); // Show loading spinner/status text, hide pattern choice
            PythonStatus.Text = $"Status: Running Python parser with pattern: {definition.Pattern}... Please wait.";
            FilePathTextBlock.Text = LogDataStore.CurrentFilePath; // Update display
            LoadFileButton.IsEnabled = false; // Disable button during processing

            try
            {
                // 2. Execute Python script and get the list of log entries
                // Pass both the regex pattern and the field names (as JSON string)
                List<LogEntry> logEntries = await Task.Run(() => RunPythonParser(filePath, definition.Pattern, definition.FieldNames));

                // 3. Clear static collection and replace with new data
                _logEntries.Clear();

                // --- NEW COLUMN LOGIC ---
                SetupDynamicColumns();
                // -------------------------

                foreach (var entry in logEntries)
                {
                    // CRITICAL: Ensure the FieldOrder is set on every entry for the derived properties to work
                    entry.FieldOrder = definition.FieldNames;
                    _logEntries.Add(entry);
                }

                // 4. Update persistent data and calculate counts
                LogDataStore.CalculateSummaryCounts();
                LogDataStore.CurrentFilePath = $"File Loaded: {Path.GetFileName(filePath)} ({_logEntries.Count} lines) using pattern: {definition.Description}";

                // 5. Final UI update
                PreviewModel.UpdateFromStore();
                RefreshView();
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

                // Display error to user
                MessageBox.Show($"An error occurred during analysis: {ex.Message}", "Python Processing Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LogDataStore.SelectedFileForParsing = null; // Clear the temporary path
                LoadFileButton.IsEnabled = true; // Re-enable Load button
            }
        }


        /// <summary>
        /// Prompts the user to select a log file, then sets up the parsing configuration step.
        /// </summary>
        private void LoadFileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Log Files (*.log, *.txt)|*.log;*.txt|All files (*.*)|*.*";
            openFileDialog.Title = "Select a Log File for Analysis";

            bool? result = openFileDialog.ShowDialog();

            if (result == true)
            {
                // 1. Store the selected file path temporarily
                LogDataStore.SelectedFileForParsing = openFileDialog.FileName;

                // 2. Read the first non-empty line of the file
                LogDataStore.FirstLogLine = GetFirstLogLine(openFileDialog.FileName);

                // 3. Test the current active pattern against the first line
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
        /// Executes the external Python parser script and returns a list of LogEntry objects.
        /// </summary>
        /// <param name="filePath">The path to the log file to analyze.</param>
        /// <param name="logPattern">The custom regex pattern to use for parsing.</param>
        /// <param name="fieldNames">The user-defined names for the capture groups.</param>
        /// <returns>A List of LogEntry objects.</returns>
        private List<LogEntry> RunPythonParser(string filePath, string logPattern, List<string> fieldNames)
        {
            string pythonExecutable = "python";
            string scriptPath = "log_parser.py";

            // Serialize field names list to a JSON string for passing to Python
            string fieldNamesJson = JsonSerializer.Serialize(fieldNames);

            // Build arguments: script path, file path, regex pattern, and field names JSON
            // CRITICAL: Escape both the regex pattern and the field names JSON for the command line.
            string escapedLogPattern = logPattern.Replace("\"", "\\\"");
            string escapedFieldNamesJson = fieldNamesJson.Replace("\"", "\\\"");

            string arguments = $"{scriptPath} \"{filePath}\" \"{escapedLogPattern}\" \"{escapedFieldNamesJson}\"";

            using (Process process = new Process())
            {
                process.StartInfo.FileName = pythonExecutable;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true; // Don't show the Python console window

                process.Start();

                string jsonOutput = process.StandardOutput.ReadToEnd();
                string errorOutput = process.StandardError.ReadToEnd();

                process.WaitForExit();

                if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(jsonOutput))
                {
                    throw new Exception($"Python process failed with exit code {process.ExitCode}. Error: {errorOutput}");
                }

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
        }

        /// <summary>
        /// Calculates and displays the count for each log level in the Summary Statistics section.
        /// </summary>
        private void UpdateSummaryStatistics(bool clear = false)
        {
            if (clear || !LogDataStore.LogEntries.Any())
            {
                InfoCountText.Text = "0";
                WarnCountText.Text = "0";
                ErrorCountText.Text = "0";
                return;
            }

            InfoCountText.Text = LogDataStore.SummaryCounts["INFO"].ToString("N0");
            WarnCountText.Text = LogDataStore.SummaryCounts["WARN"].ToString("N0");
            ErrorCountText.Text = LogDataStore.SummaryCounts["ERROR"].ToString("N0");
        }
    }
}
