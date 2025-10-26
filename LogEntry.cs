using System.Collections.Generic;
using System.Linq;
using System.ComponentModel; // Required for [Browsable(false)]

namespace Log_Analyzer_App
{
    /// <summary>
    /// Represents a single log line entry. 
    /// The fields are stored dynamically based on the user's custom regex capture groups.
    /// </summary>
    public class LogEntry
    {
        // --- Internal Data Structures (Hidden from DataGrid) ---

        // [Browsable(false)] hides these properties from DataGrid.AutoGenerateColumns
        [Browsable(false)]
        public Dictionary<string, string> Fields { get; set; } = new Dictionary<string, string>();

        [Browsable(false)]
        public List<string> FieldOrder { get; set; } = new List<string>();

        // --- Derived Properties for Backwards Compatibility and Preview ---

        // These are also hidden now. The columns for T, L, M will be created dynamically 
        // using the Fields dictionary, based on the FieldOrder list.

        // Helper to safely retrieve a field value, defaulting to "N/A" if not present.
        private string GetValueOrDefault(string key, string defaultValue)
        {
            return Fields.ContainsKey(key) ? Fields[key] : defaultValue;
        }

        [Browsable(false)]
        public string Timestamp
        {
            // We still use this getter/setter internally (e.g., for PreviewModel)
            get => FieldOrder.Any() ? GetValueOrDefault(FieldOrder[0], "N/A") : GetValueOrDefault("Timestamp", "N/A");
            set => Fields["Timestamp"] = value;
        }

        [Browsable(false)]
        public string Level
        {
            get => FieldOrder.Count > 1 ? GetValueOrDefault(FieldOrder[1], "N/A") : GetValueOrDefault("Level", "N/A");
            set => Fields["Level"] = value;
        }

        [Browsable(false)]
        public string Message
        {
            get => FieldOrder.Count > 2 ? GetValueOrDefault(FieldOrder[2], "N/A") : GetValueOrDefault("Message", "N/A");
            set => Fields["Message"] = value;
        }
    }
}
