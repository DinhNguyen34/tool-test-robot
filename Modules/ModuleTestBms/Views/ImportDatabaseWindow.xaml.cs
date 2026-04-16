using Microsoft.Win32;
using ModuleTestBms.Models;
using System.Windows;

namespace ModuleTestBms.Views
{
    public partial class ImportDatabaseWindow : Window
    {
        private readonly TestBmsModel _model;
        private string? _csvPath;

        public ImportDatabaseWindow(TestBmsModel model)
        {
            InitializeComponent();
            _model = model;
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                Title = "Select CAN Database CSV"
            };
            if (dlg.ShowDialog() == true)
            {
                _csvPath = dlg.FileName;
                TxtCsvPath.Text = _csvPath;
                AppendLog($"Selected: {_csvPath}");
            }
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_csvPath))
            {
                AppendLog("Please select a CSV file first.");
                return;
            }

            try
            {
                string jsonPath = _model.ImportDatabase(_csvPath, AppendLog);
                AppendLog($"Import completed. JSON saved to: {jsonPath}");
                MessageBox.Show($"Database imported successfully!\n{_model.CanDb?.Messages.Count ?? 0} messages loaded.",
                    "Import Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendLog($"Error: {ex.Message}");
                MessageBox.Show($"Import failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void AppendLog(string msg)
        {
            TxtLog.Text += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            TxtLog.ScrollToEnd();
        }
    }
}
