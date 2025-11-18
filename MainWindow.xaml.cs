using System;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using Log_Analyzer_App;

namespace Log_Analyzer_App
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private bool _isLoading = false;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged(nameof(IsLoading));
                    this.Cursor = value ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
                }
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;
        }

        /// <summary>
        /// This method is executed when the window finishes loading.
        /// It ensures the default view (LogAnalyzerView) is loaded on startup 
        /// by calling the navigation logic.
        /// </summary>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
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
                    Application.Current.Shutdown();
                    return;
                }

                // If loading, do not allow navigation
                if (IsLoading)
                {
                    // To prevent navigation while loading, deselect the new item and reselect the old one
                    // Find the previously selected item (before the click triggered this event)
                    var previousItem = e.RemovedItems.Count > 0 ? e.RemovedItems[0] as ListViewItem : null;
                    if (previousItem != null)
                    {
                        // Reselect the previous item to revert the UI state
                        SidebarMenu.SelectedItem = previousItem;
                    }

                    // Show a message or just silently ignore the click
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
                    var chartViewer = new ChartViewer();
                    newView = chartViewer;
                    break;
                case "SettingsView":
                    newView = new SettingsView();
                    break;
                default:
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

        // --- INotifyPropertyChanged Implementation ---
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}