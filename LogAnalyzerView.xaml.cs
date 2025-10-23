using System.Windows.Controls;

namespace Log_Analyzer_App
{
    /// <summary>
    /// Interaction logic for LogAnalyzerView.xaml
    /// This is the primary view for displaying log data.
    /// </summary>
    public partial class LogAnalyzerView : UserControl
    {
        public LogAnalyzerView()
        {
            InitializeComponent();
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}
