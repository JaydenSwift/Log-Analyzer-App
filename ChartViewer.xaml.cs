using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiveCharts;
using LiveCharts.Wpf;

namespace Log_Analyzer_App
{
    /// <summary>
    /// Interaction logic for ChartViewer.xaml
    /// </summary>
    public partial class ChartViewer : UserControl, INotifyPropertyChanged
    {
        // --- LiveCharts Properties ---
        private SeriesCollection _seriesCollection;
        public SeriesCollection SeriesCollection
        {
            get => _seriesCollection;
            set { _seriesCollection = value; OnPropertyChanged(nameof(SeriesCollection)); }
        }

        private SeriesCollection _pieSeriesCollection;
        public SeriesCollection PieSeriesCollection
        {
            get => _pieSeriesCollection;
            set { _pieSeriesCollection = value; OnPropertyChanged(nameof(PieSeriesCollection)); }
        }

        private string[] _labels;
        public string[] Labels
        {
            get => _labels;
            set { _labels = value; OnPropertyChanged(nameof(Labels)); }
        }

        // Formatter is used for the Y-Axis label display (Count)
        public Func<double, string> Formatter { get; set; }

        // --- Data and State Variables ---

        // UI State
        private bool _isInfoVisible = true;
        private bool _isWarnVisible = true;
        private bool _isErrorVisible = true;

        // --- Constructor ---
        public ChartViewer()
        {
            InitializeComponent();
            this.DataContext = this;

            // In case the user navigates directly here without loading a file first, 
            // ensure the counts are calculated (will result in 0s if no data).
            LogDataStore.CalculateSummaryCounts();

            InitializeChartData();
        }

        // --- Initialization ---

        private void InitializeChartData()
        {
            // Fetch live data from the persistent store
            var logData = LogDataStore.SummaryCounts;

            // 1. Setup Formatter
            Formatter = value => value.ToString("N0");

            // 2. Setup Labels (Must be before SeriesCollection)
            // Use the keys from the summary counts dictionary for the X-axis labels
            Labels = logData.Keys.ToArray();

            // 3. Initialize Collections
            SeriesCollection = new SeriesCollection();
            PieSeriesCollection = new SeriesCollection();

            // 4. Initial Chart Population
            UpdateBarChart(logData);
            UpdatePieChart(logData, SliceThresholdSlider.Value);
        }

        // --- Core Update Methods ---

        /// <summary>
        /// Updates the Column/Bar Chart based on current filters and data.
        /// </summary>
        /// <param name="logData">The source data dictionary (LogDataStore.SummaryCounts).</param>
        private void UpdateBarChart(Dictionary<string, double> logData)
        {
            SeriesCollection.Clear();

            // Create the data array based on current filters
            var filteredData = new ChartValues<double>
            {
                // Use safe checks to ensure the keys exist, although LogDataStore pre-initializes them.
                _isInfoVisible ? logData.ContainsKey("INFO") ? logData["INFO"] : 0 : 0,
                _isWarnVisible ? logData.ContainsKey("WARN") ? logData["WARN"] : 0 : 0,
                _isErrorVisible ? logData.ContainsKey("ERROR") ? logData["ERROR"] : 0 : 0
            };

            // Only add the series if at least one level is visible and has a value
            if (filteredData.Any(v => v > 0))
            {
                SeriesCollection.Add(new ColumnSeries
                {
                    Title = "Log Events",
                    Values = filteredData,
                    // Use a dynamic color that works well for bars
                    Fill = (SolidColorBrush)new BrushConverter().ConvertFromString("#FF007ACC"),
                    DataLabels = true,
                    LabelPoint = point => point.Y.ToString("N0")
                });
            }
        }

        /// <summary>
        /// Updates the Pie Chart based on the current data, label mode, and slice threshold.
        /// </summary>
        /// <param name="logData">The source data dictionary (LogDataStore.SummaryCounts).</param>
        /// <param name="minPercentageThreshold">The minimum percentage for a slice to be displayed.</param>
        private void UpdatePieChart(Dictionary<string, double> logData, double minPercentageThreshold)
        {
            PieSeriesCollection.Clear();

            double total = logData.Values.Sum();
            if (total == 0) return;

            // Define colors for consistency
            var colors = new Dictionary<string, SolidColorBrush>
            {
                { "INFO", (SolidColorBrush)new BrushConverter().ConvertFromString("#4CAF50") }, // Green
                { "WARN", (SolidColorBrush)new BrushConverter().ConvertFromString("#FFC107") }, // Amber
                { "ERROR", (SolidColorBrush)new BrushConverter().ConvertFromString("#F44336") }  // Red
            };

            // Determine label formatter based on RadioButton selection
            Func<ChartPoint, string> labelFormatter = point =>
            {
                // Safety check: Ensure controls are initialized before accessing them
                if (!IsLoaded) return string.Empty;

                if (PercentRadio.IsChecked == true)
                {
                    return $"{point.SeriesView.Title}: {point.Participation:P1}";
                }
                return $"{point.SeriesView.Title}: {point.Y:N0}";
            };

            foreach (var kvp in logData)
            {
                double percentage = (kvp.Value / total) * 100;

                // Apply minimum percentage threshold filter
                if (percentage >= minPercentageThreshold)
                {
                    PieSeriesCollection.Add(new PieSeries
                    {
                        Title = kvp.Key,
                        Values = new ChartValues<double> { kvp.Value },
                        Fill = colors[kvp.Key],
                        DataLabels = true,
                        LabelPoint = labelFormatter
                    });
                }
            }
        }

        // --- Event Handlers ---

        /// <summary>
        /// Handles CheckBox changes for the Bar Chart filters.
        /// </summary>
        private void LogFilter_Changed(object sender, RoutedEventArgs e)
        {
            // CRITICAL FIX: Prevent execution if the control is not fully loaded.
            if (!IsLoaded) return;

            _isInfoVisible = InfoCheckBox.IsChecked ?? false;
            _isWarnVisible = WarnCheckBox.IsChecked ?? false;
            _isErrorVisible = ErrorCheckBox.IsChecked ?? false;

            // Pass the live data to the update method
            UpdateBarChart(LogDataStore.SummaryCounts);
        }

        /// <summary>
        /// Handles RadioButton changes for the Pie Chart label display.
        /// </summary>
        private void PieLabelMode_Changed(object sender, RoutedEventArgs e)
        {
            // CRITICAL FIX: Prevent execution if the control is not fully loaded.
            if (!IsLoaded) return;

            // Re-run the pie chart update to apply the new label formatter based on checked state
            UpdatePieChart(LogDataStore.SummaryCounts, SliceThresholdSlider.Value);
        }

        /// <summary>
        /// Handles Slider changes for the Pie Chart slice threshold.
        /// </summary>
        private void SliceThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // CRITICAL FIX: Prevent execution if the control is not fully loaded.
            if (!IsLoaded) return;

            // Update the display text
            SlicePercentageText.Text = $"Minimum Slice Percentage ({e.NewValue:N0}%)";

            // Re-run the pie chart update with the new threshold
            UpdatePieChart(LogDataStore.SummaryCounts, e.NewValue);
        }

        // --- INotifyPropertyChanged Implementation ---
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
