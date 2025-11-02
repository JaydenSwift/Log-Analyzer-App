import sys
import parse
import json
import os
from itertools import islice

# --- Configuration ---
# Number of lines to check for robust pattern suggestion
ROBUST_CHECK_LINES = 5
# ---------------------

# --- Pattern Suggestion Data Loader ---

def load_patterns():
    """
    Loads COMMON_LOG_PATTERNS from the external patterns.json file.
    If loading fails, it returns a minimal pattern list.
    """
    # Define a guaranteed valid minimal pattern for fallback situations
    MINIMAL_DEFAULT_PATTERN = [{
        "pattern": "{Token1} {Message}",
        "description": "Minimal Default (File/Format Error Fallback)",
        "field_names": ["Token1", "Message"]
    }]

    try:
        # Get the directory of the *currently executing script file* regardless of the CWD.
        script_dir = os.path.dirname(os.path.abspath(__file__))
        patterns_path = os.path.join(script_dir, "patterns.json")
        
        if not os.path.exists(patterns_path):
            print(f"ERROR: patterns.json not found at expected path: {patterns_path}. Returning minimal default.", file=sys.stderr)
            return MINIMAL_DEFAULT_PATTERN

        with open(patterns_path, 'r') as f:
            patterns = json.load(f)
            
            if isinstance(patterns, list) and all(isinstance(p, dict) for p in patterns):
                if not patterns:
                    print("WARNING: patterns.json is empty. Providing minimal default pattern.", file=sys.stderr)
                    return MINIMAL_DEFAULT_PATTERN
                    
                return patterns
            else:
                print("ERROR: patterns.json loaded, but data format is invalid (not a list of objects). Returning minimal default.", file=sys.stderr)
                return MINIMAL_DEFAULT_PATTERN
                
    except json.JSONDecodeError:
        print("ERROR: patterns.json contains invalid JSON. Returning minimal default.", file=sys.stderr)
        return MINIMAL_DEFAULT_PATTERN
    except Exception as e:
        error_type = type(e).__name__
        print(f"ERROR: Unexpected error during pattern loading ({error_type}). Returning minimal default pattern.", file=sys.stderr)
        return MINIMAL_DEFAULT_PATTERN


# Load patterns once when the script starts
COMMON_LOG_PATTERNS = load_patterns()

# --- Pattern Suggestion Logic (UPDATED for 'parse') ---

def suggest_robust_pattern(file_path):
    """
    Finds the best matching parse template by checking the first 
    ROBUST_CHECK_LINES of the log file using a scoring system.
    """
    if not COMMON_LOG_PATTERNS:
        return {
            "pattern": "{Token1} {Message}",
            "description": "Critical Error Fallback",
            "field_names": ["Token1", "Message"]
        }

    initial_best_def = COMMON_LOG_PATTERNS[0]
    
    if not file_path:
        return initial_best_def
        
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
            return initial_best_def
            
        best_pattern_def = initial_best_def
        highest_score = 0
        max_capture_groups = len(initial_best_def["field_names"])

        # Iterate through all patterns and score them
        for pattern_def in COMMON_LOG_PATTERNS:
            try:
                # Use the 'parse' library to compile the template
                # NOTE: We use fuzzy=True here to ensure the suggestion logic is also highly tolerant of spacing
                template = parse.compile(pattern_def["pattern"], fuzzy=True)
                
                expected_groups = len(pattern_def["field_names"])
                current_score = 0
                
                for line in log_lines_to_check:
                    result = template.parse(line)
                    
                    # A match is successful if 'result' is not None and the number of parsed items matches expected
                    if result is not None and len(result.named) == expected_groups:
                        current_score += 1
                
                # --- Scoring & Tie-Breaking Logic ---
                if current_score > highest_score:
                    highest_score = current_score
                    max_capture_groups = expected_groups
                    best_pattern_def = pattern_def
                elif current_score == highest_score and expected_groups > max_capture_groups:
                    max_capture_groups = expected_groups
                    best_pattern_def = pattern_def
                    
            except Exception:
                # Catch invalid parse templates
                continue
                
        return best_pattern_def

    except Exception as e:
        print(f"ERROR: Unexpected error during pattern suggestion: {e}. Defaulting to first pattern.", file=sys.stderr)
        return initial_best_def


# --- Core Parsing Logic (UPDATED for 'parse' and Forgiveness) ---

def parse_log_file(file_path, log_pattern, field_names, is_best_effort=False):
    """
    Reads a log file, parses each line using the provided parse template.
    Uses fuzzy matching for maximum forgiveness.
    """
    parsed_entries = []
    total_lines = 0

    try:
        # Compile the template using fuzzy=True for maximum forgiveness on spacing and delimiters.
        LOG_TEMPLATE = parse.compile(log_pattern, fuzzy=True)
        
        expected_fields = set(field_names)
        
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
                
                result = LOG_TEMPLATE.parse(line)
                
                entry = {
                    "Fields": {},
                    "FieldOrder": field_names 
                }

                if result is not None:
                    # --- SUCCESSFUL PARSING (Parse Library is inherently forgiving) ---
                    # Populate all expected fields from the result
                    for field_name in expected_fields:
                        if field_name in result.named:
                            # Use the parsed value
                            entry["Fields"][field_name] = str(result.named[field_name]).strip()
                        else:
                            # If a field was in the template but not extracted, use a placeholder.
                            entry["Fields"][field_name] = "--- (Missing Field)"

                    parsed_entries.append(entry)

                elif is_best_effort:
                    # --- FAILED PARSING IN BEST-EFFORT MODE (Fallback) ---
                    
                    # Set all structured fields to a placeholder value
                    for field_name in other_field_names:
                        entry["Fields"][field_name] = "---"
                    
                    # Put the entire line content into the designated catch-all field
                    entry["Fields"][catch_all_field_name] = f"[UNPARSED] {line}"
                    
                    parsed_entries.append(entry)
                
                # In strict mode (is_best_effort=False), failed lines are simply skipped (not added to parsed_entries).

        # In strict mode, if zero lines matched, return an error (standard log parsing expectation)
        if not is_best_effort and not parsed_entries and total_lines > 0:
             return {
                "success": False,
                "error": f"The custom template matched 0 of {total_lines} lines. Please verify your template."
            }


        return {
            "success": True, 
            "data": parsed_entries
        }
        
    except FileNotFoundError:
        return {
            "success": False, 
            "error": f"Error: The file was not found at path: {file_path}"
        }
    except Exception as e:
        # Catch-all for other unexpected issues, including invalid templates
        return {
            "success": False, 
            "error": f"An unexpected error occurred during parsing (Template Error?): {str(e)}"
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
