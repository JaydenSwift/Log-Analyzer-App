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

        // Helper to safely retrieve a field value, defaulting to "N/A" if not present.
        private string GetValueOrDefault(string key, string defaultValue)
        {
            return Fields.ContainsKey(key) ? Fields[key] : defaultValue;
        }

        // IMPORTANT: All these are now marked as [Browsable(false)] to prevent them 
        // from being auto-generated as columns, forcing the DataGrid to rely on 
        // the columns we manually define in LogAnalyzerView.xaml.cs.

        [Browsable(false)]
        public string Timestamp
        {
            // We still use this getter/setter internally (e.g., for PreviewModel)
            // It relies on the first field name in the dynamic FieldOrder list.
            get => FieldOrder.Any() ? GetValueOrDefault(FieldOrder[0], "N/A") : GetValueOrDefault("Timestamp", "N/A");
            set => Fields[FieldOrder.Any() ? FieldOrder[0] : "Timestamp"] = value;
        }

        [Browsable(false)]
        public string Level
        {
            // It relies on the second field name in the dynamic FieldOrder list.
            get => FieldOrder.Count > 1 ? GetValueOrDefault(FieldOrder[1], "N/A") : GetValueOrDefault("Level", "N/A");
            set => Fields[FieldOrder.Count > 1 ? FieldOrder[1] : "Level"] = value;
        }

        [Browsable(false)]
        public string Message
        {
            // It relies on the third field name in the dynamic FieldOrder list.
            get => FieldOrder.Count > 2 ? GetValueOrDefault(FieldOrder[2], "N/A") : GetValueOrDefault("Message", "N/A");
            set => Fields[FieldOrder.Count > 2 ? FieldOrder[2] : "Message"] = value;
        }
    }
}
