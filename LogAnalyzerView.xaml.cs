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

    // --- Model Class for Python JSON output (Parsing) ---
    public class PythonParserResponse
    {
        public bool Success { get; set; }
        // Data is now a list of LogEntry objects (which contain a dynamic dictionary)
        public List<LogEntry> Data { get; set; }
        public string Error { get; set; }
    }

    // --- NEW Model Class for Python JSON output (Pattern Suggestion) ---
    public class PythonSuggesterResponse
    {
        public bool Success { get; set; }
        // Data is now a single LogPatternDefinition object
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
        public static string OriginalFilePath { get; set; } = null; // NEW: Store original path for parsing reuse

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
                // Get the name of the 'Level' field, which is expected to be the second field (index 1)
                string levelFieldName = LogDataStore.CurrentPatternDefinition.FieldNames.Count > 1
                    ? LogDataStore.CurrentPatternDefinition.FieldNames[1]
                    : "Level";

                // Group by Level and count
                var counts = LogEntries
                    // Safely try to get the Level field's value
                    .Where(e => e.Fields.ContainsKey(levelFieldName))
                    .Select(e => e.Fields[levelFieldName])
                    .Where(levelValue => !string.IsNullOrEmpty(levelValue) && levelValue != "N/A")
                    .GroupBy(levelValue => levelValue.ToUpper()) // Group by uppercase level for robustness
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


        public LogAnalyzerView()
        {
            InitializeComponent();

            this.DataContext = this;

            // Bind the static ObservableCollection to the DataGrid's ItemsSource
            LogDataGrid.ItemsSource = _logEntries;

            // Since we set AutoGenerateColumns=False in XAML, we only need to call SetupDynamicColumns 
            // when data is loaded, not on AutoGeneratingColumn event.

            // Ensure columns are set up on startup if data already exists (e.g., app restart)
            if (_logEntries.Any())
            {
                SetupDynamicColumns();
            }

            // Update UI with persistent data on initialization
            RefreshView();

            ShowParserStatus(false);
        }

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

            // Iterate through the user-defined field names in their correct order
            foreach (string fieldName in fieldNames)
            {
                // Create a new TextColumn
                var column = new DataGridTextColumn
                {
                    Header = fieldName,
                    // Give Timestamp/Level a fixed width and Message a star width for better default appearance
                    // NOTE: Since the column names are dynamic, we use a simple heuristic for size,
                    // or let the width auto-adjust.
                    Width = (fieldName.Contains("Timestamp") || fieldName.Contains("Level") || fieldName.Length < 10)
                        ? DataGridLength.Auto
                        : new DataGridLength(1, DataGridLengthUnitType.Star),
                    IsReadOnly = true,
                    // Set the binding path to look into the LogEntry's Fields dictionary
                    // This uses the dynamic property lookup capability of WPF binding.
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
            else if (LogDataStore.SelectedFileForParsing != null || LogDataStore.OriginalFilePath != null)
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
        /// CRITICAL FIX: Pass isBestEffort=false to enforce strict parsing (user expects all lines to match custom pattern).
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

                // 3. Start parsing immediately with the custom pattern. Strict check is enforced.
                StartParsingWithPattern(isBestEffortParse: false);
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
            PythonStatus.Text = $"Status: Running Python parser with pattern: {definition.Pattern}... Please wait.";
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

                // --- NEW COLUMN LOGIC: MUST BE CALLED BEFORE ADDING ENTRIES ---
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

                // MODIFIED: Only show the error message box if it was NOT a best-effort parse (i.e., custom regex was used).
                // For the default button (best-effort), we fail silently in the UI to prevent interruption.
                if (!isBestEffortParse)
                {
                    MessageBox.Show($"An error occurred during analysis: {ex.Message}", "Python Processing Error (Strict Mode)", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    // For best-effort, just update the status text
                    PythonStatus.Text = $"Status: Failed to parse file fully. Displaying partial results if any. Error: {ex.Message}";
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
            string pythonExecutable = "python";
            string scriptPath = "log_parser.py";
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
                PythonStatus.Text = "Status: Analyzing log format and suggesting pattern (checking first 5 lines)...";

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
