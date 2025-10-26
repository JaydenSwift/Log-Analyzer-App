import sys
import re
import json

# The default log pattern is now a constant, but will be overridden by the argument
DEFAULT_LOG_PATTERN = r'^\[(.*?)\]\s*(INFO|WARN|ERROR):\s*(.*)$'

def parse_log_file(file_path, log_pattern, field_names):
    """
    Reads a log file, parses each line using the provided regex, 
    and returns a list of log entries where each entry contains a dictionary 
    of user-defined fields.
    
    :param file_path: Path to the log file.
    :param log_pattern: The regex pattern with capture groups.
    :param field_names: List of strings (user-defined column names) for the capture groups.
    """
    parsed_entries = []
    
    try:
        # Compile the provided regex pattern
        LOG_PATTERN_REGEX = re.compile(log_pattern)
        
        # Determine the expected number of capture groups
        expected_groups = len(field_names)

        with open(file_path, 'r') as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue

                match = LOG_PATTERN_REGEX.match(line)
                
                if match:
                    # Group 0 is the full match. We need groups 1 through expected_groups.
                    # Total groups must be match.groups() + 1
                    if len(match.groups()) == expected_groups:
                        entry = {
                            "Fields": {},
                            # Store the field order explicitly for C# derivation
                            "FieldOrder": field_names 
                        }
                        
                        # Populate the dynamic Fields dictionary
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
        # Catch regex compilation errors if a bad pattern is passed
        return {
            "success": False,
            "error": f"Invalid Regex Pattern provided: {str(e)}"
        }
    except Exception as e:
        return {
            "success": False, 
            "error": f"An unexpected error occurred during parsing: {str(e)}"
        }


if __name__ == "__main__":
    if len(sys.argv) < 4:
        # Now requires file path, regex pattern, and field names JSON
        error_output = json.dumps({
            "success": False, 
            "error": "Error: Missing arguments. (Requires 3 arguments: file_path, log_pattern, and field_names_json)"
        })
        print(error_output)
        sys.exit(1)

    # The arguments are:
    file_path = sys.argv[1]
    log_pattern = sys.argv[2]
    field_names_json = sys.argv[3]
    
    # Deserialize the field names list from the JSON string
    try:
        field_names = json.loads(field_names_json)
    except json.JSONDecodeError:
        error_output = json.dumps({
            "success": False, 
            "error": "Error: Failed to decode field names JSON argument."
        })
        print(error_output)
        sys.exit(1)

    
    # Run the parser
    result = parse_log_file(file_path, log_pattern, field_names)
    
    # Print the JSON result to stdout for the C# application to read
    print(json.dumps(result))
    
    sys.exit(0)
