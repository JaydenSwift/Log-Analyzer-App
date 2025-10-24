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

namespace Log_Analyzer_App
{
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
    }

    /// <summary>
    /// Interaction logic for LogAnalyzerView.xaml
    /// This is the primary view for displaying log data.
    /// </summary>
    public partial class LogAnalyzerView : UserControl
    {
        // Reference the static LogEntries collection directly
        private ObservableCollection<LogEntry> _logEntries = LogDataStore.LogEntries;

        // Regex pattern to extract Timestamp, Level, and Message
        private readonly Regex _logPattern = new Regex(@"^\[(.*?)\]\s*(INFO|WARN|ERROR):\s*(.*)$", RegexOptions.Compiled);

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
                PythonStatus.Text = "Status: Log file ready for analysis.";
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
        /// Prompts the user to select a log file, then loads and parses the content.
        /// </summary>
        private void LoadFileButton_Click(object sender, RoutedEventArgs e)
        {
            // 1. Initialize OpenFileDialog
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Log Files (*.log, *.txt)|*.log;*.txt|All files (*.*)|*.*";
            openFileDialog.Title = "Select a Log File for Analysis";

            // 2. Show dialog and get result
            bool? result = openFileDialog.ShowDialog();

            if (result == true)
            {
                // File path selected by the user
                string filePath = openFileDialog.FileName;

                try
                {
                    // Clear static collection before loading new data
                    _logEntries.Clear();

                    // Read all lines from the file
                    string[] lines = File.ReadAllLines(filePath);

                    // Parse lines and add to the persistent collection
                    foreach (var line in lines)
                    {
                        ParseLogLine(line);
                    }

                    // Store persistent data
                    LogDataStore.CurrentFilePath = $"File Loaded: {filePath} ({_logEntries.Count} lines)";

                    // Update UI elements after loading
                    RefreshView();
                }
                catch (Exception ex)
                {
                    LogDataStore.CurrentFilePath = $"Error reading file: {ex.Message}";
                    RefreshView();

                    // Use a custom message box instead of MessageBox.Show if running in an environment without native dialogs.
                    // For standard WPF, MessageBox.Show is acceptable here.
                    MessageBox.Show($"An error occurred while reading the file: {ex.Message}", "Processing Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                // User cancelled the dialog, ensure view is refreshed if no file was loaded prior
                RefreshView();
            }
        }

        /// <summary>
        /// Parses a single log line using a regular expression and adds it to the collection.
        /// </summary>
        /// <param name="line">The single line of text from the log file.</param>
        private void ParseLogLine(string line)
        {
            var match = _logPattern.Match(line);

            if (match.Success)
            {
                // Add to the persistent collection (_logEntries is LogDataStore.LogEntries)
                _logEntries.Add(new LogEntry
                {
                    Timestamp = match.Groups[1].Value.Trim(),
                    Level = match.Groups[2].Value.Trim(),
                    Message = match.Groups[3].Value.Trim()
                });
            }
            // Ignore lines that do not match the expected pattern
        }

        /// <summary>
        /// Calculates and displays the count for each log level in the Summary Statistics section.
        /// </summary>
        private void UpdateSummaryStatistics(bool clear = false)
        {
            if (clear)
            {
                InfoCountText.Text = "0";
                WarnCountText.Text = "0";
                ErrorCountText.Text = "0";
                return;
            }

            // Calculate counts
            var counts = _logEntries
                .GroupBy(e => e.Level)
                .ToDictionary(g => g.Key, g => g.Count());

            // Set the TextBlock content. If a key is missing, default to 0.
            InfoCountText.Text = counts.ContainsKey("INFO") ? counts["INFO"].ToString("N0") : "0";
            WarnCountText.Text = counts.ContainsKey("WARN") ? counts["WARN"].ToString("N0") : "0";
            ErrorCountText.Text = counts.ContainsKey("ERROR") ? counts["ERROR"].ToString("N0") : "0";
        }
    }
}
