using LiveCharts;
using LiveCharts.Definitions.Series;
using LiveCharts.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel; // Added for ObservableCollection
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Log_Analyzer_App
{
    // MOVED: Model for dynamic legend items - Now defined outside the ChartViewer class
    public class ChartLegendItem : INotifyPropertyChanged
    {
        private string _key;
        public string Key
        {
            get => _key;
            set { _key = value; OnPropertyChanged(nameof(Key)); }
        }

        private System.Windows.Media.Brush _color;
        public System.Windows.Media.Brush Color
        {
            get => _color;
            set { _color = value; OnPropertyChanged(nameof(Color)); }
        }

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; OnPropertyChanged(nameof(IsVisible)); }
        }

        // Reference to the actual LiveCharts series object
        public ISeriesView LiveChartSeries { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }


    /// <summary>
    /// Interaction logic for ChartViewer.xaml
    /// </summary>
    public partial class ChartViewer : UserControl, INotifyPropertyChanged
    {
        // --- LiveCharts Properties ---

        // FIX: Initialize collections upon declaration to prevent NullReferenceException
        private SeriesCollection _seriesCollection = new SeriesCollection();
        public SeriesCollection SeriesCollection
        {
            get => _seriesCollection;
            set { _seriesCollection = value; OnPropertyChanged(nameof(SeriesCollection)); }
        }

        // FIX: Initialize collections upon declaration to prevent NullReferenceException
        private SeriesCollection _pieSeriesCollection = new SeriesCollection();
        public SeriesCollection PieSeriesCollection
        {
            get => _pieSeriesCollection;
            set { _pieSeriesCollection = value; OnPropertyChanged(nameof(PieSeriesCollection)); }
        }

        // Labels property is kept but will no longer be used for the X-Axis in the new ColumnSeries structure
        private string[] _labels;
        public string[] Labels
        {
            get => _labels;
            set { _labels = value; OnPropertyChanged(nameof(Labels)); }
        }

        // Formatter is used for the Y-Axis label display (Count)
        public Func<double, string> Formatter { get; set; }

        // --- Chart Grouping & State Variables ---

        // NEW: Collection to hold items for the Custom Legend (Bar Chart)
        private ObservableCollection<ChartLegendItem> _barChartLegendItems = new ObservableCollection<ChartLegendItem>();
        public ObservableCollection<ChartLegendItem> BarChartLegendItems
        {
            get => _barChartLegendItems;
            set { _barChartLegendItems = value; OnPropertyChanged(nameof(BarChartLegendItems)); }
        }

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
                    // Recalculate charts whenever the grouping field changes
                    UpdateAllCharts();
                    OnPropertyChanged(nameof(SelectedStatsField));
                }
            }
        }

        // --- Constructor ---
        public ChartViewer()
        {
            InitializeComponent();
            this.DataContext = this;

            // Load all necessary data when the viewer is first loaded or navigated to
            this.Loaded += ChartViewer_Loaded;
        }

        /// <summary>
        /// Handles the Loaded event to ensure LogDataStore is populated before initializing charts.
        /// </summary>
        private void ChartViewer_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeAvailableStatsFields();
            InitializeChartData();
        }

        /// <summary>
        /// NEW: Populates the available fields based on the currently loaded log entries.
        /// </summary>
        private void InitializeAvailableStatsFields()
        {
            AvailableStatsFields.Clear();
            List<string> fieldNames = LogDataStore.CurrentPatternDefinition?.FieldNames ?? new List<string>();

            foreach (var fieldName in fieldNames)
            {
                AvailableStatsFields.Add(fieldName);
            }

            // Set the default selection for the chart grouping.
            if (fieldNames.Any())
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


        // --- Initialization ---

        private void InitializeChartData()
        {
            // 1. Setup Formatter
            Formatter = value => value.ToString("N0");

            // 2. Collections are initialized at property declaration. We skip re-initialization here.

            // 3. Update charts immediately based on the initialized SelectedStatsField
            // This is called *after* SelectedStatsField is set in InitializeAvailableStatsFields.
            if (SelectedStatsField != null)
            {
                UpdateAllCharts();
            }
        }

        /// <summary>
        /// NEW: Centralized update method to fetch data and refresh both charts based on SelectedStatsField.
        /// </summary>
        private void UpdateAllCharts()
        {
            if (string.IsNullOrEmpty(SelectedStatsField))
            {
                SeriesCollection.Clear();
                PieSeriesCollection.Clear();
                Labels = new string[0];
                BarChartLegendItems.Clear();
                return;
            }

            // Fetch dynamic counts based on the currently selected field using the LogDataStore method
            ObservableCollection<CountItem> dynamicLogData = LogDataStore.GetDynamicSummaryCounts(SelectedStatsField);

            // 1. Labels property is now redundant for X-Axis but kept for compatibility/debug
            Labels = dynamicLogData.Select(c => c.Key).ToArray();

            // 2. Update Charts
            UpdateBarChart(dynamicLogData);
            UpdatePieChart(dynamicLogData, SliceThresholdSlider.Value);
        }

        // --- Core Update Methods ---

        /// <summary>
        /// MODIFIED: Updates the Column/Bar Chart.
        /// CRITICAL FIX: Creates a separate ColumnSeries for *every* data point (bar)
        /// so that each bar can be assigned its unique, dynamic color, and updates the custom legend items.
        /// </summary>
        /// <param name="logData">The source data collection (ObservableCollection<CountItem>).</param>
        private void UpdateBarChart(ObservableCollection<CountItem> logData)
        {
            SeriesCollection.Clear();
            BarChartLegendItems.Clear(); // Clear and rebuild the custom legend

            if (!logData.Any()) return;

            // Iterate through the CountItems and create a distinct ColumnSeries for each item.
            foreach (var item in logData)
            {
                var series = new ColumnSeries
                {
                    // Use the key as the series Title, which LiveCharts will use for the X-axis label/legend.
                    Title = item.Key,
                    // Values is a collection with only one element: the count for this key.
                    Values = new ChartValues<double> { item.Count },
                    // Use the color assigned in LogDataStore for the bar's Fill.
                    Fill = item.Color as SolidColorBrush,
                    // Optional: Show data labels on the bar
                    DataLabels = true,
                    LabelPoint = point => point.Y.ToString("N0")
                };

                SeriesCollection.Add(series);

                // NEW: Add item to the custom legend collection
                BarChartLegendItems.Add(new ChartLegendItem
                {
                    Key = item.Key,
                    Color = item.Color,
                    IsVisible = true, // Start visible
                    LiveChartSeries = series // Store reference to the series object
                });
            }
        }

        /// <summary>
        /// MODIFIED: Updates the Pie Chart based on the current data, label mode, and slice threshold.
        /// It now uses the dynamic color brush assigned in LogDataStore for each CountItem.
        /// </summary>
        /// <param name="logData">Ghe source data collection (ObservableCollection<CountItem>).</param>
        /// <param name="minPercentageThreshold">The minimum percentage for a slice to be displayed.</param>
        private void UpdatePieChart(ObservableCollection<CountItem> logData, double minPercentageThreshold)
        {
            PieSeriesCollection.Clear();

            double total = logData.Sum(c => c.Count);
            if (total == 0) return;

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

            foreach (var countItem in logData)
            {
                double percentage = (countItem.Count / total) * 100;

                // Apply minimum percentage threshold filter
                if (percentage >= minPercentageThreshold)
                {
                    // CRITICAL FIX: Use the color property directly from CountItem.
                    SolidColorBrush sliceColor = countItem.Color as SolidColorBrush;

                    // Safety check, although it should always be a SolidColorBrush now
                    if (sliceColor == null) continue;

                    PieSeriesCollection.Add(new PieSeries
                    {
                        Title = countItem.Key,
                        Values = new ChartValues<double> { countItem.Count },
                        Fill = sliceColor,
                        DataLabels = true,
                        LabelPoint = labelFormatter
                    });
                }
            }
        }

        /// <summary>
        /// NEW: Toggles the visibility of the linked LiveCharts Series object when a legend item is clicked.
        /// </summary>
        private void ToggleSeriesVisibility(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ChartLegendItem legendItem)
            {
                // Toggle the state in the data model
                legendItem.IsVisible = !legendItem.IsVisible;

                // Apply the visibility change directly to the linked LiveChartSeries
                if (legendItem.LiveChartSeries is Series series)
                {
                    series.Visibility = legendItem.IsVisible ? Visibility.Visible : Visibility.Hidden;
                }
            }
        }


        // --- Event Handlers ---

        /// <summary>
        /// Handles RadioButton changes for the Pie Chart label display.
        /// </summary>
        private void PieLabelMode_Changed(object sender, RoutedEventArgs e)
        {
            // CRITICAL FIX: Prevent execution if the control is not fully loaded.
            if (!IsLoaded) return;

            // Recalculate only the pie chart labels
            ObservableCollection<CountItem> dynamicLogData = LogDataStore.GetDynamicSummaryCounts(SelectedStatsField);
            UpdatePieChart(dynamicLogData, SliceThresholdSlider.Value);
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
            ObservableCollection<CountItem> dynamicLogData = LogDataStore.GetDynamicSummaryCounts(SelectedStatsField);
            UpdatePieChart(dynamicLogData, e.NewValue);
        }

        // --- INotifyPropertyChanged Implementation ---
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}