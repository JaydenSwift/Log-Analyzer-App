using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions; // Keep this import for placeholder detection
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Log_Analyzer_App
{
    // --- Model for dynamic fields in the Parse Template Builder UI ---
    public class FieldDefinition : INotifyPropertyChanged
    {
        // Placeholder name extracted from the template (e.g., "Timestamp")
        public string PlaceholderName { get; set; }
        // This index is now obsolete but kept for reference to the original index
        public int GroupIndex { get; set; }

        // This is now always "Parsing with Python required for accurate preview"
        public string PreviewValue { get; set; }

        private string _fieldName;
        /// <summary>
        /// This is the final column name the user defines. It defaults to PlaceholderName.
        /// </summary>
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
    /// A modal dialog that allows users to create and test custom parse templates and name the fields.
    /// </summary>
    public partial class PatternBuilderWindow : Window, INotifyPropertyChanged
    {
        // --- Properties for DataContext Binding ---

        // The input log line provided by the main view
        public string LogLinePreview { get; }

        // The parse template being edited by the user (retaining CustomRegex name for XAML compatibility)
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
        public PatternBuilderWindow(string firstLogLine, LogPatternDefinition initialDefinition)
        {
            InitializeComponent();
            this.DataContext = this;

            LogLinePreview = firstLogLine;
            CustomRegex = initialDefinition.Pattern;
            PatternDescription = initialDefinition.Description;

            // Run the test immediately to show initial state
            TestPattern();
        }

        // --- Core Template Testing Logic ---

        /// <summary>
        /// MODIFIED: Attempts to identify placeholder fields ({...}) in the template string.
        /// The previous logic for converting the pattern to Regex and attempting a match in C# is removed
        /// because it was unreliable for the `parse` format. The preview value is now a static message.
        /// </summary>
        private void TestPattern()
        {
            MatchStatusTextBlock.Foreground = Brushes.Gray;
            SaveButton.IsEnabled = false;

            // Store old definitions temporarily to preserve names if field count is the same
            // Use PlaceholderName as the key for comparison
            var oldDefinitions = FieldDefinitions.ToDictionary(f => f.PlaceholderName, f => f.FieldName);
            FieldDefinitions.Clear(); // Clear old definitions

            if (string.IsNullOrWhiteSpace(LogLinePreview))
            {
                MatchStatusTextBlock.Text = "Status: Cannot test, log line preview is empty.";
                return;
            }

            if (string.IsNullOrWhiteSpace(CustomRegex))
            {
                MatchStatusTextBlock.Text = "Status: Enter a parse template above (use {field_name}).";
                return;
            }

            // --- 1. IDENTIFY FIELDS IN TEMPLATE (Find all {placeholder} occurrences) ---
            // Regex to find placeholders, optionally including type specifiers (e.g., {Timestamp} or {Status:d})
            // Capture the name (everything before ':' or '}')
            var placeholderRegex = new Regex(@"\{(?<name>.*?)(?::.*?)?\}");
            var templateMatches = placeholderRegex.Matches(CustomRegex);

            // Create a list of unique field names extracted from the template
            // Field names must be everything before the optional format specifier (e.g., 'Timestamp' from '{Timestamp:S}')
            var uniqueFieldNames = templateMatches
                .Select(m => m.Groups["name"].Value.Split(':')[0].Trim()) // Take only the name part before the colon
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct()
                .ToList();

            // --- 2. POPULATE FIELD DEFINITIONS ---
            if (uniqueFieldNames.Count > 0)
            {
                for (int i = 0; i < uniqueFieldNames.Count; i++)
                {
                    string placeholderName = uniqueFieldNames[i];

                    // The actual C# preview logic is removed as it was unreliable.
                    // Instead, we inform the user that the Python parser will handle it.
                    string previewValue = "Parsing with Python required for preview";

                    string initialName;

                    // If the placeholder name is one of the standard names, use that as the default column name.
                    if (new[] { "Timestamp", "Level", "Message" }.Contains(placeholderName, StringComparer.OrdinalIgnoreCase))
                    {
                        initialName = placeholderName;
                    }
                    else
                    {
                        // Default to the placeholder name
                        initialName = placeholderName;
                    }


                    // Attempt to pre-fill name from the previous state (oldDefinitions)
                    if (oldDefinitions.ContainsKey(placeholderName))
                    {
                        // Use the last-entered user name for this placeholder name
                        initialName = oldDefinitions[placeholderName];
                    }

                    FieldDefinitions.Add(new FieldDefinition
                    {
                        GroupIndex = i + 1, // Retain index for original compatibility, though it's less meaningful now
                        PlaceholderName = placeholderName,
                        FieldName = initialName,
                        PreviewValue = previewValue
                    });
                }

                MatchStatusTextBlock.Text = $"Status: Found {uniqueFieldNames.Count} unique fields. Please confirm column names.";
                MatchStatusTextBlock.Foreground = Brushes.Blue;

                UpdateSaveButtonState();
            }
            else
            {
                MatchStatusTextBlock.Text = "Status: No fields detected. Use curly braces like {Timestamp}.";
                MatchStatusTextBlock.Foreground = Brushes.Orange;
            }
        }

        /// <summary>
        /// Checks if all necessary conditions for saving are met: 
        /// 1. At least one field found. 2. All found fields are named.
        /// </summary>
        private void UpdateSaveButtonState()
        {
            if (FieldDefinitions.Any() && FieldDefinitions.All(f => !string.IsNullOrWhiteSpace(f.FieldName)))
            {
                SaveButton.IsEnabled = true;
                MatchStatusTextBlock.Text = $"Status: READY TO SAVE. Found {FieldDefinitions.Count} fields, all are named.";
                MatchStatusTextBlock.Foreground = Brushes.Blue;
            }
            else
            {
                SaveButton.IsEnabled = false;
                if (FieldDefinitions.Any())
                {
                    MatchStatusTextBlock.Text = $"Status: Please confirm all {FieldDefinitions.Count} field names to enable save.";
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
        private void ParsePatternTextBox_KeyUp(object sender, KeyEventArgs e)
        {
            // Renamed handler to match XAML name
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
                PatternDescription = $"Custom Template: {CustomRegex}";
            }

            // Create the final data structure to return
            FinalPatternDefinition = new LogPatternDefinition
            {
                Pattern = CustomRegex,
                Description = PatternDescription,
                // Ensure field names are returned in the correct order (based on FieldDefinitions ordering)
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