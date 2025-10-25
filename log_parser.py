import sys
import re
import json

# Define the log pattern used in the original C# application.
# It captures Timestamp, Level (INFO|WARN|ERROR), and Message.
# Example: [2025-10-23 09:00:00] INFO: Application started successfully.
LOG_PATTERN = re.compile(r'^\[(.*?)\]\s*(INFO|WARN|ERROR):\s*(.*)$')

def parse_log_file(file_path):
    """
    Reads a log file, parses each line, and returns a list of log entries.
    """
    parsed_entries = []
    
    try:
        with open(file_path, 'r') as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue

                match = LOG_PATTERN.match(line)
                
                if match:
                    # Create a dictionary matching the C# LogEntry class structure
                    entry = {
                        "Timestamp": match.group(1).strip(),
                        "Level": match.group(2).strip(),
                        "Message": match.group(3).strip()
                    }
                    parsed_entries.append(entry)
                # Note: Lines that don't match the standard format are ignored, 
                # which is the current behavior. This can be expanded later.

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
        return {
            "success": False, 
            "error": f"An unexpected error occurred during parsing: {str(e)}"
        }


if __name__ == "__main__":
    if len(sys.argv) < 2:
        # If no file path is provided, print an error message JSON
        error_output = json.dumps({
            "success": False, 
            "error": "Error: Missing file path argument."
        })
        print(error_output)
        sys.exit(1)

    # The file path is the first argument
    file_path = sys.argv[1]
    
    # Run the parser
    result = parse_log_file(file_path)
    
    # Print the JSON result to stdout for the C# application to read
    print(json.dumps(result))
    
    # Exit with code 0 if successful, 1 if failed (though the C# app checks the JSON 'success' field)
    sys.exit(0)
