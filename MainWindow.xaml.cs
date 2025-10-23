using System.Windows;
using Log_Analyzer_App; // Ensures we can reference LogAnalyzerView

namespace Log_Analyzer_App
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// This method is executed when the window finishes loading.
        /// It loads the LogAnalyzerView into the main content area (RenderPages).
        /// </summary>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Load the primary user control (view) into the content host Grid.
            RenderPages.Children.Clear();
            RenderPages.Children.Add(new LogAnalyzerView());
        }

        /// <summary>
        /// Handles the click event for the Exit button on the navigation bar.
        /// </summary>
        private void btnExit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        // NOTE: Additional navigation/switching logic would go here for other sidebar buttons.
    }
}
