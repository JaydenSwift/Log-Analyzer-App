import sys
import re
import json
import os
from itertools import islice

# --- Configuration ---
# Number of lines to check for robust pattern suggestion
ROBUST_CHECK_LINES = 5
# ---------------------

# --- Pattern Sanitization Helper ---
def sanitize_pattern(pattern):
    """
    Ensures backslashes in the regex pattern are correctly escaped for 
    compilation after being passed via command-line arguments.
    """
    # Replace single backslashes with double backslashes (e.g., '\s' becomes '\\s')
    # This is often necessary when the command line or C# argument marshalling strips one layer of escaping.
    return pattern.replace('\\', '\\\\')

# --- Pattern Suggestion Data Loader ---

def load_patterns():
    """
    Loads COMMON_LOG_PATTERNS from the external patterns.json file.
    If loading fails, it returns a minimal pattern list.
    """
    # Define a guaranteed valid minimal pattern for fallback situations (File not found, JSON error)
    MINIMAL_DEFAULT_PATTERN = [{
        "pattern": r"^(\S+)\s+(.*)$",
        "description": "Minimal Default (File/Format Error Fallback)",
        "field_names": ["Token1", "Message"]
    }]

    try:
        # CRITICAL PATH FIX:
        # Get the directory of the *currently executing script file* regardless of the CWD.
        script_dir = os.path.dirname(os.path.abspath(__file__))
        patterns_path = os.path.join(script_dir, "patterns.json")
        
        # Check if the file exists before attempting to open it (for better debugging)
        if not os.path.exists(patterns_path):
            print(f"ERROR: patterns.json not found at expected path: {patterns_path}. Returning minimal default.", file=sys.stderr)
            return MINIMAL_DEFAULT_PATTERN

        with open(patterns_path, 'r') as f:
            patterns = json.load(f)
            
            if isinstance(patterns, list) and all(isinstance(p, dict) for p in patterns):
                print(f"DEBUG: Successfully loaded {len(patterns)} patterns from patterns.json at {patterns_path}", file=sys.stderr)
                
                # If patterns list is loaded but empty, return minimal default to prevent index errors
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

# --- Pattern Suggestion Logic (FINALIZED) ---

def suggest_robust_pattern(file_path):
    """
    Finds the best matching regex pattern by checking the first 
    ROBUST_CHECK_LINES of the log file using a scoring system.
    
    If no pattern matches any line (score=0), it defaults to the first pattern in the list.
    """
    # Ensure there is at least one pattern loaded (the minimal default if all else failed)
    if not COMMON_LOG_PATTERNS:
        # This state should be unreachable due to load_patterns returning a minimal default
        # but included for absolute safety.
        return {
            "pattern": r"^(\S+)\s+(.*)$",
            "description": "Critical Error Fallback (UNREACHABLE)",
            "field_names": ["Token1", "Message"]
        }

    # Start with the first available pattern (which is the minimal default if loading failed, 
    # or the user's specific pattern if loading succeeded)
    initial_best_def = COMMON_LOG_PATTERNS[0]
    
    if not file_path:
        # Return the most standard pattern if no file path is given
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
            # If the file is empty or only contains whitespace
            return initial_best_def
            
        # Initialize tracking variables for the best pattern found so far
        best_pattern_def = initial_best_def
        highest_score = 0
        max_capture_groups = len(initial_best_def["field_names"])

        # Iterate through all patterns and score them
        for pattern_def in COMMON_LOG_PATTERNS:
            try:
                # IMPORTANT: Sanitize the pattern before compiling it for testing
                pattern = sanitize_pattern(pattern_def["pattern"])
                regex = re.compile(pattern)
                expected_groups = len(pattern_def["field_names"])
                current_score = 0
                
                for line in log_lines_to_check:
                    match = regex.match(line)
                    
                    # A match is only considered successful if it matches AND extracts the expected number of groups
                    # NOTE: This check remains strict here to ensure the SUGGESTION is accurate.
                    if match and len(match.groups()) == expected_groups:
                        current_score += 1
                
                # --- Scoring & Tie-Breaking Logic ---
                if current_score > highest_score:
                    # Found a strictly better pattern based on number of matched lines
                    highest_score = current_score
                    max_capture_groups = expected_groups
                    best_pattern_def = pattern_def
                elif current_score == highest_score and expected_groups > max_capture_groups:
                    # Found a tie in score, but this pattern is more specific (more capture groups)
                    max_capture_groups = expected_groups
                    best_pattern_def = pattern_def
                    
            except re.error:
                # Skip invalid patterns
                continue
                
        # --- FINAL SELECTION LOGIC ---
        # The best scoring pattern is returned, or the first pattern if no lines matched (highest_score=0).
        
        print(f"DEBUG: Final selection (Score: {highest_score}/{len(log_lines_to_check)}, Groups: {max_capture_groups}). Description: {best_pattern_def['description']}", file=sys.stderr)
        
        return best_pattern_def

    except Exception as e:
        print(f"ERROR: Unexpected error during pattern suggestion: {e}. Defaulting to first pattern.", file=sys.stderr)
        # If file reading fails or another unexpected error occurs, fall back gracefully to the first pattern
        return initial_best_def


# --- Core Parsing Logic (UPDATED) ---

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
        # CRITICAL FIX: Sanitize the incoming pattern immediately before compilation
        sanitized_log_pattern = sanitize_pattern(log_pattern)
        LOG_PATTERN_REGEX = re.compile(sanitized_log_pattern)
        
        # Dynamic: The expected groups are determined by the field names
        expected_groups = len(field_names)
        
        # --- DEBUGGING INSERT START ---
        print(f"DEBUG PARSE: Using Pattern (Original): {log_pattern}", file=sys.stderr)
        print(f"DEBUG PARSE: Using Pattern (Sanitized): {sanitized_log_pattern}", file=sys.stderr)
        print(f"DEBUG PARSE: Expected Groups: {expected_groups}", file=sys.stderr)
        # --- DEBUGGING INSERT END ---

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
                        # CRITICAL CHANGE: Only check for 'if match' to force extraction 
                        # even if group counts are technically incorrect (best effort).
                        if match:
                            for i in range(expected_groups):
                                field_name = field_names[i]
                                # This line will throw an IndexError if match.group(i + 1) does not exist
                                field_value = match.group(i + 1).strip()
                                entry["Fields"][field_name] = field_value
                            
                            parsed_lines_count += 1
                            is_matched_and_extracted = True
                        else:
                            # If match failed, fall through to forced entry creation
                            raise ValueError("Forced fallback due to match failure.")
                        
                    except (AttributeError, IndexError, ValueError):
                        # Attribute Error if match is None, Index Error if group count is wrong, ValueError from explicit raise
                        # --- UNMATCHED LINE / BEST-EFFORT LOGIC (The Forced Fallback) ---
                        
                        # --- DEBUGGING INSERT START ---
                        match_status = "No Match" if match is None else f"Group Index Error (Groups < {expected_groups})"
                        print(f"DEBUG PARSE: Match Failed on line {total_lines}: {match_status}. Line: {line[:80]}...", file=sys.stderr)
                        # --- DEBUGGING INSERT END ---
                        
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
