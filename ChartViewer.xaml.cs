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

        // Holds the hardcoded log counts
        private Dictionary<string, double> _logData = new Dictionary<string, double>
        {
            { "INFO", 12500 },
            { "WARN", 580 },
            { "ERROR", 20 }
        };

        // UI State
        private bool _isInfoVisible = true;
        private bool _isWarnVisible = true;
        private bool _isErrorVisible = true;

        // --- Constructor ---
        public ChartViewer()
        {
            InitializeComponent();
            this.DataContext = this;
            InitializeChartData();
        }

        // --- Initialization ---

        private void InitializeChartData()
        {
            // 1. Setup Formatter
            Formatter = value => value.ToString("N0");

            // 2. Setup Labels (Must be before SeriesCollection)
            Labels = new[] { "INFO", "WARN", "ERROR" };

            // 3. Initialize Collections
            SeriesCollection = new SeriesCollection();
            PieSeriesCollection = new SeriesCollection();

            // 4. Initial Chart Population
            UpdateBarChart();
            UpdatePieChart(0); // Initialize with 0 threshold, default percentage mode
        }

        // --- Core Update Methods ---

        private void UpdateBarChart()
        {
            SeriesCollection.Clear();

            // Create the data array based on current filters
            var filteredData = new ChartValues<double>
            {
                _isInfoVisible ? _logData["INFO"] : 0,
                _isWarnVisible ? _logData["WARN"] : 0,
                _isErrorVisible ? _logData["ERROR"] : 0
            };

            // Only add the series if at least one level is visible
            if (filteredData.Any(v => v > 0))
            {
                SeriesCollection.Add(new ColumnSeries
                {
                    Title = "Log Events",
                    Values = filteredData,
                    Fill = Brushes.Blue, // Solid blue for the combined bar
                    DataLabels = true,
                    LabelPoint = point => point.Y.ToString("N0")
                });
            }
        }

        private void UpdatePieChart(double minPercentageThreshold)
        {
            PieSeriesCollection.Clear();

            double total = _logData.Values.Sum();
            if (total == 0) return;

            // Define colors for consistency
            var colors = new Dictionary<string, SolidColorBrush>
            {
                { "INFO", Brushes.Green },
                { "WARN", Brushes.Orange },
                { "ERROR", Brushes.Red }
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

            foreach (var kvp in _logData)
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

            UpdateBarChart();
        }

        /// <summary>
        /// Handles RadioButton changes for the Pie Chart label display.
        /// </summary>
        private void PieLabelMode_Changed(object sender, RoutedEventArgs e)
        {
            // CRITICAL FIX: Prevent execution if the control is not fully loaded.
            if (!IsLoaded) return;

            // Re-run the pie chart update to apply the new label formatter based on checked state
            UpdatePieChart(SliceThresholdSlider.Value);
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
            UpdatePieChart(e.NewValue);
        }

        // --- INotifyPropertyChanged Implementation ---
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
