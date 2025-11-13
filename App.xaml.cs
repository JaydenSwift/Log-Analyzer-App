using MaterialDesignThemes.Wpf;
using System.Configuration;
using System.Data;
using System.Windows;
// The 'using MaterialDesignThemes.Wpf.Themes;' line is removed to avoid conflicts 
// and the code now relies on the BaseTheme enum/struct being in the main namespace.

namespace Log_Analyzer_App
{
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