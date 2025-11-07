import sys
import parse
import json
import os
from itertools import islice
from typing import List, Dict, Any

# --- Configuration ---
# Number of lines to check for robust pattern suggestion
ROBUST_CHECK_LINES = 5
# ---------------------

# --- Helper to dynamically extract field names from a parse template ---
def extract_field_names(pattern: str) -> list:
    """Uses the parse library to extract the list of named fields from a template."""
    try:
        # We rely on parse.compile to get the field names for the C# UI.
        return list(parse.compile(pattern).named_fields)
    except Exception:
        # Return an empty list or a fallback if compilation fails
        return []

# --- Pattern Suggestion Data Loader ---

def load_patterns():
    """
    Loads COMMON_LOG_PATTERNS from the external patterns.json file.
    It dynamically generates the 'field_names' list from the 'pattern' string.
    If loading fails, it returns a minimal pattern list.
    """
    # Define a guaranteed valid minimal pattern for fallback situations
    MINIMAL_DEFAULT_PATTERN = [{
        "pattern": "{Token1} {Message}",
        "description": "Minimal Default (File/Format Error Fallback)",
    }]
    # Add field_names dynamically to the fallback pattern
    MINIMAL_DEFAULT_PATTERN[0]["field_names"] = extract_field_names(MINIMAL_DEFAULT_PATTERN[0]["pattern"])


    try:
        # Get the directory of the *currently executing script file* regardless of the CWD.
        script_dir = os.path.dirname(os.path.abspath(__file__))
        patterns_path = os.path.join(script_dir, "patterns.json")
        
        if not os.path.exists(patterns_path):
            print(f"ERROR: patterns.json not found at expected path: {patterns_path}. Returning minimal default.", file=sys.stderr)
            return MINIMAL_DEFAULT_PATTERN

        with open(patterns_path, 'r') as f:
            raw_patterns = json.load(f)
            
            if isinstance(raw_patterns, list) and all(isinstance(p, dict) for p in raw_patterns):
                if not raw_patterns:
                    print("WARNING: patterns.json is empty. Providing minimal default pattern.", file=sys.stderr)
                    return MINIMAL_DEFAULT_PATTERN
                
                # CRITICAL: Dynamically add 'field_names' to each pattern dictionary
                processed_patterns = []
                for p in raw_patterns:
                    if "pattern" in p:
                        # Extract the field names from the template string
                        p["field_names"] = extract_field_names(p["pattern"])
                        processed_patterns.append(p)

                # If no valid patterns were processed, fall back
                if not processed_patterns:
                     print("ERROR: No valid patterns found after processing. Returning minimal default.", file=sys.stderr)
                     return MINIMAL_DEFAULT_PATTERN
                     
                return processed_patterns
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

# --- Pattern Suggestion Logic (Uses parse.search) ---

def suggest_robust_pattern(file_path):
    """
    Finds the best matching parse template by checking the first 
    ROBUST_CHECK_LINES of the log file using a scoring system.
    """
    if not COMMON_LOG_PATTERNS:
        # The fallback pattern already has field_names added in load_patterns
        return load_patterns()[0]

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
        # Use the count of dynamically extracted field names
        max_capture_groups = len(initial_best_def["field_names"])

        # Iterate through all patterns and score them
        for pattern_def in COMMON_LOG_PATTERNS:
            try:
                # Use parse.search for suggestion check (single-use)
                
                expected_groups = len(pattern_def["field_names"])
                current_score = 0
                
                for line in log_lines_to_check:
                    # Use parse.search for a single-use match check
                    result = parse.search(pattern_def["pattern"], line)
                    
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


# --- Core Parsing Logic (Field Name Logic Removed) ---

def parse_log_file(file_path, log_pattern, field_names: List[str], is_best_effort: bool = False):
    """
    Reads a log file, parses each line using the provided parse template.
    It ignores the input field_names for parsing and determines fields dynamically.
    The original field_names is only used for the output structure (FieldOrder).
    """
    parsed_entries: List[Dict[str, Any]] = []
    total_lines = 0
    failed_lines = 0
    lines: List[tuple[int, str]] = []

    try:
        with open(file_path, 'r') as f:
            # Prepare a list of non-empty lines, keeping track of the original line number
            raw_lines = f.readlines()
            for i, line in enumerate(raw_lines):
                stripped_line = line.strip()
                if stripped_line:
                    # Store tuple of (line_number, stripped_content)
                    lines.append((i + 1, stripped_line))
        
        if not lines:
            # Handle empty file case
            return {
                "success": True, 
                "data": []
            }
            
        total_lines = len(lines)
        
        # --- NEW LOGIC: Determine the expected field names dynamically from the pattern
        # This list will include only the NAMED fields, used for filtering output
        expected_named_fields = extract_field_names(log_pattern)


        for i, (line_num, line) in enumerate(lines):
            # Use parse.parse()
            result = parse.parse(log_pattern, line)
            
            entry: Dict[str, Any] = {
                "Fields": {},
                # We retain the input field_names list here for C# structural compatibility
                "FieldOrder": field_names 
            }

            if result:
                # --- SUCCESSFUL PARSING (Replicating ParsingTest.py logic) ---
                
                # 1. Combine named results
                data = result.named.copy()
                
                # 2. Append unnamed/fixed fields, naming them numerically
                for j, fixed_val in enumerate(result.fixed):
                    key = f"unnamed_{j+1}"
                    # Use the key as the field name, avoiding collisions
                    if key not in data: 
                        data[key] = fixed_val

                # 3. Transfer ALL dynamically found results to the C# Fields dictionary
                # The C# client is responsible for showing the correct columns
                
                # Use all dynamically found keys (named + unnamed)
                all_found_keys = list(data.keys())
                
                # The C# application expects the output fields dictionary to match the FieldOrder.
                # Since we want to use dynamically discovered fields, we must update 
                # FieldOrder *if* the template resulted in fixed/unnamed fields not known to C#.
                
                # For simplicity and robust parsing, we fill the 'Fields' dictionary 
                # with everything found. The C# client must be updated to use the 
                # keys of the 'Fields' dictionary dynamically.
                
                # Use the C# required field names as a primary output set
                output_field_names = expected_named_fields.copy()
                
                # Add any unnamed fields discovered during parsing to the output field list
                for key in all_found_keys:
                    if key.startswith("unnamed_") and key not in output_field_names:
                        output_field_names.append(key)
                
                # If the template changed, we must send back the new field order
                if field_names != output_field_names and output_field_names:
                     entry["FieldOrder"] = output_field_names
                     
                for key, val in data.items():
                    entry["Fields"][key] = str(val).strip()

                parsed_entries.append(entry)

            elif is_best_effort:
                # --- FAILED PARSING IN BEST-EFFORT MODE (Fallback) ---
                # Fallback logic: put the whole line in the catch-all field

                # Since we don't know the catch-all field name without checking the 
                # original field_names list, we use a fixed fallback key 'FullLine'.
                # This breaks the dependency on field_names for parsing logic.
                fallback_key = "FullLine"
                
                # Clear existing fields and put the whole line in the fallback key
                entry["Fields"] = {fallback_key: f"[UNPARSED] {line}"}
                # Ensure C# knows about this fallback key
                entry["FieldOrder"] = [fallback_key]
                
                parsed_entries.append(entry)
                failed_lines += 1
                
                print(f"  [Error] Line {line_num} failed to parse: {line[:60]}...", file=sys.stderr)
            
            else:
                # --- FAILED PARSING IN STRICT MODE ---
                failed_lines += 1
                
                print(f"  [Error] Line {line_num} failed to parse: {line[:60]}...", file=sys.stderr)


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
            # We still need to receive this argument from C#
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