using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq; // Added for Linq usage
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using System.Collections.ObjectModel; // Added for ObservableCollection
using System.Windows.Threading; // Added for Dispatcher
using System.Collections.Specialized; // Added for NotifyCollectionChangedEventArgs

namespace Log_Analyzer_App
{
    // Class to represent a single message in the chat
    public class ChatMessage : INotifyPropertyChanged
    {
        public string Sender { get; set; }
        public string Text { get; set; }
        public Color TextColor { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Interaction logic for ChatAssistantView.xaml
    /// </summary>
    public partial class ChatAssistantView : UserControl, INotifyPropertyChanged
    {
        // --- Gemini API Configuration ---
        private const string GEMINI_API_URL = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-preview-09-2025:generateContent";
        private const string API_KEY = "";

        private readonly HttpClient _httpClient = new HttpClient();

        // --- Properties ---
        public string LoadedFilePath => LogDataStore.CurrentFilePath;

        // Property reflecting the presence of data in the global store
        public bool IsDataLoaded => LogDataStore.LogEntries.Any(); // Check if ANY item exists

        private bool _isLoading = false;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged(nameof(IsLoading));
                    // CRITICAL: Update CanSendMessage when loading state changes
                    OnPropertyChanged(nameof(CanSendMessage));
                }
            }
        }

        private string _currentQueryText = "";
        public string CurrentQueryText
        {
            get => _currentQueryText;
            set
            {
                if (_currentQueryText != value)
                {
                    _currentQueryText = value;
                    OnPropertyChanged(nameof(CurrentQueryText));
                    // Update the CanSendMessage property whenever the text changes
                    OnPropertyChanged(nameof(CanSendMessage));
                }
            }
        }

        // Property to control the Send button enablement
        public bool CanSendMessage => !string.IsNullOrWhiteSpace(CurrentQueryText) && IsDataLoaded && !IsLoading;

        private readonly List<ChatMessage> _chatHistory = new List<ChatMessage>();
        public List<ChatMessage> ChatHistory => _chatHistory;

        // Reference to the global Log Entries
        public ObservableCollection<LogEntry> LogEntries => LogDataStore.LogEntries;


        public ChatAssistantView()
        {
            InitializeComponent();
            this.DataContext = this;

            // Initial message from the assistant
            _chatHistory.Add(new ChatMessage
            {
                Sender = "Assistant",
                Text = "Welcome! Load a log file in the Analyzer tab, and then ask me questions about the dataset here.",
                TextColor = Colors.DarkGray
            });
            OnPropertyChanged(nameof(ChatHistory));

            // Set up event handler to listen for changes in the global store
            // We ensure this only attaches once
            if (!System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            {
                LogDataStore.LogEntries.CollectionChanged += LogEntries_CollectionChanged;
            }

            // Initial setup and state refresh
            RefreshState();
        }

        /// <summary>
        /// Forces notification for all data-related properties.
        /// </summary>
        private void RefreshState()
        {
            // Update UI properties
            OnPropertyChanged(nameof(LoadedFilePath));
            OnPropertyChanged(nameof(IsDataLoaded));
            OnPropertyChanged(nameof(CanSendMessage));

            if (IsDataLoaded)
            {
                // Ensure column setup runs on the UI thread and is based on the final data structure
                // We use DispatcherPriority.Send/Normal to ensure it executes before rendering other things
                Dispatcher.Invoke(() =>
                {
                    SetupDynamicColumns();
                    // Only add the "Data ready" message if the collection was just populated (Count > 0)
                    if (LogDataStore.LogEntries.Count > 0)
                    {
                        AddAssistantMessage($"Data ready! Loaded {LogDataStore.LogEntries.Count} entries from the Log Analyzer. What would you like to know?");
                    }
                }, DispatcherPriority.Send);
            }
            else
            {
                // Clear columns if data is unloaded
                Dispatcher.Invoke(() =>
                {
                    JsonDataGrid.Columns.Clear();
                    // Do not spam chat on startup if no data is present
                    if (_chatHistory.Count > 1)
                    {
                        AddAssistantMessage("Log data cleared. Please load a new file in the Analyzer tab.");
                    }
                }, DispatcherPriority.Send);
            }
        }


        /// <summary>
        /// Handles changes in the global log entries collection (i.e., when a new file is loaded).
        /// CRITICAL FIX: Only call RefreshState on Reset or if the list is empty/populated for the first time.
        /// </summary>
        private void LogEntries_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // The LogAnalyzerView typically clears the collection (Reset) and then repopulates it.
            // We only need to trigger a full state refresh on the Reset action (clear/re-fill complete).
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                // When the collection changes, immediately invoke RefreshState on the UI thread.
                Dispatcher.Invoke(RefreshState, DispatcherPriority.Send);
            }
            else if (e.Action == NotifyCollectionChangedAction.Add && e.NewStartingIndex == 0 && !IsDataLoaded)
            {
                // This handles the initial transition from 0 entries to some entries being added, 
                // which might happen if the LogAnalyzerView uses Add() instead of Reset().
                Dispatcher.Invoke(RefreshState, DispatcherPriority.Send);
            }
        }

        /// <summary>
        /// Sets up the DataGrid columns based on the fields defined in the current pattern.
        /// </summary>
        private void SetupDynamicColumns()
        {
            // Only proceed if we have a pattern definition
            if (LogDataStore.CurrentPatternDefinition == null || !LogDataStore.CurrentPatternDefinition.FieldNames.Any())
            {
                JsonDataGrid.Columns.Clear();
                return;
            }

            // Check if columns already exist and match the expected count
            if (JsonDataGrid.Columns.Count > 0 && JsonDataGrid.Columns.Count == LogDataStore.CurrentPatternDefinition.FieldNames.Count)
            {
                return;
            }

            JsonDataGrid.Columns.Clear();
            List<string> fieldNames = LogDataStore.CurrentPatternDefinition.FieldNames;

            string lastFieldName = fieldNames.LastOrDefault();

            foreach (string fieldName in fieldNames)
            {
                var column = new DataGridTextColumn
                {
                    Header = char.ToUpper(fieldName[0]) + fieldName.Substring(1), // Capitalize for display
                    IsReadOnly = true,
                    // Binding path targets the dictionary entry in LogEntry.Fields
                    Binding = new Binding($"Fields[{fieldName}]"),
                    Width = DataGridLength.Auto
                };

                // Apply width hints for better layout, similar to LogAnalyzerView
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
                    column.MinWidth = 100;
                }
                else
                {
                    column.MinWidth = 100;
                }

                JsonDataGrid.Columns.Add(column);
            }
        }


        /// <summary>
        /// Converts the loaded LogEntry collection to a concise CSV string for the LLM prompt.
        /// </summary>
        /// <returns>A string representation of the data, or an error message.</returns>
        private string EntriesToPromptString()
        {
            if (!IsDataLoaded)
            {
                return "Error: No data loaded.";
            }

            const int maxRowsToSend = 10;
            StringBuilder csv = new StringBuilder();

            // Use the determined field order for the header
            List<string> fieldNames = LogDataStore.CurrentPatternDefinition.FieldNames;

            // 1. Append Header
            csv.AppendLine(string.Join(",", fieldNames));

            // 2. Append Data Rows (up to maxRowsToSend)
            var entries = LogDataStore.LogEntries;
            for (int r = 0; r < Math.Min(entries.Count, maxRowsToSend); r++)
            {
                var entry = entries[r];
                var values = new List<string>();

                foreach (string fieldName in fieldNames)
                {
                    if (entry.Fields.TryGetValue(fieldName, out string value))
                    {
                        // Basic CSV-safe quoting and escaping
                        string safeValue = value.Replace("\"", "\"\"").Replace("\r", "").Replace("\n", "");
                        if (safeValue.Contains(",") || safeValue.Contains("\""))
                        {
                            values.Add($"\"{safeValue}\"");
                        }
                        else
                        {
                            values.Add(safeValue);
                        }
                    }
                    else
                    {
                        values.Add("N/A");
                    }
                }
                csv.AppendLine(string.Join(",", values));
            }

            if (entries.Count > maxRowsToSend)
            {
                csv.AppendLine($"(... {(entries.Count - maxRowsToSend)} more rows exist, providing only sample above)");
            }

            return csv.ToString();
        }

        /// <summary>
        /// Builds the prompt and calls the Gemini API.
        /// </summary>
        private async void AnalyzeData_Click(object sender, RoutedEventArgs e)
        {
            if (!IsDataLoaded || string.IsNullOrWhiteSpace(CurrentQueryText))
            {
                AddAssistantMessage("Please load a log file in the Analyzer tab and enter a valid question.");
                return;
            }

            if (IsLoading) return;

            string userQuery = CurrentQueryText;
            AddUserMessage(userQuery);
            CurrentQueryText = ""; // Clear input box

            IsLoading = true; // Set loading state

            string dataSample = EntriesToPromptString();

            string systemInstruction = "You are an AI Log Analyzer. The user has provided a CSV sample of log data from a larger dataset and a query. Analyze the provided sample data and answer the user's query based on the fields and values you see. Be concise and infer potential patterns or data types. Do not make up data that is not represented by the fields shown.";

            string fullPrompt = $"LOG DATA SAMPLE (Total Rows: {LogDataStore.LogEntries.Count}):\n---\n{dataSample}\n---\nUSER QUERY: {userQuery}";

            AddAssistantMessage("Thinking...", Colors.Gray);

            try
            {
                // Construct the JSON payload for the API
                var payload = new
                {
                    contents = new[]
                    {
                        new { parts = new[] { new { text = fullPrompt } } }
                    },
                    systemInstruction = new { parts = new[] { new { text = systemInstruction } } }
                };

                string jsonPayload = JsonSerializer.Serialize(payload);

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Execute the API call
                string apiUrlWithKey = $"{GEMINI_API_URL}?key={API_KEY}";

                var response = await _httpClient.PostAsync(apiUrlWithKey, content);
                response.EnsureSuccessStatusCode();

                string responseBody = await response.Content.ReadAsStringAsync();

                // Process the API response
                using (JsonDocument doc = JsonDocument.Parse(responseBody))
                {
                    string aiResponseText = doc.RootElement
                        .GetProperty("candidates")[0]
                        .GetProperty("content")
                        .GetProperty("parts")[0]
                        .GetProperty("text")
                        .GetString();

                    UpdateLastAssistantMessage(aiResponseText);
                }
            }
            catch (Exception ex)
            {
                UpdateLastAssistantMessage($"Error communicating with AI: {ex.Message}. Check your API key and network connection.", Colors.Red);
            }
            finally
            {
                IsLoading = false; // Reset loading state
            }
        }

        // --- Helper Methods for Chat Display ---
        private void AddUserMessage(string text)
        {
            _chatHistory.Add(new ChatMessage { Sender = "User", Text = text, TextColor = Colors.DarkBlue });
            OnPropertyChanged(nameof(ChatHistory));
            ScrollToBottom();
        }

        private void AddAssistantMessage(string text, Color? color = null)
        {
            _chatHistory.Add(new ChatMessage
            {
                Sender = "Assistant",
                Text = text,
                TextColor = color ?? Colors.Black
            });
            OnPropertyChanged(nameof(ChatHistory));
            ScrollToBottom();
        }

        private void UpdateLastAssistantMessage(string text, Color? color = null)
        {
            // Find the last message sent by the assistant (useful for updating "Thinking...")
            if (_chatHistory.Count > 0 && _chatHistory[_chatHistory.Count - 1].Sender == "Assistant")
            {
                _chatHistory[_chatHistory.Count - 1].Text = text;
                _chatHistory[_chatHistory.Count - 1].TextColor = color ?? Colors.Black;
                OnPropertyChanged(nameof(ChatHistory));
                ScrollToBottom();
            }
            else
            {
                AddAssistantMessage(text, color);
            }
        }

        private void ScrollToBottom()
        {
            // The Dispatcher is necessary because the ListBox update might not have completed yet.
            Dispatcher.BeginInvoke((Action)(() =>
            {
                if (ChatListView.Items.Count > 0)
                {
                    ChatListView.ScrollIntoView(ChatListView.Items[ChatListView.Items.Count - 1]);
                }
            }));
        }

        // --- INotifyPropertyChanged Implementation ---
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}