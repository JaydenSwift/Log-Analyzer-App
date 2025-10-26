using System;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
// FIX: The 'using MaterialDesignThemes.Wpf' is kept for other potential uses.
using MaterialDesignThemes.Wpf;

namespace Log_Analyzer_App
{
    /// <summary>
    /// Interaction logic for RegexBuilderWindow.xaml
    /// A modal dialog that allows users to create and test custom regex patterns.
    /// </summary>
    public partial class RegexBuilderWindow : Window, INotifyPropertyChanged
    {
        // --- Properties for DataContext Binding ---

        // The input log line provided by the main view
        public string LogLinePreview { get; }

        // The regex pattern being edited by the user
        private string _customRegex;
        public string CustomRegex
        {
            get => _customRegex;
            set { _customRegex = value; OnPropertyChanged(nameof(CustomRegex)); }
        }

        // The user-provided description for the pattern
        private string _patternDescription;
        public string PatternDescription
        {
            get => _patternDescription;
            set { _patternDescription = value; OnPropertyChanged(nameof(PatternDescription)); }
        }

        // The result of the parsing test against the log line
        private LogEntry _parsedResult;
        public LogEntry ParsedResult
        {
            get => _parsedResult;
            set { _parsedResult = value; OnPropertyChanged(nameof(ParsedResult)); }
        }

        // --- Constructor and Initialization ---

        /// <summary>
        /// Initializes the modal with the current log pattern and the first log line.
        /// </summary>
        /// <param name="firstLogLine">The first non-empty line of the log file.</param>
        /// <param name="initialPattern">The currently active pattern (default or last custom).</param>
        /// <param name="initialDescription">The current description.</param>
        public RegexBuilderWindow(string firstLogLine, string initialPattern, string initialDescription)
        {
            InitializeComponent();

            // Set initial state from existing values
            LogLinePreview = firstLogLine;
            CustomRegex = initialPattern;
            PatternDescription = initialDescription;

            // Initialize the result object
            ParsedResult = new LogEntry();

            // Run the test immediately to show initial state
            TestPattern();
        }

        // --- Core Regex Testing Logic ---

        /// <summary>
        /// Tests the current custom regex against the log line preview in real-time.
        /// </summary>
        private void TestPattern()
        {
            // Reset state
            ParsedResult.Timestamp = "N/A";
            ParsedResult.Level = "N/A";
            ParsedResult.Message = "N/A";
            MatchStatusTextBlock.Foreground = System.Windows.Media.Brushes.Gray;
            SaveButton.IsEnabled = false;

            if (string.IsNullOrWhiteSpace(LogLinePreview))
            {
                MatchStatusTextBlock.Text = "Status: Cannot test, log line preview is empty.";
                return;
            }

            if (string.IsNullOrWhiteSpace(CustomRegex))
            {
                MatchStatusTextBlock.Text = "Status: Enter a regex pattern above.";
                return;
            }

            try
            {
                var regex = new Regex(CustomRegex);
                Match match = regex.Match(LogLinePreview);

                // Group 0 is the full match. We need groups 1, 2, and 3 for the fields.
                if (match.Success && match.Groups.Count >= 4)
                {
                    // Update the INPC properties
                    ParsedResult.Timestamp = match.Groups[1].Value.Trim();
                    ParsedResult.Level = match.Groups[2].Value.Trim();
                    ParsedResult.Message = match.Groups[3].Value.Trim();

                    // Force UI update for nested properties (since LogEntry is a reference type)
                    OnPropertyChanged(nameof(ParsedResult));

                    MatchStatusTextBlock.Text = "Status: SUCCESS! All 3 fields matched.";
                    // CRITICAL FIX: Use Application.Current.FindResource to guarantee finding the brush defined in App.xaml
                    MatchStatusTextBlock.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("SecondaryMidBrush");
                    SaveButton.IsEnabled = true; // Enable saving only on successful 3-group match
                }
                else
                {
                    // Handle partial matches or no match
                    if (match.Success)
                    {
                        MatchStatusTextBlock.Text = $"Status: Match found, but only {match.Groups.Count - 1} capture groups found. Need 3.";
                        // CRITICAL FIX: Use Application.Current.FindResource
                        MatchStatusTextBlock.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("WarningBrush");
                    }
                    else
                    {
                        MatchStatusTextBlock.Text = "Status: NO MATCH found on the log line.";
                        // CRITICAL FIX: Use Application.Current.FindResource
                        MatchStatusTextBlock.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("ErrorBrush");
                    }
                }
            }
            catch (ArgumentException)
            {
                // Invalid regex pattern compilation error
                MatchStatusTextBlock.Text = "Status: ERROR - Invalid Regex Syntax (check syntax).";
                // CRITICAL FIX: Use Application.Current.FindResource
                MatchStatusTextBlock.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("ErrorBrush");
            }
        }

        // --- Event Handlers ---

        /// <summary>
        /// Triggers the pattern test when the user clicks the Test button.
        /// </summary>
        private void TestPattern_Click(object sender, RoutedEventArgs e)
        {
            TestPattern();
        }

        /// <summary>
        /// Allows testing on key up to provide real-time feedback, but only for the Enter key 
        /// to prevent too many tests during typing.
        /// </summary>
        private void RegexPatternTextBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TestPattern();
            }
        }

        /// <summary>
        /// Saves the pattern and closes the dialog with success result.
        /// </summary>
        private void SaveAndUsePattern_Click(object sender, RoutedEventArgs e)
        {
            // The button is only enabled if the pattern is valid and produces 3 groups.
            // We ensure a description is also present, or use a default one.
            if (string.IsNullOrWhiteSpace(PatternDescription))
            {
                PatternDescription = $"Custom Pattern: {CustomRegex}";
            }

            this.DialogResult = true;
            this.Close();
        }

        /// <summary>
        /// Closes the dialog without saving the pattern.
        /// </summary>
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        // --- INotifyPropertyChanged Implementation ---
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
