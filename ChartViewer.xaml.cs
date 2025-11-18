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
using System.Threading.Tasks; // NEW: Added for threading
using System.Windows.Threading; // Added for completeness, though not explicitly used for Invoke

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
        // ** NEW: Expose the TopNLimit constant locally for UI binding **
        public int TopNLimit => 10;

        // ** NEW: Property to indicate if data has been aggregated into an "Other" category **
        private bool _isAggregated = false;
        public bool IsAggregated
        {
            get => _isAggregated;
            set { _isAggregated = value; OnPropertyChanged(nameof(IsAggregated)); }
        }

        // --- LiveCharts Properties ---

        private SeriesCollection _seriesCollection = new SeriesCollection();
        public SeriesCollection SeriesCollection
        {
            get => _seriesCollection;
            set { _seriesCollection = value; OnPropertyChanged(nameof(SeriesCollection)); }
        }

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

        public Func<double, string> Formatter { get; set; }

        // --- Chart Grouping & State Variables ---

        // NEW: Property to indicate if calculation is in progress (for UI threading)
        private bool _isCalculating = false;
        public bool IsCalculating
        {
            get => _isCalculating;
            set { _isCalculating = value; OnPropertyChanged(nameof(IsCalculating)); }
        }


        // NEW: Time Series Chart Properties
        private SeriesCollection _timeSeriesCollection = new SeriesCollection();
        public SeriesCollection TimeSeriesCollection
        {
            get => _timeSeriesCollection;
            set { _timeSeriesCollection = value; OnPropertyChanged(nameof(TimeSeriesCollection)); }
        }

        private string[] _timeLabels;
        public string[] TimeLabels
        {
            get => _timeLabels;
            set { _timeLabels = value; OnPropertyChanged(nameof(TimeLabels)); }
        }

        // NEW: Time Range Grouping Properties
        private TimeRangeType _selectedTimeRange = TimeRangeType.Months;
        public TimeRangeType SelectedTimeRange
        {
            get => _selectedTimeRange;
            set
            {
                if (_selectedTimeRange != value)
                {
                    _selectedTimeRange = value;
                    // FIX for CS4014 on line 168: Use a pattern that avoids the warning while running async.
                    // We call the helper method and ignore the Task result directly in the setter.
#pragma warning disable CS4014 // Because this call is not awaited...
                    UpdateOnlyTimeChartTask();
#pragma warning restore CS4014
                    OnPropertyChanged(nameof(SelectedTimeRange));
                }
            }
        }

        // NEW: Helper method to call UpdateOnlyTimeChart without blocking the setter.
        // We moved the async logic here so the setter stays synchronous.
        private async Task UpdateOnlyTimeChartTask()
        {
            await UpdateOnlyTimeChart();
        }

        // Exposes the enum values for ComboBox binding
        public Array TimeRangeTypes => Enum.GetValues(typeof(TimeRangeType));


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
                    // FIX for CS4014 on line 123: Use a helper task pattern for the setter.
#pragma warning disable CS4014 // Because this call is not awaited...
                    UpdateAllChartsTask(ignoreTimeChart: false);
#pragma warning restore CS4014
                    OnPropertyChanged(nameof(SelectedStatsField));
                }
            }
        }

        // NEW: Helper task for SelectedStatsField setter (same pattern as above helper)
        private async Task UpdateAllChartsTask(bool ignoreTimeChart)
        {
            await UpdateAllCharts(ignoreTimeChart);
        }

        // --- NEW: Chart-Specific Filter Properties ---

        private DateTime? _chartStartDate;
        public DateTime? ChartStartDate
        {
            get => _chartStartDate;
            set { _chartStartDate = value; OnPropertyChanged(nameof(ChartStartDate)); }
        }

        private DateTime? _chartEndDate;
        public DateTime? ChartEndDate
        {
            get => _chartEndDate;
            set { _chartEndDate = value; OnPropertyChanged(nameof(ChartEndDate)); }
        }

        private string _chartStartTimeText = string.Empty;
        public string ChartStartTimeText
        {
            get => _chartStartTimeText;
            set { _chartStartTimeText = value; OnPropertyChanged(nameof(ChartStartTimeText)); }
        }

        private string _chartEndTimeText = string.Empty;
        public string ChartEndTimeText
        {
            get => _chartEndTimeText;
            set { _chartEndTimeText = value; OnPropertyChanged(nameof(ChartEndTimeText)); }
        }

        // NEW: Property for displaying the filtered log count
        private int _filteredLogCount = 0;
        public int FilteredLogCount
        {
            get => _filteredLogCount;
            set { _filteredLogCount = value; OnPropertyChanged(nameof(FilteredLogCount)); }
        }

        // --- Constructor ---
        public ChartViewer()
        {
            InitializeComponent();
            this.DataContext = this;

            // Load all necessary data when the viewer is first loaded or navigated to
            this.Loaded += ChartViewer_Loaded;

            // Set initial date range (full range of the loaded data)
            SetInitialDateRange();
        }

        /// <summary>
        /// NEW: Handles the click of the "Apply Filter" button.
        /// </summary>
        private async void ApplyChartFilter_Click(object sender, RoutedEventArgs e)
        {
            // Awaiting the async Task here is correct for an event handler
            await UpdateAllCharts();
        }

        /// <summary>
        /// NEW: Sets the initial Start and End Date filter values based on the data's timestamp range 
        /// using the currently selected timestamp field.
        /// </summary>
        private void SetInitialDateRange()
        {
            // Reset Chart filter properties
            _chartStartDate = null;
            _chartEndDate = null;
            _chartStartTimeText = string.Empty;
            _chartEndTimeText = string.Empty;

            var entries = LogDataStore.LogEntries;
            if (!entries.Any() || string.IsNullOrEmpty(LogDataStore.SelectedTimestampField)) return;

            string timestampFieldName = LogDataStore.SelectedTimestampField;

            // Collect all successfully parsed dates from the selected column
            var validDates = entries
        .Where(e => e.Fields.ContainsKey(timestampFieldName))
        .Select(e => e.Fields[timestampFieldName])
                // Use the shared TryParseLogDateTime helper
                .Where(v => LogDataStore.TryParseLogDateTime(v, out DateTime _))
        .Select(v => { LogDataStore.TryParseLogDateTime(v, out DateTime dt); return dt; })
        .ToList();

            if (validDates.Any())
            {
                // Get the date part of the earliest and latest entry
                _chartStartDate = validDates.Min().Date;
                _chartEndDate = validDates.Max().Date;

                // Manually notify property change since direct assignment was used
                OnPropertyChanged(nameof(ChartStartDate));
                OnPropertyChanged(nameof(ChartEndDate));
            }
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

        private async void InitializeChartData()
        {
            // 1. Setup Formatter
            Formatter = value => value.ToString("N0");

            // 2. Collections are initialized at property declaration. We skip re-initialization here.

            // 3. Update charts immediately based on the initialized SelectedStatsField
            // CRITICAL: We call UpdateAllCharts here to ensure initial load runs through the new manual process
            if (SelectedStatsField != null)
            {
                await UpdateAllCharts();
            }
        }

        /// <summary>
        /// NEW: Async method to update only the time series chart data.
        /// This is called when the user changes the time range grouping dropdown.
        /// </summary>
        public async Task UpdateOnlyTimeChart() // MODIFIED from async void to async Task
        {
            // Do not run if no timestamp field is selected or if a full calculation is already in progress
            if (string.IsNullOrEmpty(LogDataStore.SelectedTimestampField) || IsCalculating)
            {
                // Clear time chart on insufficient data or if already calculating
                TimeSeriesCollection.Clear();
                TimeLabels = new string[0];
                OnPropertyChanged(nameof(TimeLabels));
                return;
            }

            // Set calculating state to disable time chart controls
            IsCalculating = true;

            try
            {
                // Capture the current filter state to pass to the background thread
                TimeRangeType currentRange = SelectedTimeRange;
                DateTime? currentStartDate = ChartStartDate;
                DateTime? currentEndDate = ChartEndDate;
                string currentStartTimeText = ChartStartTimeText;
                string currentEndTimeText = ChartEndTimeText;

                // Offload the potentially expensive time-based grouping to a background thread
                ObservableCollection<CountItem> timeLogData = await Task.Run(() =>
                {
                    // This runs on a separate thread, calling the static helper method
                    return LogDataStore.GetTimeRangeSummaryCounts(
            currentRange,
            currentStartDate,
            currentEndDate,
            currentStartTimeText,
            currentEndTimeText
        );
                });

                // Update the UI properties on the main thread (automatic after await)
                UpdateTimeSeriesChartUI(timeLogData);
            }
            catch (Exception ex)
            {
                // Handle exceptions gracefully
                Console.WriteLine($"Error updating time chart: {ex.Message}");
            }
            finally
            {
                IsCalculating = false;
            }
        }


        /// <summary>
        /// NEW: Centralized ASYNC update method to fetch data and refresh both charts based on SelectedStatsField.
        /// </summary>
        /// <param name="ignoreTimeChart">Flag to prevent time chart calculation, used when only Pie/Bar are updated.</param>
        public async Task UpdateAllCharts(bool ignoreTimeChart = false)
        {
            if (string.IsNullOrEmpty(SelectedStatsField) || LogDataStore.LogEntries.Count == 0)
            {
                SeriesCollection.Clear();
                PieSeriesCollection.Clear();
                TimeSeriesCollection.Clear();
                Labels = new string[0];
                TimeLabels = new string[0];
                BarChartLegendItems.Clear();
                FilteredLogCount = 0;
                IsAggregated = false; // ** NEW: Reset aggregation flag **
                return;
            }

            // 1. Set UI state to calculating
            IsCalculating = true;

            // NEW FIX: Capture the current UI control states (DependencyObjects)
            // on the UI thread BEFORE starting the background task.
            double sliceThreshold = 0;
            bool isPercentMode = false; // Capture the radio button state safely

            try
            {
                // We must check if the control is loaded to avoid an exception during startup initialization
                if (SliceThresholdSlider.IsLoaded)
                {
                    // Accessing UI controls before Task.Run is safe as this method starts on the UI thread.
                    sliceThreshold = SliceThresholdSlider.Value;

                    // Safely read the RadioButton state on the UI thread
                    isPercentMode = PercentRadio.IsChecked == true;
                }
            }
            catch (Exception ex)
            {
                // If XAML access fails unexpectedly, use defaults and log.
                Console.WriteLine($"Warning: Failed to read UI state before Task.Run: {ex.Message}");
            }


            try
            {
                // Capture the current filter state and selected field to pass to the background thread
                string currentStatsField = SelectedStatsField;
                DateTime? currentStartDate = ChartStartDate;
                DateTime? currentEndDate = ChartEndDate;
                string currentStartTimeText = ChartStartTimeText;
                string currentEndTimeText = ChartEndTimeText;
                TimeRangeType currentRange = SelectedTimeRange;


                // --- 2. Offload Data Fetching and Calculation to a Background Thread (ThreadPool) ---

                // The task will return two data collections (main counts and time counts)
                var (dynamicLogData, timeLogData) = await Task.Run(() =>
                {
                    // A. Fetch main counts (Runs on background thread)
                    ObservableCollection<CountItem> dynamicCounts = LogDataStore.GetDynamicSummaryCounts(
            currentStatsField,
            currentStartDate,
            currentEndDate,
            currentStartTimeText,
            currentEndTimeText
        );

                    // B. Fetch time series counts (Runs on background thread)
                    // Skip if specifically told to ignore, or if no timestamp field is set
                    ObservableCollection<CountItem> timeCounts = new ObservableCollection<CountItem>();
                    if (!ignoreTimeChart && !string.IsNullOrEmpty(LogDataStore.SelectedTimestampField))
                    {
                        timeCounts = LogDataStore.GetTimeRangeSummaryCounts(
                              currentRange,
                              currentStartDate,
                              currentEndDate,
                              currentStartTimeText,
                              currentEndTimeText
                            );
                    }

                    return (dynamicCounts, timeCounts);
                });

                // --- 3. Update UI on the Main Thread (Awaited continuation) ---

                // ** NEW: Check for aggregation (the presence of "Other") **
                IsAggregated = dynamicLogData.Any(c => c.Key.Equals("Other", StringComparison.OrdinalIgnoreCase));

                // Calculate the total number of filtered logs represented in the counts
                FilteredLogCount = (int)dynamicLogData.Sum(c => c.Count);

                // Update Main Charts
                // Pass the captured sliceThreshold and isPercentMode values
                UpdateBarChartUI(dynamicLogData);
                UpdatePieChartUI(dynamicLogData, sliceThreshold, isPercentMode); // MODIFIED

                // Update Time Series Chart (Only if requested)
                if (!ignoreTimeChart)
                {
                    UpdateTimeSeriesChartUI(timeLogData);
                }
            }
            catch (Exception ex)
            {
                // Log and handle exceptions
                Console.WriteLine($"Error during chart calculation: {ex.Message}");
                // This MessageBox call is safe because the catch block runs on the UI thread due to the 'await'
                MessageBox.Show($"An error occurred during chart data calculation: {ex.Message}", "Chart Error", MessageBoxButton.OK, MessageBoxImage.Error);
                IsAggregated = false; // ** NEW: Reset aggregation flag on error **
            }
            finally
            {
                // 4. Reset UI state on the main thread
                IsCalculating = false;
            }
        }

        // --- Core UI Update Methods (Always run on the Main Thread) ---
        // NOTE: The previous GetThreadSafeColor method has been removed, as CountItem.Color is now a thread-safe struct.

        /// <summary>
        /// Updates the Column/Bar Chart UI, must run on the main thread.
        /// ASSUMPTION: CountItem.Color is now a thread-safe System.Windows.Media.Color struct.
        /// </summary>
        /// <param name="logData">The source data collection (ObservableCollection<CountItem>).</param>
        private void UpdateBarChartUI(ObservableCollection<CountItem> logData)
        {
            SeriesCollection.Clear();
            BarChartLegendItems.Clear();

            if (!logData.Any()) return;

            foreach (var item in logData)
            {
                // CRITICAL FIX: We create the UI-thread-owned SolidColorBrush here 
                // using the thread-safe Color struct (item.Color).
                SolidColorBrush uiThreadBrush = new SolidColorBrush(item.Color);

                var series = new ColumnSeries
                {
                    Title = item.Key,
                    Values = new ChartValues<double> { item.Count },
                    Fill = uiThreadBrush, // Use the new UI-thread-owned brush
                    DataLabels = true,
                    LabelPoint = point => point.Y.ToString("N0")
                };

                SeriesCollection.Add(series);

                BarChartLegendItems.Add(new ChartLegendItem
                {
                    Key = item.Key,
                    Color = uiThreadBrush, // Use the new UI-thread-owned brush
                    IsVisible = true,
                    LiveChartSeries = series
                });
            }
        }

        /// <summary>
        /// Updates the Pie Chart UI, must run on the main thread.
        /// ASSUMPTION: CountItem.Color is now a thread-safe System.Windows.Media.Color struct.
        /// </summary>
        /// <param name="logData">The source data collection (ObservableCollection<CountItem>).</param>
        /// <param name="minPercentageThreshold">The minimum percentage for a slice to be displayed.</param>
        /// <param name="isPercentMode">A simple bool flag indicating if percent mode is active.</param>
        private void UpdatePieChartUI(ObservableCollection<CountItem> logData, double minPercentageThreshold, bool isPercentMode)
        {
            PieSeriesCollection.Clear();

            double total = logData.Sum(c => c.Count);
            if (total == 0) return;

            // Use the thread-safe boolean flag captured on the UI thread.
            Func<ChartPoint, string> labelFormatter = point =>
            {
                if (isPercentMode)
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
                    // CRITICAL FIX: We create the UI-thread-owned SolidColorBrush here 
                    // using the thread-safe Color struct (countItem.Color).
                    SolidColorBrush uiThreadBrush = new SolidColorBrush(countItem.Color);

                    PieSeriesCollection.Add(new PieSeries
                    {
                        Title = countItem.Key,
                        Values = new ChartValues<double> { countItem.Count },
                        Fill = uiThreadBrush, // Use the new UI-thread-owned brush
                        DataLabels = true,
                        LabelPoint = labelFormatter
                    });
                }
            }
        }

        /// <summary>
        /// Updates the Time Range Bar Chart UI, must run on the main thread.
        /// ASSUMPTION: CountItem.Color is now a thread-safe System.Windows.Media.Color struct.
        /// </summary>
        /// <param name="timeLogData">The source data collection (ObservableCollection<CountItem>).</param>
        public void UpdateTimeSeriesChartUI(ObservableCollection<CountItem> timeLogData)
        {
            TimeSeriesCollection.Clear();

            if (!timeLogData.Any())
            {
                TimeLabels = new string[0];
                OnPropertyChanged(nameof(TimeLabels));
                return;
            }

            // Extract labels (the formatted time strings)
            TimeLabels = timeLogData.Select(c => c.Key).ToArray();
            OnPropertyChanged(nameof(TimeLabels));

            // Extract values for the single series
            var values = timeLogData.Select(c => c.Count).ToList();

            // CRITICAL FIX: We create the UI-thread-owned SolidColorBrush here 
            // using the thread-safe Color struct (timeLogData.FirstOrDefault()?.Color).
            Color safeColor = timeLogData.FirstOrDefault()?.Color ?? Colors.Black;
            SolidColorBrush uiThreadBrush = new SolidColorBrush(safeColor);

            // The time chart is a single series chart
            var series = new ColumnSeries
            {
                Title = "Events Count",
                Values = new ChartValues<double>(values),
                Fill = uiThreadBrush, // Use the new UI-thread-owned brush
                DataLabels = true,
                LabelPoint = point => point.Y.ToString("N0")
            };

            TimeSeriesCollection.Add(series);
        }

        // --- Event Handlers (MODIFIED for async and UI updating) ---

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

        /// <summary>
        /// Handles RadioButton changes for the Pie Chart label display.
        /// </summary>
        private async void PieLabelMode_Changed(object sender, RoutedEventArgs e) // MODIFIED to async void
        {
            // CRITICAL FIX: Prevent execution if the control is not fully loaded.
            if (!IsLoaded) return;

            // Rerun all charts, but instruct to ignore the heavy time chart calculation.
            // FIX: Added await to resolve CS4014 warning and enforce proper sequencing
            await UpdateAllCharts(ignoreTimeChart: true);
        }

        /// <summary>
        /// Handles Slider changes for the Pie Chart slice threshold.
        /// </summary>
        private async void SliceThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) // MODIFIED to async void
        {
            // CRITICAL FIX: Prevent execution if the control is not fully loaded.
            if (!IsLoaded) return;

            // Update the display text
            // NOTE: The SlicePercentageText TextBox must be defined in the corresponding XAML file.
            // SlicePercentageText.Text = $"Minimum Slice Percentage ({e.NewValue:N0}%)";

            // Rerun all charts, but instruct to ignore the heavy time chart calculation.
            // FIX: Added await to resolve CS4014 warning and enforce proper sequencing
            await UpdateAllCharts(ignoreTimeChart: true);
        }

        // --- INotifyPropertyChanged Implementation ---
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}