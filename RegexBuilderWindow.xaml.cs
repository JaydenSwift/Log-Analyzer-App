using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Log_Analyzer_App
{
    // --- Model for dynamic fields in the Regex Builder UI ---
    public class FieldDefinition : INotifyPropertyChanged
    {
        public int GroupIndex { get; set; }
        public string PreviewValue { get; set; }

        private string _fieldName;
        public string FieldName
        {
            get => _fieldName;
            set { _fieldName = value; OnPropertyChanged(nameof(FieldName)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Interaction logic for RegexBuilderWindow.xaml
    /// A modal dialog that allows users to create and test custom regex patterns and name the fields.
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

        // The collection that drives the dynamic column naming UI
        public ObservableCollection<FieldDefinition> FieldDefinitions { get; set; } = new ObservableCollection<FieldDefinition>();

        // Properties to be returned to the main view on save
        public LogPatternDefinition FinalPatternDefinition { get; private set; }

        // --- Constructor and Initialization ---

        /// <summary>
        /// Initializes the modal with the current log pattern and the first log line.
        /// </summary>
        /// <param name="firstLogLine">The first non-empty line of the log file.</param>
        /// <param name="initialDefinition">The currently active pattern and field names.</param>
        public RegexBuilderWindow(string firstLogLine, LogPatternDefinition initialDefinition)
        {
            InitializeComponent();
            this.DataContext = this;

            LogLinePreview = firstLogLine;
            CustomRegex = initialDefinition.Pattern;
            PatternDescription = initialDefinition.Description;

            // Initialize the FieldDefinitions from the initial data's field names
            // This ensures that if the user clicks "Custom Pattern" after using the default,
            // the fields are pre-populated with "Timestamp", "Level", and "Message".
            for (int i = 0; i < initialDefinition.FieldNames.Count; i++)
            {
                FieldDefinitions.Add(new FieldDefinition
                {
                    GroupIndex = i + 1,
                    FieldName = initialDefinition.FieldNames[i],
                    PreviewValue = "N/A" // Will be updated by TestPattern()
                });
            }

            // Run the test immediately to show initial state
            TestPattern();
        }

        // --- Core Regex Testing Logic ---

        /// <summary>
        /// Tests the current custom regex against the log line preview in real-time.
        /// </summary>
        private void TestPattern()
        {
            MatchStatusTextBlock.Foreground = Brushes.Gray;
            SaveButton.IsEnabled = false;

            // Store old definitions temporarily to preserve names if group count is the same
            var oldDefinitions = FieldDefinitions.ToDictionary(f => f.GroupIndex, f => f.FieldName);
            FieldDefinitions.Clear(); // Clear old definitions

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

                // Group 0 is the full match, so we iterate from 1 up.
                int totalCaptureGroups = match.Groups.Count - 1;

                if (!match.Success)
                {
                    MatchStatusTextBlock.Text = "Status: NO MATCH found on the log line.";
                    MatchStatusTextBlock.Foreground = Brushes.Red;
                    return;
                }

                // Populate FieldDefinitions list with detected groups and their preview values
                for (int i = 1; i <= totalCaptureGroups; i++)
                {
                    string initialName = "";

                    // Attempt to pre-fill name from the previous state (oldDefinitions)
                    if (oldDefinitions.ContainsKey(i))
                    {
                        initialName = oldDefinitions[i];
                    }
                    // Apply default names for the first few columns if not already named
                    else if (i == 1) initialName = "Timestamp";
                    else if (i == 2) initialName = "Level";
                    else if (i == 3) initialName = "Message";
                    else initialName = $"Column {i}";


                    FieldDefinitions.Add(new FieldDefinition
                    {
                        GroupIndex = i,
                        FieldName = initialName,
                        PreviewValue = match.Groups[i].Value.Trim()
                    });
                }

                // Check for success conditions: at least one group and all groups named
                if (totalCaptureGroups > 0)
                {
                    MatchStatusTextBlock.Text = $"Status: SUCCESS! Found {totalCaptureGroups} capture groups. Please name them all.";
                    MatchStatusTextBlock.Foreground = Brushes.Blue;

                    // The logic for SaveButton.IsEnabled now moves to a dedicated check
                    UpdateSaveButtonState();
                }
                else
                {
                    MatchStatusTextBlock.Text = "Status: Match found, but NO CAPTURE GROUPS detected (need parentheses '()').";
                    MatchStatusTextBlock.Foreground = Brushes.Orange;
                }

            }
            catch (ArgumentException)
            {
                // Invalid regex pattern compilation error
                MatchStatusTextBlock.Text = "Status: ERROR - Invalid Regex Syntax (check syntax).";
                MatchStatusTextBlock.Foreground = Brushes.Red;
            }
        }

        /// <summary>
        /// Checks if all necessary conditions for saving are met: 
        /// 1. Regex is valid and matched. 2. At least one group found. 3. All found groups are named.
        /// </summary>
        private void UpdateSaveButtonState()
        {
            if (FieldDefinitions.Any() && FieldDefinitions.All(f => !string.IsNullOrWhiteSpace(f.FieldName)))
            {
                SaveButton.IsEnabled = true;
                MatchStatusTextBlock.Text = $"Status: READY TO SAVE. Found {FieldDefinitions.Count} groups, all are named.";
                MatchStatusTextBlock.Foreground = Brushes.Blue;
            }
            else
            {
                SaveButton.IsEnabled = false;
                if (FieldDefinitions.Any())
                {
                    MatchStatusTextBlock.Text = $"Status: Please name all {FieldDefinitions.Count} capture groups to enable save.";
                    MatchStatusTextBlock.Foreground = Brushes.Orange;
                }
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
            // Also check on any key up to ensure naming changes update the Save button state
            UpdateSaveButtonState();
        }

        /// <summary>
        /// Saves the pattern and closes the dialog with success result.
        /// </summary>
        private void SaveAndUsePattern_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveButton.IsEnabled) return;

            // Ensure a description is present
            if (string.IsNullOrWhiteSpace(PatternDescription))
            {
                PatternDescription = $"Custom Pattern: {CustomRegex}";
            }

            // Create the final data structure to return
            FinalPatternDefinition = new LogPatternDefinition
            {
                Pattern = CustomRegex,
                Description = PatternDescription,
                FieldNames = FieldDefinitions.Select(f => f.FieldName.Trim()).ToList()
            };

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
