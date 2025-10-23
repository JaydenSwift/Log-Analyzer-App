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
            // Set initial selection after fields are assigned
            SidebarMenu.SelectedIndex = 0;
        }

        /// <summary>
        /// This method is executed when the window finishes loading.
        /// It ensures the default view (LogAnalyzerView) is loaded on startup 
        /// by calling the navigation logic.
        /// </summary>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure the UI tree is ready before selecting
            SidebarMenu.SelectedIndex = 0; // or NavigateToView("LogAnalyzerView");
        }

        /// <summary>
        /// Handles the click event for the Exit button on the navigation bar.
        /// </summary>
        private void btnExit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
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
                    // For the Exit button, we don't want to load a new view, 
                    // and the exit logic is handled by the dedicated btnExit_Click
                    // However, we need to deselect it immediately to avoid confusion.
                    selectedItem.IsSelected = false;
                    return;
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
    }
}
