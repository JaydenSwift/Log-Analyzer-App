using MaterialDesignThemes.Wpf;
using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Data; // Added for IValueConverter
using System.Globalization; // Added for IValueConverter
using System; // Added for IValueConverter
// The 'using MaterialDesignThemes.Wpf.Themes;' line is removed to avoid conflicts 
// and the code now relies on the BaseTheme enum/struct being in the main namespace.

namespace Log_Analyzer_App
{
    // NEW: Converter to invert a boolean value (False becomes True, True becomes False)
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return value;
        }
    }

    // NEW: Converter to display different text based on a boolean value (e.g., button text)
    public class BooleanToContentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string paramString)
            {
                // Parameter format: 'FalseText|TrueText'
                string[] parts = paramString.Split('|');
                if (parts.Length == 2)
                {
                    return boolValue ? parts[1] : parts[0];
                }
            }
            // Default to the boolean value if conversion fails
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Conversion back is not supported for this scenario
            throw new NotImplementedException();
        }
    }


    /// <summary>
    /// Static helper class to manage the application's Material Design theme.
    /// Uses the approach compatible with versions that utilize the BaseTheme enum/struct.
    /// </summary>
    public static class ThemeManager
    {
        private static readonly PaletteHelper _paletteHelper = new PaletteHelper();

        /// <summary>
        /// Applies the specified theme to the application.
        /// </summary>
        /// <param name="isDark">True for dark theme, false for light theme.</param>
        public static void ApplyTheme(bool isDark)
        {
            // Use the concrete Theme class
            Theme theme = _paletteHelper.GetTheme();

            // FIX for CS1503: We must pass the BaseTheme enum value, not a boolean.
            theme.SetBaseTheme(isDark ? BaseTheme.Dark : BaseTheme.Light);

            _paletteHelper.SetTheme(theme);
        }

        /// <summary>
        /// Gets the current theme state.
        /// </summary>
        public static bool IsDarkModeEnabled()
        {
            // Use the concrete Theme class
            Theme theme = _paletteHelper.GetTheme();

            // FIX for CS1061: Compare the value returned by GetBaseTheme() against 
            // the BaseTheme.Dark enum value.
            return theme.GetBaseTheme() == BaseTheme.Dark;
        }
    }

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // You can now safely call the manager here, for example:
            // ThemeManager.ApplyTheme(false); // Force Light Mode
        }
    }
}