using System;
using System.Windows;
using System.Windows.Controls;
// Make sure to include all necessary view namespaces
using Log_Analyzer_App;

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
        /// It ensures the default view (LogAnalyzerView) is loaded on startup 
        /// by calling the navigation logic.
        /// </summary>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Explicitly call the navigation method to guarantee the initial view loads.
            NavigateToView("LogAnalyzerView");
        }

        /// <summary>
        /// Handles the selection change event for the sidebar menu (ListView).
        /// </summary>
        private void SidebarMenu_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsInitialized || RenderPages == null) return;

            if (SidebarMenu.SelectedItem is ListViewItem selectedItem)
            {
                // The Tag is used to identify which UserControl (View) to load
                string viewName = selectedItem.Tag as string;

                // Check for the special case 'Exit' tag
                if (viewName == "Exit")
                {
                    // FIX: Handle exit directly here since the ListViewItem click triggers the selection.
                    Application.Current.Shutdown();
                    return; // Application is shutting down, no need to navigate
                }

                NavigateToView(viewName);
            }
        }

        /// <summary>
        /// Clears the content area and loads the specified UserControl.
        /// </summary>
        /// <param name="viewName">The name of the view (UserControl) to load.</param>
        private void NavigateToView(string viewName)
        {
            UserControl newView = null;

            switch (viewName)
            {
                case "LogAnalyzerView":
                    newView = new LogAnalyzerView();
                    break;
                case "ChartViewer":
                    newView = new ChartViewer();
                    break;
                case "SettingsView":
                    newView = new SettingsView();
                    break;
                default:
                    // Log an error or return a default/error view if the name is unrecognized
                    Console.WriteLine($"Error: Attempted to navigate to unknown view: {viewName}");
                    return;
            }

            // Clear existing content and load the new view
            RenderPages.Children.Clear();
            if (newView != null)
            {
                RenderPages.Children.Add(newView);
            }
        }

        // Removed the unused btnExit_Click handler
    }
}
