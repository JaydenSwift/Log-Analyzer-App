using System;
using System.Collections.Generic;
using System.Collections.ObjectModel; // Added for ObservableCollection
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

        private string[] _labels;
        public string[] Labels
        {
            get => _labels;
            set { _labels = value; OnPropertyChanged(nameof(Labels)); }
        }

        // Formatter is used for the Y-Axis label display (Count)
        public Func<double, string> Formatter { get; set; }

        // --- Chart Grouping & State Variables ---

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


        // UI State
        // NOTE: These fixed booleans are only used to filter the *Bar Chart* for a fixed subset of levels.
        private bool _isInfoVisible = true;
        private bool _isWarnVisible = true;
        private bool _isErrorVisible = true;

        // NEW: Centralized color mapping for dynamic levels
        private readonly Dictionary<string, SolidColorBrush> LevelColors = new Dictionary<string, SolidColorBrush>(StringComparer.OrdinalIgnoreCase)
        {
            // Consistent colors with LogAnalyzerView.xaml.cs
            { "INFO", (SolidColorBrush)new BrushConverter().ConvertFromString("#4CAF50") }, // Green
            { "WARN", (SolidColorBrush)new BrushConverter().ConvertFromString("#FFC107") }, // Amber
            { "ERROR", (SolidColorBrush)new BrushConverter().ConvertFromString("#F44336") },  // Red
            { "DEBUG", (SolidColorBrush)new BrushConverter().ConvertFromString("#9E9E9E") }, // Gray
            { "FATAL", (SolidColorBrush)new BrushConverter().ConvertFromString("#B71C1C") }  // Dark Red
        };

        // --- Constructor ---
        public ChartViewer()
        {
            InitializeComponent();
            this.DataContext = this;

            // Load all necessary data when the viewer is first loaded or navigated to
            this.Loaded += ChartViewer_Loaded;

            // NOTE: The explicit calls to 'new SeriesCollection()' are removed from InitializeChartData 
            // because they are now initialized in the property declarations above.
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
                return;
            }

            // Fetch dynamic counts based on the currently selected field using the LogDataStore method
            ObservableCollection<CountItem> dynamicLogData = LogDataStore.GetDynamicSummaryCounts(SelectedStatsField);

            // 1. Setup Labels (Must be before SeriesCollection)
            Labels = dynamicLogData.Select(c => c.Key).ToArray();

            // 2. Update Charts
            UpdateBarChart(dynamicLogData);
            UpdatePieChart(dynamicLogData, SliceThresholdSlider.Value);
        }

        // --- Core Update Methods ---

        /// <summary>
        /// MODIFIED: Updates the Column/Bar Chart based on current filters and dynamic data.
        /// </summary>
        /// <param name="logData">The source data collection (ObservableCollection<CountItem>).</param>
        private void UpdateBarChart(ObservableCollection<CountItem> logData)
        {
            SeriesCollection.Clear();

            // 1. Prepare data for the Bar Chart
            var filteredLevels = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            // Determine if the currently selected field is a 'Level'-like field
            bool isLevelField = SelectedStatsField.Equals("Level", StringComparison.OrdinalIgnoreCase);

            if (isLevelField)
            {
                // If it's a level-like field, apply fixed INFO/WARN/ERROR filtering from checkboxes
                foreach (var item in logData)
                {
                    bool isKnownLevel = LevelColors.ContainsKey(item.Key);

                    if (isKnownLevel)
                    {
                        if (item.Key.Equals("INFO", StringComparison.OrdinalIgnoreCase) && _isInfoVisible) filteredLevels.Add(item.Key, item.Count);
                        else if (item.Key.Equals("WARN", StringComparison.OrdinalIgnoreCase) && _isWarnVisible) filteredLevels.Add(item.Key, item.Count);
                        else if (item.Key.Equals("ERROR", StringComparison.OrdinalIgnoreCase) && _isErrorVisible) filteredLevels.Add(item.Key, item.Count);
                        // All other known levels (DEBUG, FATAL) are hidden by default fixed filters
                    }
                    else
                    {
                        // Include unknown fields
                        filteredLevels.Add(item.Key, item.Count);
                    }
                }
            }
            else
            {
                // If grouping by a custom field (like 'Thread' or 'Resource'), display all entries
                foreach (var item in logData)
                {
                    filteredLevels.Add(item.Key, item.Count);
                }
            }

            // Re-update Labels for the X-axis to reflect only the displayed levels
            Labels = filteredLevels.Keys.ToArray();
            var chartValues = new ChartValues<double>(filteredLevels.Values);


            // 2. Add the series
            if (chartValues.Any(v => v > 0))
            {
                SeriesCollection.Add(new ColumnSeries
                {
                    // Use the Selected Field as the title
                    Title = SelectedStatsField,
                    Values = chartValues,
                    // Use a dynamic color that works well for bars - picking a consistent theme color
                    Fill = (SolidColorBrush)new BrushConverter().ConvertFromString("#FF007ACC"),
                    DataLabels = true,
                    LabelPoint = point => point.Y.ToString("N0")
                });
            }
        }

        /// <summary>
        /// MODIFIED: Updates the Pie Chart based on the current data, label mode, and slice threshold.
        /// </summary>
        /// <param name="logData">The source data collection (ObservableCollection<CountItem>).</param>
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
                    // Use the color defined in the LevelColors map, falling back if key is dynamic
                    SolidColorBrush sliceColor;
                    if (LevelColors.TryGetValue(countItem.Key, out var predefinedBrush))
                    {
                        sliceColor = predefinedBrush;
                    }
                    else
                    {
                        // Fallback to a consistent gray for truly unknown/dynamic levels
                        sliceColor = (SolidColorBrush)new BrushConverter().ConvertFromString("#9E9E9E");
                    }

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

            // Re-run the update to apply the filters to the current grouping
            UpdateAllCharts();
        }

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