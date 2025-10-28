import sys
import re
import json
import os

# --- Pattern Suggestion Data Loader ---

# Define a fallback if the external file cannot be loaded
FALLBACK_LOG_PATTERNS = [
    {
        "pattern": r"^\[(\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2})\]\s*(INFO|WARN|ERROR|DEBUG|TRACE):\s*(.*)$",
        "description": "FALLBACK: Standard [Timestamp] LEVEL: Message",
        "field_names": ["Timestamp", "Level", "Message"]
    },
    {
        "pattern": r"^(\S+)\s+(\S+)\s+(.*)$",
        "description": "FALLBACK: Generic - First two words as fields, rest as message.",
        "field_names": ["Token1", "Token2", "Message"]
    }
]

def load_patterns():
    """Loads COMMON_LOG_PATTERNS from the external patterns.json file."""
    try:
        # Determine the path of the patterns.json file relative to the script's location
        script_dir = os.path.dirname(os.path.abspath(__file__))
        patterns_path = os.path.join(script_dir, "patterns.json")
        
        with open(patterns_path, 'r') as f:
            patterns = json.load(f)
            if isinstance(patterns, list) and all(isinstance(p, dict) for p in patterns):
                print(f"DEBUG: Successfully loaded {len(patterns)} patterns from patterns.json", file=sys.stderr)
                return patterns
            else:
                print("ERROR: patterns.json loaded, but data format is invalid. Using fallback.", file=sys.stderr)
                return FALLBACK_LOG_PATTERNS
                
    except FileNotFoundError:
        print("ERROR: patterns.json not found. Using fallback patterns.", file=sys.stderr)
        return FALLBACK_LOG_PATTERNS
    except json.JSONDecodeError:
        print("ERROR: patterns.json contains invalid JSON. Using fallback patterns.", file=sys.stderr)
        return FALLBACK_LOG_PATTERNS
    except Exception as e:
        print(f"ERROR: Unexpected error loading patterns.json: {e}. Using fallback patterns.", file=sys.stderr)
        return FALLBACK_LOG_PATTERNS


# Load patterns once when the script starts
COMMON_LOG_PATTERNS = load_patterns()

# --- Pattern Suggestion Logic ---

def suggest_pattern(log_line):
    """
    Attempts to find the best matching regex pattern from predefined heuristics 
    for a single log line.
    """
    if not log_line:
        # Return the first pattern if the line is empty (often the most standard one)
        return COMMON_LOG_PATTERNS[0]
        
    for pattern_def in COMMON_LOG_PATTERNS:
        try:
            pattern = pattern_def["pattern"]
            regex = re.compile(pattern)
            match = regex.match(log_line)
            
            # Check if the pattern matches and if it extracts the expected number of groups
            if match and len(match.groups()) == len(pattern_def["field_names"]):
                # Found the best match, return its definition
                return pattern_def
                
        except re.error:
            # Skip invalid patterns 
            continue
            
    # If no pattern matches, return the last pattern (the most generic one)
    return COMMON_LOG_PATTERNS[-1]


# --- Core Parsing Logic ---

def parse_log_file(file_path, log_pattern, field_names):
    """
    Reads a log file, parses each line using the provided regex, 
    and returns a list of log entries where each entry contains a dictionary 
    of user-defined fields.
    """
    parsed_entries = []
    
    try:
        LOG_PATTERN_REGEX = re.compile(log_pattern)
        expected_groups = len(field_names)

        with open(file_path, 'r') as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue

                match = LOG_PATTERN_REGEX.match(line)
                
                if match:
                    if len(match.groups()) == expected_groups:
                        entry = {
                            "Fields": {},
                            "FieldOrder": field_names 
                        }
                        
                        for i in range(expected_groups):
                            field_name = field_names[i]
                            field_value = match.group(i + 1).strip()
                            entry["Fields"][field_name] = field_value

                        parsed_entries.append(entry)
                    else:
                         # If the regex matches but doesn't have the expected number of capture groups, ignore the line
                        continue

        return {
            "success": True, 
            "data": parsed_entries
        }
        
    except FileNotFoundError:
        return {
            "success": False, 
            "error": f"Error: The file was not found at path: {file_path}"
        }
    except re.error as e:
        return {
            "success": False,
            "error": f"Invalid Regex Pattern provided: {str(e)}"
        }
    except Exception as e:
        return {
            "success": False, 
            "error": f"An unexpected error occurred during parsing: {str(e)}"
        }

# --- Main Entry Point ---

if __name__ == "__main__":
    if len(sys.argv) < 2:
        error_output = json.dumps({
            "success": False, 
            "error": "Error: Missing command. (Requires: parse or suggest_pattern)"
        })
        print(error_output)
        sys.exit(1)

    command = sys.argv[1]
    
    if command == "parse":
        if len(sys.argv) < 5:
            error_output = json.dumps({
                "success": False, 
                "error": "Error: Missing arguments for 'parse'. (Requires: file_path, log_pattern, and field_names_json)"
            })
            print(error_output)
            sys.exit(1)

        file_path = sys.argv[2]
        log_pattern = sys.argv[3]
        field_names_json = sys.argv[4]
        
        try:
            field_names = json.loads(field_names_json)
        except json.JSONDecodeError:
            error_output = json.dumps({
                "success": False, 
                "error": "Error: Failed to decode field names JSON argument."
            })
            print(error_output)
            sys.exit(1)

        result = parse_log_file(file_path, log_pattern, field_names)
    
    elif command == "suggest_pattern":
        if len(sys.argv) < 3:
            error_output = json.dumps({
                "success": False, 
                "error": "Error: Missing argument for 'suggest_pattern'. (Requires: log_line)"
            })
            print(error_output)
            sys.exit(1)

        log_line = sys.argv[2]
        
        # Get the suggested pattern (which is a dictionary)
        suggested_def = suggest_pattern(log_line)
        
        # Return the definition dictionary
        result = {
            "success": True, 
            "data": suggested_def
        }
    
    else:
        result = {
            "success": False,
            "error": f"Unknown command: {command}"
        }

    # Print the JSON result to stdout for the C# application to read
    print(json.dumps(result))
    
    sys.exit(0)
