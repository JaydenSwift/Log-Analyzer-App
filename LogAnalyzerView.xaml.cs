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
                if (counts.ContainsKey("INFO")) SummaryCounts["INFO"] = counts["INFO"];
                if (counts.ContainsKey("WARN")) SummaryCounts["WARN"] = counts["WARN"];
                if (counts.ContainsKey("ERROR")) SummaryCounts["ERROR"] = counts["ERROR"];
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

        // REMOVED: private readonly Regex _logPattern and ParseLogLine are no longer needed.

        public LogAnalyzerView()
        {
            InitializeComponent();

            // Bind the static ObservableCollection to the DataGrid's ItemsSource
            LogDataGrid.ItemsSource = _logEntries;

            // Update UI with persistent data on initialization
            RefreshView();
        }

        /// <summary>
        /// Updates the view's display elements (FilePath, Status, Counts) 
        /// using the persistent data in LogDataStore.
        /// </summary>
        private void RefreshView()
        {
            FilePathTextBlock.Text = LogDataStore.CurrentFilePath;

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
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Placeholder for selection logic
        }

        /// <summary>
        /// Prompts the user to select a log file, then loads and parses the content
        /// using the external Python script.
        /// </summary>
        private async void LoadFileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Log Files (*.log, *.txt)|*.log;*.txt|All files (*.*)|*.*";
            openFileDialog.Title = "Select a Log File for Analysis";

            bool? result = openFileDialog.ShowDialog();

            if (result == true)
            {
                string filePath = openFileDialog.FileName;

                // 1. Update UI to show processing is starting
                LogDataStore.CurrentFilePath = "Processing file via Python parser...";
                PythonStatus.Text = "Status: Running Python parser... Please wait.";
                RefreshView();
                LoadFileButton.IsEnabled = false; // Disable button during processing

                try
                {
                    // 2. Execute Python script and get the list of log entries
                    List<LogEntry> logEntries = await Task.Run(() => RunPythonParser(filePath));

                    // 3. Clear static collection and replace with new data
                    _logEntries.Clear();
                    foreach (var entry in logEntries)
                    {
                        _logEntries.Add(entry);
                    }

                    // 4. Update persistent data and calculate counts
                    LogDataStore.CalculateSummaryCounts();
                    LogDataStore.CurrentFilePath = $"File Loaded: {filePath} ({_logEntries.Count} lines)";

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

                    // Display error to user
                    MessageBox.Show($"An error occurred during analysis: {ex.Message}", "Python Processing Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    LoadFileButton.IsEnabled = true; // Re-enable button
                }
            }
            else
            {
                RefreshView();
            }
        }

        /// <summary>
        /// Executes the external Python parser script and returns a list of LogEntry objects.
        /// </summary>
        /// <param name="filePath">The path to the log file to analyze.</param>
        /// <returns>A List of LogEntry objects.</returns>
        /// <exception cref="Exception">Throws an exception if the Python process fails or returns an error.</exception>
        private List<LogEntry> RunPythonParser(string filePath)
        {
            // The Python executable path. We assume 'python' is in the system PATH.
            string pythonExecutable = "python";
            // The path to the script file (assumed to be in the same directory as the application executable)
            // In a production environment, you might need to use a fully qualified path or relative path from CWD.
            string scriptPath = "log_parser.py";

            // Build arguments: script path followed by the file path (quoted to handle spaces)
            string arguments = $"{scriptPath} \"{filePath}\"";

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
