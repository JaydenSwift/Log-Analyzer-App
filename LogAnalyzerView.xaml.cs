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
using System.Globalization; // Needed for DateTime parsing
using System.Text; // Needed for StringBuilder/Encoding

namespace Log_Analyzer_App
{
    // NEW: Enum for time-based grouping in charts
    public enum TimeRangeType
    {
        Hours,
        Days,
        Weeks,
        Months
    }

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

        // MODIFIED: Static properties for LOG GRID FILTER persistence (used by LogAnalyzerView)
        public static DateTime? GridStartDate { get; set; }
        public static DateTime? GridEndDate { get; set; }

        // MODIFIED: Static properties for LOG GRID FILTER time text persistence
        public static string GridStartTimeText { get; set; } = string.Empty;
        public static string GridEndTimeText { get; set; } = string.Empty;

        // NEW: Static property for the selected timestamp column (shared, as it depends on the loaded log file)
        public static string SelectedTimestampField { get; set; }

        // NEW: Centralized pattern definition store
        public static LogPatternDefinition CurrentPatternDefinition { get; set; }

        // NEW: Static property to hold the user-selected path to a custom patterns.json file
        public static string CustomPatternsFilePath { get; set; } = string.Empty;


        // Default pattern definition (now redundant as it's defined in Python, but kept as a fallback)
        public static LogPatternDefinition DefaultPatternDefinition = new LogPatternDefinition
        {
            Pattern = @"^\[(.*?)\]\s*(INFO|WARN|ERROR):\s*(.*)$",
            FieldNames = new List<string> { "Timestamp", "Level", "Message" }
        };

        // NEW: Stores the first non-empty line of the selected file for preview
        public static string FirstLogLine { get; set; } = string.Empty;

        // NEW: Stores the result of parsing the first line with the current pattern
        public static LogEntry ParsedFirstLine { get; set; } = new LogEntry();

        // MODIFIED: ObservableCollection for dynamic summary statistics binding
        // These counts will still be filtered, but based on the Log Grid filter's state.
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
            // Initialize the default timestamp field (if one exists in the default pattern)
            SelectedTimestampField = CurrentPatternDefinition.FieldNames.FirstOrDefault(name => name.Equals("Timestamp", StringComparison.OrdinalIgnoreCase)) ?? null;
        }

        /// <summary>
        /// NEW: Generates a random SolidColorBrush with a relatively high saturation to be visible on white background.
        /// Uses HSL color space for better color distribution and contrast.
        /// </summary>
        private static System.Windows.Media.SolidColorBrush GetRandomBrush()
        {
            // Generate random HSL colors and convert to RGB for better color distribution of vivid colors
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

        // --- NEW: Centralized Date/Time Parsing and Combining Helpers (Made public static) ---

        /// <summary>
        /// Helper to parse log timestamps, trying standard formats first, then the Apache format.
        /// </summary>
        /// <param name="timestampValue">The raw string from the log entry.</param>
        /// <param name="logDateTime">Output parameter for the parsed DateTime.</param>
        /// <returns>True if parsing was successful.</returns>
        public static bool TryParseLogDateTime(string timestampValue, out DateTime logDateTime)
        {
            // 1. Try generic parsing (works for formats like YYYY-MM-DD HH:MM:SS)
            if (DateTime.TryParse(timestampValue, out logDateTime))
            {
                return true;
            }

            // 2. Try the Apache Combined Log Format: [dd/MMM/yyyy:HH:mm:ss zzz]
            // Note: We use CultureInfo.InvariantCulture as the month name (MMM) is English (Oct).
            const string apacheFormat = "dd/MMM/yyyy:HH:mm:ss zzz";
            // We need to strip the wrapping brackets first, as the pattern capture includes them.
            string strippedValue = timestampValue.TrimStart('[').TrimEnd(']');

            if (DateTime.TryParseExact(strippedValue, apacheFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out logDateTime))
            {
                return true;
            }

            // 3. Add any other specific formats here if required later.

            logDateTime = default;
            return false;
        }

        /// <summary>
        /// Combines a nullable date and a time string (HH:MM) into a single nullable DateTime.
        /// If date is null, returns null. If time is blank, it defaults to midnight (00:00:00).
        /// If time is invalid, returns null.
        /// </summary>
        /// <param name="date">The date part.</param>
        /// <param name="timeText">The time string (e.g., "14:30").</param>
        /// <returns>Combined DateTime? or null.</returns>
        public static DateTime? CombineDateTime(DateTime? date, string timeText)
        {
            if (!date.HasValue)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(timeText))
            {
                // NEW BEHAVIOR: If time is blank, default to midnight (00:00:00)
                return date.Value.Date;
            }

            // Use the most flexible TryParseExact for HH:mm and HH:mm:ss
            if (DateTime.TryParseExact(timeText.Trim(), new string[] { "H:m", "HH:mm", "H:m:s", "HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime timeOnly))
            {
                // Combine the date part with the parsed time part
                return date.Value.Date.Add(timeOnly.TimeOfDay);
            }

            // Time text was present but invalid
            return null;
        }

        // --- End Centralized Date/Time Parsing and Combining Helpers ---


        // --- LOG GRID FILTERING (Using Grid-prefixed static properties) ---

        /// <summary>
        /// Filters the master log collection by the currently set log grid date/time and timestamp field.
        /// </summary>
        /// <returns>An IEnumerable<LogEntry> containing the filtered logs.</returns>
        public static IEnumerable<LogEntry> GetLogGridFilteredEntries()
        {
            if (!LogEntries.Any())
            {
                return Enumerable.Empty<LogEntry>();
            }

            // Use Grid-prefixed static filter values
            DateTime? filterStart = CombineDateTime(GridStartDate, GridStartTimeText);
            DateTime? filterEnd = CombineDateTime(GridEndDate, GridEndTimeText);

            // Handle the case where EndDate is set, but EndTime is empty (make range inclusive of the whole day)
            if (GridEndDate.HasValue && string.IsNullOrWhiteSpace(GridEndTimeText) && filterEnd.HasValue)
            {
                filterEnd = GridEndDate.Value.Date.AddDays(1).AddTicks(-1);
            }

            string timestampFieldName = SelectedTimestampField;

            // If no filters are applied, return all entries
            if (!filterStart.HasValue && !filterEnd.HasValue)
            {
                return LogEntries;
            }

            // If a timestamp field is not selected but filters are applied, we return no entries
            if (string.IsNullOrEmpty(timestampFieldName))
            {
                return Enumerable.Empty<LogEntry>();
            }

            return LogEntries.Where(logEntry =>
            {
                // Try to get and parse the timestamp value
                if (logEntry.Fields.TryGetValue(timestampFieldName, out string timestampValue) &&
                    TryParseLogDateTime(timestampValue, out DateTime logDateTime))
                {
                    bool match = true;

                    // Check start boundary
                    if (filterStart.HasValue && logDateTime < filterStart.Value)
                    {
                        match = false;
                    }

                    // Check end boundary (inclusive)
                    if (filterEnd.HasValue && logDateTime > filterEnd.Value)
                    {
                        match = false;
                    }

                    return match;
                }

                // If parsing fails or the field is missing, exclude the log entry 
                // only if filters are actively set
                return false;
            });
        }

        // --- END LOG GRID FILTERING ---


        /// <summary>
        /// NEW: Calculates the count for all unique values in a specific field, returning a new collection.
        /// This is used by the ChartViewer for independent analysis.
        /// </summary>
        /// <param name="statsFieldName">The name of the column/field to group by.</param>
        /// <param name="startDate">The chart's start date filter.</param>
        /// <param name="endDate">The chart's end date filter.</param>
        /// <param name="startTimeText">The chart's start time text.</param>
        /// <param name="endTimeText">The chart's end time text.</param>
        /// <returns>A new ObservableCollection<CountItem> representing the counts.</returns>
        public static ObservableCollection<CountItem> GetDynamicSummaryCounts(string statsFieldName,
            DateTime? startDate = null, DateTime? endDate = null, string startTimeText = null, string endTimeText = null)
        {
            var dynamicCounts = new ObservableCollection<CountItem>();
            string fieldName = statsFieldName;

            if (string.IsNullOrEmpty(fieldName))
            {
                return dynamicCounts;
            }

            // --- Determine the Source Data ---
            IEnumerable<LogEntry> sourceEntries;

            // If the filter arguments are NULL (used by LogAnalyzerView for its SummaryCounts)
            // This is the filter used for the LogAnalyzerView Summary Card.
            if (!startDate.HasValue && !endDate.HasValue && string.IsNullOrEmpty(startTimeText) && string.IsNullOrEmpty(endTimeText))
            {
                // Use the filtered data from the main grid view (which has its own filter applied)
                sourceEntries = GetLogGridFilteredEntries();
            }
            else
            {
                // Use a temporary filter for the ChartViewer's unique filter settings

                DateTime? chartFilterStart = CombineDateTime(startDate, startTimeText);
                DateTime? chartFilterEnd = CombineDateTime(endDate, endTimeText);

                if (endDate.HasValue && string.IsNullOrWhiteSpace(endTimeText) && chartFilterEnd.HasValue)
                {
                    chartFilterEnd = endDate.Value.Date.AddDays(1).AddTicks(-1);
                }

                string timestampFieldName = SelectedTimestampField;

                // If filters are set but no timestamp field, return empty
                if (string.IsNullOrEmpty(timestampFieldName) && (chartFilterStart.HasValue || chartFilterEnd.HasValue))
                {
                    return dynamicCounts;
                }

                // Apply Chart-specific filter
                sourceEntries = LogEntries.Where(logEntry =>
                {
                    // Check if chart filter is enabled
                    if (!chartFilterStart.HasValue && !chartFilterEnd.HasValue) return true;

                    // Apply date filter
                    if (logEntry.Fields.TryGetValue(timestampFieldName, out string timestampValue) &&
                        TryParseLogDateTime(timestampValue, out DateTime logDateTime))
                    {
                        bool match = true;
                        if (chartFilterStart.HasValue && logDateTime < chartFilterStart.Value) match = false;
                        if (chartFilterEnd.HasValue && logDateTime > chartFilterEnd.Value) match = false;
                        return match;
                    }

                    // Fail if field is missing or unparseable and a chart filter is active
                    return false;
                });
            }
            // --- End Source Data Determination ---


            if (!sourceEntries.Any())
            {
                return dynamicCounts;
            }

            // Group by the value of the identified statistics field
            var counts = sourceEntries
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
        /// NEW: Calculates the event count grouped by the specified time range (Hour, Day, Week, Month).
        /// This is used primarily by the ChartViewer for independent analysis.
        /// </summary>
        /// <param name="rangeType">The time range to group by.</param>
        /// <param name="startDate">The chart's start date filter.</param>
        /// <param name="endDate">The chart's end date filter.</param>
        /// <param name="startTimeText">The chart's start time text.</param>
        /// <param name="endTimeText">The chart's end time text.</param>
        /// <returns>A new ObservableCollection<CountItem> representing the time-based counts.</returns>
        public static ObservableCollection<CountItem> GetTimeRangeSummaryCounts(TimeRangeType rangeType,
            DateTime? startDate = null, DateTime? endDate = null, string startTimeText = null, string endTimeText = null)
        {
            var dynamicCounts = new ObservableCollection<CountItem>();

            string timestampFieldName = SelectedTimestampField;

            // If no timestamp field is selected, we cannot perform time analysis.
            if (string.IsNullOrEmpty(timestampFieldName))
            {
                return dynamicCounts;
            }

            // --- Determine the Source Data (identical filtering logic as GetDynamicSummaryCounts) ---
            IEnumerable<LogEntry> sourceEntries;

            DateTime? chartFilterStart = CombineDateTime(startDate, startTimeText);
            DateTime? chartFilterEnd = CombineDateTime(endDate, endTimeText);

            if (endDate.HasValue && string.IsNullOrWhiteSpace(endTimeText) && chartFilterEnd.HasValue)
            {
                chartFilterEnd = endDate.Value.Date.AddDays(1).AddTicks(-1);
            }

            // Apply Chart-specific filter
            sourceEntries = LogEntries.Where(logEntry =>
            {
                // Check if chart filter is enabled
                if (!chartFilterStart.HasValue && !chartFilterEnd.HasValue) return true;

                // Apply date filter
                if (logEntry.Fields.TryGetValue(timestampFieldName, out string timestampValue) &&
                    TryParseLogDateTime(timestampValue, out DateTime logDateTime))
                {
                    bool match = true;
                    if (chartFilterStart.HasValue && logDateTime < chartFilterStart.Value) match = false;
                    if (chartFilterEnd.HasValue && logDateTime > chartFilterEnd.Value) match = false;
                    return match;
                }
                return false;
            });

            if (!sourceEntries.Any())
            {
                return dynamicCounts;
            }

            // --- Grouping by Time Range ---

            // 1. Convert all timestamps in the filtered set into DateTime objects
            var validDates = sourceEntries
                .Where(e => e.Fields.ContainsKey(timestampFieldName))
                .Select(e => e.Fields[timestampFieldName])
                .Where(v => TryParseLogDateTime(v, out DateTime _))
                .Select(v => { TryParseLogDateTime(v, out DateTime dt); return dt; })
                .ToList();

            if (!validDates.Any())
            {
                return dynamicCounts;
            }

            // 2. Group by the appropriate interval and format the key
            var groupedData = validDates
                .GroupBy(dt =>
                {
                    switch (rangeType)
                    {
                        case TimeRangeType.Hours:
                            // Group by year, month, day, and hour (e.g., 2025-10-23 09)
                            return dt.ToString("yyyy-MM-dd HH");
                        case TimeRangeType.Days:
                            // Group by year, month, day (e.g., 2025-10-23)
                            return dt.ToString("yyyy-MM-dd");
                        case TimeRangeType.Weeks:
                            // Group by ISO week (requires CultureInfo for WeekOfYear)
                            // Key format: YYYY-WW (e.g., 2025-43)
                            System.Globalization.Calendar calendar = CultureInfo.InvariantCulture.Calendar;
                            int week = calendar.GetWeekOfYear(dt, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
                            // To ensure unique grouping across years, include the year
                            return dt.Year.ToString() + "-" + week.ToString("D2");
                        case TimeRangeType.Months:
                            // Group by year and month (e.g., 2025-10)
                            return dt.ToString("yyyy-MM");
                        default:
                            return dt.ToString(); // Fallback
                    }
                })
                .OrderBy(g => g.Key) // Order chronologically
                .ToDictionary(g => g.Key, g => (double)g.Count());

            // 3. Format and Populate CountItems (using a consistent color, as this is a single series)
            System.Windows.Media.Brush defaultColor = System.Windows.Media.Brushes.DodgerBlue; // Use a distinct color

            foreach (var kvp in groupedData)
            {
                // The key is the formatted time range string (e.g., "2025-10-23 09", "2025-43")
                // We don't need to reformat the key for display here, the chart X-axis will use it.
                dynamicCounts.Add(new CountItem
                {
                    Key = kvp.Key,
                    Count = kvp.Value,
                    Color = defaultColor
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

            if (string.IsNullOrEmpty(fieldName))
            {
                return;
            }

            // Update the last used field name
            LastStatsFieldName = fieldName;

            // Get dynamic counts for the determined field (which now uses the date filter)
            // CRITICAL: We call the main GetDynamicSummaryCounts *without* date/time arguments, 
            // forcing it to use the shared Log Grid Filter.
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

        // --- Time Range Text Binding Properties (MODIFIED to use Grid-prefixed properties) ---
        private string _startTimeText = LogDataStore.GridStartTimeText;
        public string StartTimeText
        {
            get => _startTimeText;
            set
            {
                if (_startTimeText != value)
                {
                    _startTimeText = value;
                    LogDataStore.GridStartTimeText = value; // Store in Grid-prefixed static property
                    OnPropertyChanged(nameof(StartTimeText));
                    // CRITICAL: Refresh filter when time text changes
                    LogCollectionViewSource.View.Refresh();
                }
            }
        }

        private string _endTimeText = LogDataStore.GridEndTimeText;
        public string EndTimeText
        {
            get => _endTimeText;
            set
            {
                if (_endTimeText != value)
                {
                    _endTimeText = value;
                    LogDataStore.GridEndTimeText = value; // Store in Grid-prefixed static property
                    OnPropertyChanged(nameof(EndTimeText));
                    // CRITICAL: Refresh filter when time text changes
                    LogCollectionViewSource.View.Refresh();
                }
            }
        }

        // NEW: Properties for DatePicker binding (MODIFIED to use Grid-prefixed properties)
        public DateTime? StartDate
        {
            get => LogDataStore.GridStartDate;
            set
            {
                if (LogDataStore.GridStartDate != value)
                {
                    LogDataStore.GridStartDate = value; // Store in Grid-prefixed static property
                    OnPropertyChanged(nameof(StartDate));
                    // CRITICAL: Refresh filter when date changes (XAML calls DateRange_SelectedDateChanged anyway)
                }
            }
        }

        public DateTime? EndDate
        {
            get => LogDataStore.GridEndDate;
            set
            {
                if (LogDataStore.GridEndDate != value)
                {
                    LogDataStore.GridEndDate = value; // Store in Grid-prefixed static property
                    OnPropertyChanged(nameof(EndDate));
                    // CRITICAL: Refresh filter when date changes (XAML calls DateRange_SelectedDateChanged anyway)
                }
            }
        }

        // NEW: Property for the Selected Timestamp Field (bound to the new ComboBox)
        public string SelectedTimestampField
        {
            get => LogDataStore.SelectedTimestampField;
            set
            {
                if (LogDataStore.SelectedTimestampField != value)
                {
                    LogDataStore.SelectedTimestampField = value;
                    // When the field changes, recalculate the default date range and refresh the filter
                    SetInitialDateRange(_logEntries);
                    LogCollectionViewSource.View.Refresh();
                    OnPropertyChanged(nameof(SelectedTimestampField));
                }
            }
        }


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
                    // CRITICAL: Calculate counts using the new field and the applied date filter
                    // This uses the implicitly filtered data from LogDataStore.GetDynamicSummaryCounts
                    LogDataStore.CalculateSummaryCounts(value);
                    OnPropertyChanged(nameof(SummaryCounts));
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
                // If data exists, ensure the timestamp field selection is updated
                UpdateAvailableStatsFields(LogDataStore.CurrentPatternDefinition.FieldNames);
                // Also set the initial date range based on the stored data and selected field
                SetInitialDateRange(_logEntries);
            }
            else
            {
                // NEW: Initialize the field lists on startup even if no data exists yet (empty list)
                UpdateAvailableStatsFields(LogDataStore.CurrentPatternDefinition.FieldNames);
            }


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

            // --- Update SelectedStatsField (for Summary Card) ---
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

            // --- Update SelectedTimestampField (for Date Filter) ---
            if (fieldNames.Contains(LogDataStore.SelectedTimestampField))
            {
                // If the previously selected timestamp field is still available, keep it.
                // NOTE: We don't use the property setter here to avoid immediate filter refresh; 
                // the full load process handles the refresh.
                LogDataStore.SelectedTimestampField = LogDataStore.SelectedTimestampField;
            }
            else
            {
                // Default to the field named 'Timestamp', 'DateTime', or the first field.
                string defaultTimestampField = fieldNames.FirstOrDefault(name => name.Equals("Timestamp", StringComparison.OrdinalIgnoreCase) || name.Equals("DateTime", StringComparison.OrdinalIgnoreCase));
                // Use the public property setter to ensure the filter UI is updated if the value changes.
                SelectedTimestampField = defaultTimestampField ?? fieldNames.FirstOrDefault();
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
        /// Grep-like search implementation. Filters the collection based on keyword search AND date range.
        /// </summary>
        private void LogFilter(object sender, FilterEventArgs e)
        {
            if (!(e.Item is LogEntry logEntry))
            {
                e.Accepted = false;
                return;
            }

            // --- 1. Date/Time Range Filter ---
            // The actual filtering logic is now centralized in LogDataStore.GetLogGridFilteredEntries.
            // We can determine acceptance by checking if the logEntry exists in the result of that function.

            // NOTE: This check might be slow for massive logs as it iterates the list, but for simplicity
            // and consistency with the ChartViewer's filtering logic, we rely on the centralized helper here.

            // We iterate over the results of the filtered list to check if the current log entry is included.
            bool dateMatch = LogDataStore.GetLogGridFilteredEntries().Contains(logEntry);

            // If the date filter failed, we stop here.
            if (!dateMatch)
            {
                e.Accepted = false;
                return;
            }


            // --- 2. Keyword (Grep) Filter ---
            // (Keyword filter remains local to the grid view)

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
            // If inverted, accept if there was NO keyword match.
            // If not inverted, accept if there WAS a keyword match.
            // Since we already filtered by date, we just check the keyword match condition.
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
        /// NEW: Event handler for the DatePicker change.
        /// </summary>
        private void DateRange_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            // CRITICAL: Refresh filter when date changes AND recalculate summary stats for the current field
            if (LogCollectionViewSource?.View != null)
            {
                LogCollectionViewSource.View.Refresh();
                LogDataStore.CalculateSummaryCounts(SelectedStatsField);
            }
        }

        /// <summary>
        /// NEW: Event handler for the Time TextBoxes.
        /// </summary>
        private void TimeRange_TextChanged(object sender, TextChangedEventArgs e)
        {
            // CRITICAL: Refresh filter when time text changes AND recalculate summary stats for the current field
            if (LogCollectionViewSource?.View != null)
            {
                LogCollectionViewSource.View.Refresh();
                LogDataStore.CalculateSummaryCounts(SelectedStatsField);
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

            // NEW: Enable/Disable Export button based on data existence
            ExportButton.IsEnabled = LogDataStore.LogEntries.Any();

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
        /// NEW: Event handler for the Export button. Opens the export options pop-up.
        /// </summary>
        private void ExportLog_Click(object sender, RoutedEventArgs e)
        {
            // 1. Ensure there is data to export (even if filter results in zero, we can still show the dialog)
            if (!LogDataStore.LogEntries.Any())
            {
                MessageBox.Show("No log file has been loaded or parsed yet.", "Export Unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. Open the custom modal dialog with the current column definitions
            var exportWindow = new ExportWindow(this.ColumnControls);

            if (exportWindow.ShowDialog() == true)
            {
                // The user clicked 'Export' in the modal.
                // We use the CollectionViewSource.View.Cast<LogEntry>().ToList() to get only the filtered items
                var filterItems = LogCollectionViewSource.View.Cast<LogEntry>().ToList();

                if (!filterItems.Any())
                {
                    MessageBox.Show("The current filter selection resulted in zero log entries to export.", "No Data to Export", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 3. Prompt for file save location
                SaveFileDialog saveDialog = new SaveFileDialog();

                string extension;
                string filter;

                switch (exportWindow.FinalFormat)
                {
                    case ExportFormat.CSV:
                        extension = "csv";
                        filter = "CSV (Comma Separated Values)|*.csv";
                        break;
                    case ExportFormat.TXT:
                        extension = "txt";
                        filter = "TXT (Plain Text - Tab Separated)|*.txt";
                        break;
                    case ExportFormat.JSON:
                        extension = "json";
                        filter = "JSON (JavaScript Object Notation)|*.json";
                        break;
                    default:
                        // Should not happen, but for safety
                        extension = "txt";
                        filter = "All files (*.*)|*.*";
                        break;
                }

                saveDialog.Filter = filter + "|All files (*.*)|*.*";
                saveDialog.FileName = "FilteredLogExport." + extension;
                saveDialog.Title = "Save Filtered Log File";

                if (saveDialog.ShowDialog() == true)
                {
                    // 4. Perform the export
                    try
                    {
                        ExportFilteredLogs(
                            saveDialog.FileName,
                            filterItems,
                            exportWindow.FinalColumns.Where(c => c.IsVisible).ToList(),
                            exportWindow.FinalFormat
                        );
                        MessageBox.Show($"Successfully exported {filterItems.Count} lines to:\n{saveDialog.FileName}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"An error occurred during export: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        /// <summary>
        /// NEW: Performs the core logic of exporting the filtered log entries to a file.
        /// </summary>
        private void ExportFilteredLogs(string filePath, List<LogEntry> logEntries, List<ColumnModel> columnsToExport, ExportFormat format)
        {
            if (!columnsToExport.Any() || !logEntries.Any()) return;

            // --- JSON EXPORT ---
            if (format == ExportFormat.JSON)
            {
                // Create a list of dictionaries, containing only the selected fields
                var exportData = logEntries.Select(entry =>
                {
                    var dict = new Dictionary<string, string>();
                    foreach (var col in columnsToExport)
                    {
                        // Use the Header as the JSON key for readability, defaulting to FieldName if header is empty
                        string key = col.Header.Replace(" ", "_"); // Sanitize key for JSON

                        // Safely get the value
                        if (entry.Fields.TryGetValue(col.FieldName, out string value))
                        {
                            dict.Add(key, value);
                        }
                        else
                        {
                            dict.Add(key, "N/A");
                        }
                    }
                    return dict;
                }).ToList();

                // Serialize the list of dictionaries to a JSON string
                // Use indented formatting for human readability
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(exportData, options);

                // Write to file
                File.WriteAllText(filePath, jsonString, Encoding.UTF8);
                return;
            }

            // --- CSV / TXT EXPORT ---
            char delimiter = format == ExportFormat.CSV ? ',' : '\t';
            var sb = new StringBuilder();

            // 1. Write Header Row
            // Use the ColumnModel Header for the output header row
            string header = string.Join(delimiter.ToString(), columnsToExport.Select(c => $"\"{c.Header}\""));
            sb.AppendLine(header);

            // 2. Write Data Rows
            foreach (var entry in logEntries)
            {
                var row = columnsToExport.Select(column =>
                {
                    // Safely try to get the field value
                    if (entry.Fields.TryGetValue(column.FieldName, out string value))
                    {
                        // Escape quotes and wrap in quotes for robust CSV/TSV handling
                        // Newlines are removed for single-line log entries
                        return $"\"{value.Replace("\"", "\"\"").Replace("\r", "").Replace("\n", "")}\"";
                    }
                    return "\"N/A\""; // Default value if field is missing
                });
                sb.AppendLine(string.Join(delimiter.ToString(), row));
            }

            // 3. Write to file
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
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
            ExportButton.IsEnabled = false; // Disable export button during processing

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

                // NEW: Set the initial date filter range to the min/max dates found in the data
                // This now uses LogDataStore.SelectedTimestampField
                SetInitialDateRange(_logEntries);

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
                // ExportButton.IsEnabled state is handled by RefreshView (called above)
            }
        }

        /// <summary>
        /// NEW: Sets the initial Start and End Date filter values based on the data's timestamp range 
        /// using the currently selected timestamp field.
        /// </summary>
        private void SetInitialDateRange(ObservableCollection<LogEntry> entries)
        {
            // Update Log Grid filters
            LogDataStore.GridStartDate = null;
            LogDataStore.GridEndDate = null;

            // We do *not* update the time text here, as they default to empty string,
            // relying on CombineDateTime to default to 00:00:00.

            OnPropertyChanged(nameof(StartTimeText));
            OnPropertyChanged(nameof(EndTimeText));

            if (!entries.Any()) return;

            // Use the currently selected timestamp field
            string timestampFieldName = LogDataStore.SelectedTimestampField;

            if (string.IsNullOrEmpty(timestampFieldName)) return;

            // Collect all successfully parsed dates from the selected column
            var validDates = entries
                .Where(e => e.Fields.ContainsKey(timestampFieldName))
                .Select(e => e.Fields[timestampFieldName])
                // Use the new TryParseLogDateTime helper
                .Where(v => LogDataStore.TryParseLogDateTime(v, out DateTime _))
                .Select(v => { LogDataStore.TryParseLogDateTime(v, out DateTime dt); return dt; })
                .ToList();

            if (validDates.Any())
            {
                // Get the date part of the earliest and latest entry
                LogDataStore.GridStartDate = validDates.Min().Date;
                LogDataStore.GridEndDate = validDates.Max().Date;

                // Notify the UI to update the DatePicker controls
                OnPropertyChanged(nameof(StartDate));
                OnPropertyChanged(nameof(EndDate));
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

            // NEW: Add the custom patterns file path to the arguments if it's set
            string customPatternsPath = LogDataStore.CustomPatternsFilePath;
            // Use 'null' string if empty, so Python can check for it
            string customPathArg = string.IsNullOrWhiteSpace(customPatternsPath) ? "null" : $"\"{customPatternsPath}\"";


            // FIX: Explicitly include the scriptPath in the arguments for reliable execution.
            if (command == "parse")
            {
                // For parsing, we need all four arguments
                string fieldNamesJson = JsonSerializer.Serialize(fieldNames);
                string escapedLogPattern = logPattern.Replace("\"", "\\\"");
                string escapedFieldNamesJson = fieldNamesJson.Replace("\"", "\\\"");
                string isBestEffortStr = isBestEffortParse ? "true" : "false"; // Correct variable name

                // MODIFIED: Added the custom path argument as the last parameter
                arguments = $"{scriptPath} {command} \"{filePath}\" \"{escapedLogPattern}\" \"{escapedFieldNamesJson}\" {isBestEffortStr} {customPathArg}";
            }
            // NEW command name and logic: passing file path for robust suggestion
            else if (command == "suggest_robust_pattern")
            {
                // For suggestion, we only need the file path
                // MODIFIED: Added the custom path argument as the last parameter
                arguments = $"{scriptPath} {command} \"{filePath}\" {customPathArg}";
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
                ExportButton.IsEnabled = false; // Disable export button during suggestion

                // Run the Python suggester command, passing the full file path for robust checking
                // CRITICAL: RunPythonParser now automatically includes LogDataStore.CustomPatternsFilePath
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