import sys
import re
import json
import os
from itertools import islice

# --- Configuration ---
# Number of lines to check for robust pattern suggestion
ROBUST_CHECK_LINES = 5
# ---------------------

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

# --- Pattern Suggestion Logic (UPDATED FOR ROBUSTNESS) ---

def suggest_robust_pattern(file_path):
    """
    Attempts to find the best matching regex pattern by checking the first 
    ROBUST_CHECK_LINES of the log file.
    """
    if not file_path:
        # Return the most standard pattern if no file path is given
        return COMMON_LOG_PATTERNS[0]
        
    try:
        # Read the first N non-empty lines from the file
        log_lines_to_check = []
        with open(file_path, 'r') as f:
            for line in f:
                line = line.strip()
                if line:
                    log_lines_to_check.append(line)
                
                if len(log_lines_to_check) >= ROBUST_CHECK_LINES:
                    break
        
        if not log_lines_to_check:
            # If the file is empty or only contains whitespace
            return COMMON_LOG_PATTERNS[0]

        # Iterate through all patterns and check if they match ALL collected lines
        for pattern_def in COMMON_LOG_PATTERNS:
            try:
                pattern = pattern_def["pattern"]
                regex = re.compile(pattern)
                # The expected number of groups is the number of field names
                expected_groups = len(pattern_def["field_names"])
                
                all_lines_matched = True
                
                for line in log_lines_to_check:
                    match = regex.match(line)
                    
                    # If it fails to match OR the group count is wrong, this pattern fails
                    # Note: match.groups().Count includes Group 0 (full match), so we use len(match.groups()) for the capture groups count.
                    if not (match and len(match.groups()) == expected_groups):
                        all_lines_matched = False
                        break
                
                if all_lines_matched:
                    # Found the most robust match, return its definition
                    return pattern_def
                    
            except re.error:
                # Skip invalid patterns 
                continue
                
        # If no pattern matches all lines, return the last pattern (the most generic one)
        return COMMON_LOG_PATTERNS[-1]

    except Exception:
        # If file reading fails or another unexpected error occurs, fall back gracefully
        return COMMON_LOG_PATTERNS[-1]


# --- Core Parsing Logic (UPDATED FOR FORCED PARSING) ---

def parse_log_file(file_path, log_pattern, field_names, is_best_effort=False):
    """
    Reads a log file, parses each line using the provided regex, 
    and returns a list of log entries where each entry contains a dictionary 
    of user-defined fields.
    
    If is_best_effort is True (Default Button), matching is forced. If 
    parsing fails (due to a non-conforming line), the whole line is placed 
    in the 'Message' field.
    """
    parsed_entries = []
    
    total_lines = 0
    parsed_lines_count = 0

    try:
        LOG_PATTERN_REGEX = re.compile(log_pattern)
        # Dynamic: The expected groups are determined by the field names
        expected_groups = len(field_names)

        # Determine the name of the catch-all field (the last field name)
        catch_all_field_name = field_names[-1] if field_names else "Message"
        
        # Determine the field names that aren't the catch-all field
        other_field_names = field_names[:-1] if field_names else []


        with open(file_path, 'r') as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                
                total_lines += 1 # Count non-empty lines

                # Attempt a match for both cases
                match = LOG_PATTERN_REGEX.match(line)
                
                entry = {
                    "Fields": {},
                    "FieldOrder": field_names 
                }

                is_matched_and_extracted = False
                
                if is_best_effort:
                    # --- FORCED PARSING (Default Button) ---
                    # We only need the match object to attempt extraction.
                    # We wrap the extraction in try/except to catch IndexErrors or AttributeErrors 
                    # if the match object is None or has too few groups.
                    try:
                        # Attempt to extract groups based on expected_groups count
                        if match and len(match.groups()) == expected_groups:
                            for i in range(expected_groups):
                                field_name = field_names[i]
                                field_value = match.group(i + 1).strip()
                                entry["Fields"][field_name] = field_value
                            
                            parsed_lines_count += 1
                            is_matched_and_extracted = True
                        else:
                            # If match failed or group count mismatch, fall through to forced entry creation
                            raise ValueError("Forced fallback due to match failure or group count mismatch.")
                        
                    except (AttributeError, IndexError, ValueError):
                        # Attribute Error if match is None, Index Error if group count is wrong, ValueError from explicit raise
                        # --- UNMATCHED LINE / BEST-EFFORT LOGIC (The Forced Fallback) ---
                        
                        # Set all non-catch-all fields to N/A
                        for field_name in other_field_names:
                            entry["Fields"][field_name] = "N/A"
                        
                        # Put the entire line content into the designated catch-all field (usually Message)
                        entry["Fields"][catch_all_field_name] = f"[UNPARSED] {line}"
                        
                        # Set generic level and timestamp if they are the first two fields
                        if len(field_names) > 0 and field_names[0].lower().startswith('timestamp'):
                            entry["Fields"][field_names[0]] = "N/A"
                        if len(field_names) > 1 and field_names[1].lower().startswith('level'):
                            entry["Fields"][field_names[1]] = "UNRECOGNIZED"

                    # Every line processed in best_effort mode is added immediately
                    parsed_entries.append(entry)

                else:
                    # --- STRICT PARSING (Custom Regex Button) ---
                    if match and len(match.groups()) == expected_groups:
                        # --- SUCCESSFUL MATCH ---
                        for i in range(expected_groups):
                            field_name = field_names[i]
                            field_value = match.group(i + 1).strip()
                            entry["Fields"][field_name] = field_value

                        parsed_lines_count += 1
                        is_matched_and_extracted = True
                        parsed_entries.append(entry) # Add only on success

                    elif total_lines > 0 and parsed_lines_count == 0:
                        # If strict mode and zero lines matched, return error (this is the logic for the Custom button)
                         return {
                            "success": False,
                            "error": f"The pattern matched 0 of {total_lines} lines. This strict check is performed for custom patterns. Please verify your regex or use the default pattern for best-effort parsing."
                        }

        # Since we guarantee every line is added in best-effort mode, the logic below simplifies.
        # Strict mode will only succeed if parsed_entries is populated (i.e., parsed_lines_count > 0).

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
        # This error is critical and must be returned, as it means the regex pattern itself is invalid.
        return {
            "success": False,
            "error": f"Invalid Regex Pattern provided: {str(e)}"
        }
    except Exception as e:
        # Catch-all for other unexpected issues
        return {
            "success": False, 
            "error": f"An unexpected error occurred during parsing: {str(e)}"
        }

# --- Main Entry Point (UNCHANGED) ---

if __name__ == "__main__":
    if len(sys.argv) < 2:
        error_output = json.dumps({
            "success": False, 
            "error": "Error: Missing command. (Requires: parse or suggest_robust_pattern)"
        })
        print(error_output)
        sys.exit(1)

    command = sys.argv[1]
    
    if command == "parse":
        if len(sys.argv) < 6:
            error_output = json.dumps({
                "success": False, 
                "error": "Error: Missing arguments for 'parse'. (Requires: file_path, log_pattern, field_names_json, and is_best_effort)"
            })
            print(error_output)
            sys.exit(1)

        file_path = sys.argv[2]
        log_pattern = sys.argv[3]
        field_names_json = sys.argv[4]
        # NEW: Read the best-effort flag (passed as "true" or "false" string)
        is_best_effort_str = sys.argv[5].lower()
        is_best_effort = is_best_effort_str == "true"
        
        try:
            field_names = json.loads(field_names_json)
        except json.JSONDecodeError:
            error_output = json.dumps({
                "success": False, 
                "error": "Error: Failed to decode field names JSON argument."
            })
            print(error_output)
            sys.exit(1)

        result = parse_log_file(file_path, log_pattern, field_names, is_best_effort)
    
    elif command == "suggest_robust_pattern":
        # NEW command implementation: takes the file path and returns the best pattern
        if len(sys.argv) < 3:
            error_output = json.dumps({
                "success": False, 
                "error": "Error: Missing argument for 'suggest_robust_pattern'. (Requires: file_path)"
            })
            print(error_output)
            sys.exit(1)

        file_path = sys.argv[2]
        
        # Get the suggested pattern (which is a dictionary)
        suggested_def = suggest_robust_pattern(file_path)
        
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
