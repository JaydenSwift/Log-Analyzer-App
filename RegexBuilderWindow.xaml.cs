using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions; // Keep this import, but we will avoid using its methods
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
    /// A modal dialog that allows users to create and test custom parse templates and name the fields.
    /// </summary>
    public partial class RegexBuilderWindow : Window, INotifyPropertyChanged
    {
        // --- Properties for DataContext Binding ---

        // The input log line provided by the main view
        public string LogLinePreview { get; }

        // The parse template being edited by the user
        private string _customRegex; // Renamed 'CustomTemplate' mentally, but keep 'CustomRegex' for XAML binding compatibility
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
        public RegexBuilderWindow(string firstLogLine, LogPatternDefinition initialDefinition)
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
        /// Attempts to parse the custom template and identify placeholder fields.
        /// NOTE: This does NOT use the Python parser; it uses C# logic to identify 
        /// the named fields ({...}) in the template string.
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
                MatchStatusTextBlock.Text = "Status: Enter a parse template above (use {...}).";
                return;
            }

            // --- 1. IDENTIFY FIELDS IN TEMPLATE (C# equivalent to parse.compile()._names) ---
            var placeholderRegex = new Regex(@"\{(?<name>.*?)\}");
            var templateMatches = placeholderRegex.Matches(CustomRegex);

            // Create a list of unique field names extracted from the template
            var uniqueFieldNames = templateMatches
                .Select(m => m.Groups["name"].Value.Trim())
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct()
                .ToList();

            // --- 2. PREVIEW LOGIC (STILL USES REGEX TO FAKE PARSING FOR PREVIEW) ---
            // Because C# does not have the 'parse' library, we temporarily convert the template 
            // back to a rough regex for the preview value only.

            // This replacement is very rough and is ONLY for UI preview, not actual parsing.
            string previewRegexPattern = CustomRegex;
            foreach (string name in uniqueFieldNames)
            {
                // Replace named placeholders with a non-greedy capture group
                previewRegexPattern = previewRegexPattern.Replace($"{{{name}}}", "(.*?)");
            }
            // Ensure the entire line is considered for match
            previewRegexPattern = "^" + previewRegexPattern + "$";

            Match previewMatch = null;
            try
            {
                previewMatch = System.Text.RegularExpressions.Regex.Match(LogLinePreview, previewRegexPattern);
            }
            catch (ArgumentException)
            {
                MatchStatusTextBlock.Text = "Status: ERROR - Template generates invalid Regex. Check for misplaced braces/chars.";
                MatchStatusTextBlock.Foreground = Brushes.Red;
                return;
            }

            // --- 3. POPULATE FIELD DEFINITIONS ---
            if (uniqueFieldNames.Count > 0)
            {
                for (int i = 0; i < uniqueFieldNames.Count; i++)
                {
                    string fieldName = uniqueFieldNames[i];
                    string previewValue = "N/A";

                    // Try to get the actual preview value if the rough regex matched
                    // Note: Groups.Count includes group 0, so we check group i + 1
                    if (previewMatch != null && previewMatch.Success && i + 1 < previewMatch.Groups.Count)
                    {
                        previewValue = previewMatch.Groups[i + 1].Value.Trim();
                    }

                    string initialName = "";

                    // Attempt to pre-fill name from the previous state (oldDefinitions)
                    if (oldDefinitions.ContainsKey(i + 1))
                    {
                        initialName = oldDefinitions[i + 1];
                    }
                    else
                    {
                        // Apply default names based on position
                        if (i == 0) initialName = "Timestamp";
                        else if (i == 1) initialName = "Level";
                        else if (i == 2) initialName = "Message";
                        else initialName = fieldName; // Use the placeholder name as default
                    }

                    FieldDefinitions.Add(new FieldDefinition
                    {
                        GroupIndex = i + 1,
                        FieldName = initialName,
                        PreviewValue = previewValue
                    });
                }

                MatchStatusTextBlock.Text = $"Status: Found {uniqueFieldNames.Count} unique fields. Please name them all.";
                MatchStatusTextBlock.Foreground = Brushes.Blue;

                UpdateSaveButtonState();
            }
            else
            {
                MatchStatusTextBlock.Text = "Status: No fields detected. Use braces like {Timestamp}.";
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
                    MatchStatusTextBlock.Text = $"Status: Please name all {FieldDefinitions.Count} fields to enable save.";
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
