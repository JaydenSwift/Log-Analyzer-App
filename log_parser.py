import sys
import re
import json

# The default log pattern is now a constant, but will be overridden by the argument
DEFAULT_LOG_PATTERN = r'^\[(.*?)\]\s*(INFO|WARN|ERROR):\s*(.*)$'

def parse_log_file(file_path, log_pattern):
    """
    Reads a log file, parses each line using the provided regex, 
    and returns a list of log entries.
    """
    parsed_entries = []
    
    try:
        # Compile the provided regex pattern
        LOG_PATTERN_REGEX = re.compile(log_pattern)
        
        with open(file_path, 'r') as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue

                match = LOG_PATTERN_REGEX.match(line)
                
                if match:
                    # Assuming the regex captures (1) Timestamp, (2) Level, (3) Message
                    # We check if there are at least 3 capture groups (match.groups() includes group 0, the whole match)
                    if len(match.groups()) >= 3:
                        entry = {
                            "Timestamp": match.group(1).strip(),
                            "Level": match.group(2).strip(),
                            "Message": match.group(3).strip()
                        }
                        parsed_entries.append(entry)
                    else:
                         # If the regex matches but doesn't have enough capture groups, ignore the line
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
    if len(sys.argv) < 3:
        # Now requires both file path and regex pattern
        error_output = json.dumps({
            "success": False, 
            "error": "Error: Missing file path or regex pattern argument. (Requires 2 arguments: file_path and log_pattern)"
        })
        print(error_output)
        sys.exit(1)

    # The file path is the first argument
    file_path = sys.argv[1]
    # The regex pattern is the second argument
    log_pattern = sys.argv[2]
    
    # Run the parser
    result = parse_log_file(file_path, log_pattern)
    
    # Print the JSON result to stdout for the C# application to read
    print(json.dumps(result))
    
    sys.exit(0)
