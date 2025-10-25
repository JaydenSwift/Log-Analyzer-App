using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Text.RegularExpressions;
// Add using for OpenFileDialog
using Microsoft.Win32;
// NEW: For Python process execution and JSON handling
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
// NEW: For forcing UI updates on static bindings
using System.Windows.Data;
using System.Windows.Input;

namespace Log_Analyzer_App
{
    // --- Model Class for Python JSON output ---
    // This structure holds the entire JSON response from the Python script.
    public class PythonParserResponse
    {
        public bool Success { get; set; }
        public List<LogEntry> Data { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// Static class to hold the application's persistent log data.
    /// Since the views (UserControls) are recreated upon navigation, this global 
    /// persistence layer ensures data survives tab switches.
    /// </summary>
    public static class LogDataStore
    {
        // Persistent collection of log entries
        public static ObservableCollection<LogEntry> LogEntries { get; } = new ObservableCollection<LogEntry>();
        // Persistent file path
        public static string CurrentFilePath { get; set; } = "No log file loaded.";
        // New: Persistent path for a file that is selected but not yet parsed (to handle the new feature)
        public static string SelectedFileForParsing { get; set; }

        // New: Default log pattern and the currently active pattern
        public const string DefaultLogPattern = @"^\[(.*?)\]\s*(INFO|WARN|ERROR):\s*(.*)$";
        public static string CurrentLogPattern { get; set; } = DefaultLogPattern;
        public static string CurrentLogPatternDescription { get; set; } = "Default Pattern: Captures [Timestamp], Level (INFO|WARN|ERROR), and Message.";

        // NEW: Stores the first non-empty line of the selected file for preview
        // CRITICAL FIX: Must be public for XAML binding
        public static string FirstLogLine { get; set; } = string.Empty;

        // NEW: Stores the result of parsing the first line with the current pattern
        // CRITICAL FIX: Must be public for XAML binding
        public static LogEntry ParsedFirstLine { get; set; } = new LogEntry
        {
            Timestamp = "N/A",
            Level = "N/A",
            Message = "No successful match on first line."
        };


        // --- New persistent statistics properties for charts/summary ---
        // Stored as Dictionary<string, double> to be easily consumable by LiveCharts
        public static Dictionary<string, double> SummaryCounts { get; } = new Dictionary<string, double>
        {
            { "INFO", 0 },
            { "WARN", 0 },
            { "ERROR", 0 }
        };

        /// <summary>
        /// Calculates the count for each log level and stores it in SummaryCounts.
        /// This method should be called immediately after LogEntries is populated.
        /// </summary>
        public static void CalculateSummaryCounts()
        {
            // Reset counts
            SummaryCounts["INFO"] = 0;
            SummaryCounts["WARN"] = 0;
            SummaryCounts["ERROR"] = 0;

            if (LogEntries.Any())
            {
                // Group by Level and count
                var counts = LogEntries
                    // Check if Level is not null/empty before grouping
                    .Where(e => !string.IsNullOrEmpty(e.Level))
                    .GroupBy(e => e.Level.ToUpper()) // Group by uppercase level for robustness
                    .ToDictionary(g => g.Key, g => (double)g.Count());

                // Update SummaryCounts dictionary safely
                if (counts.ContainsKey("INFO")) SummaryCounts["INFO"] = counts.ContainsKey("INFO") ? counts["INFO"] : 0;
                if (counts.ContainsKey("WARN")) SummaryCounts["WARN"] = counts.ContainsKey("WARN") ? counts["WARN"] : 0;
                if (counts.ContainsKey("ERROR")) SummaryCounts["ERROR"] = counts.ContainsKey("ERROR") ? counts["ERROR"] : 0;
            }
        }
    }

    /// <summary>
    /// Interaction logic for LogAnalyzerView.xaml
    /// This is the primary view for displaying log data.
    /// </summary>
    public partial class LogAnalyzerView : UserControl
    {
        // Reference the static LogEntries collection directly
        private ObservableCollection<LogEntry> _logEntries = LogDataStore.LogEntries;

        public LogAnalyzerView()
        {
            InitializeComponent();

            // CRITICAL FIX: Set the DataContext to the type of the static class
            // This allows us to use simple Path bindings like {Binding FirstLogLine} 
            // instead of complex, error-prone x:Static bindings.
            this.DataContext = typeof(LogDataStore);

            // Bind the static ObservableCollection to the DataGrid's ItemsSource
            LogDataGrid.ItemsSource = _logEntries;

            // Update UI with persistent data on initialization
            RefreshView();

            // Initialize the visibility state for the new feature
            ShowParserStatus(false);
        }

        /// <summary>
        /// Updates the view's display elements (FilePath, Status, Counts) 
        /// using the persistent data in LogDataStore.
        /// </summary>
        private void RefreshView()
        {
            FilePathTextBlock.Text = LogDataStore.CurrentFilePath;

            // Update the pattern display elements (only visible when a file is selected)
            PatternDisplayTextBlock.Text = LogDataStore.CurrentLogPattern;
            PatternDescriptionTextBlock.Text = LogDataStore.CurrentLogPatternDescription;

            if (_logEntries.Any())
            {
                // File is loaded, update status and counts
                PythonStatus.Text = $"Status: Log file ready for analysis. ({_logEntries.Count} entries)";
                UpdateSummaryStatistics();
            }
            else if (LogDataStore.SelectedFileForParsing != null)
            {
                // A file has been selected but not yet parsed (waiting for confirmation)
                PythonStatus.Text = $"Status: File selected: {Path.GetFileName(LogDataStore.SelectedFileForParsing)}. Confirm parsing pattern.";
                UpdateSummaryStatistics(true);
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
        /// New helper to control visibility of the parsing prompt/status UI
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
                RunAnalysisButton.Visibility = Visibility.Collapsed; // Hide during processing
            }
            // If a file is selected and we need a decision (show choice panel)
            else if (LogDataStore.SelectedFileForParsing != null)
            {
                PythonStatus.Visibility = Visibility.Visible;
                ParsingChoicePanel.Visibility = Visibility.Visible;
                RunAnalysisButton.Visibility = Visibility.Collapsed; // Hide the 'Run Analysis' placeholder
            }
            // If no file is selected/loaded, or parsing is complete and successful
            else
            {
                PythonStatus.Visibility = Visibility.Visible;
                ParsingChoicePanel.Visibility = Visibility.Collapsed;
                // If log entries exist, show the Run Analysis button for future filtering features
                RunAnalysisButton.Visibility = _logEntries.Any() ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Attempts to read the first non-empty line of the file for preview purposes.
        /// </summary>
        /// <param name="filePath">The path to the selected file.</param>
        /// <returns>The first non-empty line, or an empty string on failure.</returns>
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
                // In a real application, you might want better logging, but for now, 
                // we'll just return empty and log to console.
                Console.WriteLine($"Error reading first line: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Tests the currently suggested pattern against the first line of the log.
        /// This is done purely in C# to give immediate feedback.
        /// </summary>
        private void TestParsingPatternOnFirstLine()
        {
            LogDataStore.ParsedFirstLine = new LogEntry
            {
                Timestamp = "N/A",
                Level = "N/A",
                Message = "No successful match on first line."
            };

            if (string.IsNullOrWhiteSpace(LogDataStore.FirstLogLine) || string.IsNullOrWhiteSpace(LogDataStore.CurrentLogPattern))
            {
                // Nothing to test
                return;
            }

            try
            {
                var regex = new Regex(LogDataStore.CurrentLogPattern);
                Match match = regex.Match(LogDataStore.FirstLogLine);

                if (match.Success && match.Groups.Count >= 4) // Group 0 is the full match, 1, 2, 3 are the desired captures
                {
                    LogDataStore.ParsedFirstLine.Timestamp = match.Groups[1].Value.Trim();
                    LogDataStore.ParsedFirstLine.Level = match.Groups[2].Value.Trim();
                    LogDataStore.ParsedFirstLine.Message = match.Groups[3].Value.Trim();
                }
            }
            catch (ArgumentException)
            {
                // Invalid regex pattern
                LogDataStore.ParsedFirstLine.Message = "Error: Invalid Regex Pattern.";
            }
            catch (Exception)
            {
                // Other unexpected error during match
                LogDataStore.ParsedFirstLine.Message = "Error: Matching failed.";
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
            LogDataStore.CurrentLogPattern = LogDataStore.DefaultLogPattern;
            LogDataStore.CurrentLogPatternDescription = "Default Pattern: Captures [Timestamp], Level (INFO|WARN|ERROR), and Message.";

            // Start the parsing process
            StartParsingWithPattern();
        }

        // NEW: Handles the click to use a custom pattern (placeholder)
        private void CustomPattern_Click(object sender, RoutedEventArgs e)
        {
            // NOTE: Custom Pattern logic is a placeholder as requested.
            MessageBox.Show(
                "Custom Pattern Configuration is not yet implemented. Please click 'Use Default Pattern' to continue.",
                "Feature Placeholder",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        /// <summary>
        /// Encapsulates the core parsing logic, triggered after the user confirms the pattern.
        /// </summary>
        private async void StartParsingWithPattern()
        {
            string filePath = LogDataStore.SelectedFileForParsing;
            string pattern = LogDataStore.CurrentLogPattern;

            // Safety check
            if (string.IsNullOrWhiteSpace(filePath)) return;

            // 1. Update UI to show processing is starting
            LogDataStore.CurrentFilePath = $"Processing file: {Path.GetFileName(filePath)}";
            ShowParserStatus(true); // Show loading spinner/status text, hide pattern choice
            PythonStatus.Text = $"Status: Running Python parser with pattern: {pattern}... Please wait.";
            FilePathTextBlock.Text = LogDataStore.CurrentFilePath; // Update display
            LoadFileButton.IsEnabled = false; // Disable button during processing

            try
            {
                // 2. Execute Python script and get the list of log entries
                // Pass the current active pattern to the parser
                List<LogEntry> logEntries = await Task.Run(() => RunPythonParser(filePath, pattern));

                // 3. Clear static collection and replace with new data
                _logEntries.Clear();
                foreach (var entry in logEntries)
                {
                    _logEntries.Add(entry);
                }

                // 4. Update persistent data and calculate counts
                LogDataStore.CalculateSummaryCounts();
                LogDataStore.CurrentFilePath = $"File Loaded: {Path.GetFileName(filePath)} ({_logEntries.Count} lines) using pattern: {pattern}";

                // 5. Final UI update
                RefreshView();
            }
            catch (Exception ex)
            {
                // Handle errors during execution or JSON deserialization
                LogDataStore.CurrentFilePath = $"Error during analysis: {ex.Message}";
                _logEntries.Clear(); // Clear any corrupted data
                LogDataStore.CalculateSummaryCounts(); // Reset counts
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

                // 3. Update UI to prompt for parsing pattern choice
                // Display the default pattern suggestion.
                LogDataStore.CurrentLogPattern = LogDataStore.DefaultLogPattern; // Reset to default for suggestion
                LogDataStore.CurrentLogPatternDescription = "Default Pattern: Captures [Timestamp], Level (INFO|WARN|ERROR), and Message.";

                // 4. Test the default pattern against the first line
                TestParsingPatternOnFirstLine();

                // 5. Set initial status text
                LogDataStore.CurrentFilePath = $"File Selected: {Path.GetFileName(openFileDialog.FileName)}";

                // CRITICAL FIX: Instead of individual FindName/UpdateTarget calls (which often fail for static bindings 
                // and newly created elements), we force a visual update on the main panel itself.
                ParsingChoicePanel.UpdateLayout();
                ParsingChoicePanel.InvalidateVisual();
            }
            // Ensure view is refreshed to show the updated status or pattern choice
            RefreshView();
        }

        /// <summary>
        /// Executes the external Python parser script and returns a list of LogEntry objects.
        /// </summary>
        /// <param name="filePath">The path to the log file to analyze.</param>
        /// <param name="logPattern">The custom regex pattern to use for parsing.</param>
        /// <returns>A List of LogEntry objects.</returns>
        /// <exception cref="Exception">Throws an exception if the Python process fails or returns an error.</exception>
        private List<LogEntry> RunPythonParser(string filePath, string logPattern)
        {
            // The Python executable path. We assume 'python' is in the system PATH.
            string pythonExecutable = "python";
            // The path to the script file
            string scriptPath = "log_parser.py";

            // Build arguments: script path, file path (quoted), and the REGEX PATTERN (quoted)
            // CRITICAL: The regex pattern is sent as a command line argument.
            // We use ' escape to ensure the regex string is passed correctly within quotes.
            string escapedLogPattern = logPattern.Replace("\"", "\\\"");
            string arguments = $"{scriptPath} \"{filePath}\" \"{escapedLogPattern}\"";


            using (Process process = new Process())
            {
                process.StartInfo.FileName = pythonExecutable;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true; // Don't show the Python console window

                process.Start();

                // Read the standard output (which contains the JSON result)
                string jsonOutput = process.StandardOutput.ReadToEnd();
                // Read any errors from standard error
                string errorOutput = process.StandardError.ReadToEnd();

                process.WaitForExit();

                if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(jsonOutput))
                {
                    // If the process failed and returned an error on stderr (e.g., Python not found)
                    throw new Exception($"Python process failed with exit code {process.ExitCode}. Error: {errorOutput}");
                }

                // Try to deserialize the JSON output
                PythonParserResponse response;
                try
                {
                    // Deserialize the entire response object (which includes success/error fields)
                    response = JsonSerializer.Deserialize<PythonParserResponse>(jsonOutput, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true // Allow flexible property matching (e.g., 'Timestamp' vs 'timestamp')
                    });
                }
                catch (JsonException ex)
                {
                    throw new Exception($"Failed to parse JSON output from Python. Raw Output: '{jsonOutput}'. JSON Error: {ex.Message}");
                }

                // Check the 'success' flag within the JSON response
                if (!response.Success)
                {
                    throw new Exception($"Log parsing failed within Python script. Reason: {response.Error}");
                }

                return response.Data ?? new List<LogEntry>(); // Return parsed data or empty list
            }
        }

        /// <summary>
        /// Calculates and displays the count for each log level in the Summary Statistics section.
        /// Now uses the persistent counts stored in LogDataStore.
        /// </summary>
        private void UpdateSummaryStatistics(bool clear = false)
        {
            // If cleared or no entries, reset display.
            if (clear || !LogDataStore.LogEntries.Any())
            {
                InfoCountText.Text = "0";
                WarnCountText.Text = "0";
                ErrorCountText.Text = "0";
                return;
            }

            // Retrieve and display counts from the central data store
            InfoCountText.Text = LogDataStore.SummaryCounts["INFO"].ToString("N0");
            WarnCountText.Text = LogDataStore.SummaryCounts["WARN"].ToString("N0");
            ErrorCountText.Text = LogDataStore.SummaryCounts["ERROR"].ToString("N0");
        }
    }
}
