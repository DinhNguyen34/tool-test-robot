using System.Windows;
using Common.Core.Helpers;
using ModuleTestBms.Models;
using ModuleTestBms.Views;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation.Regions;
using System.IO;

namespace ModuleTestBms.ViewModels
{
    public class TestBmsViewModel : BindableBase
    {
        public TestBmsModel Model { get; } = new TestBmsModel();
        private readonly IRegionManager _regionManager;

        private string _logText = string.Empty;

        public TestBmsViewModel(IRegionManager regionManager)
        {
            _regionManager = regionManager;
        }

        public string LogText
        {
            get => _logText;
            set => SetProperty(ref _logText, value);
        }
        public DelegateCommand RefreshCanCommand => new(OnRefreshCan);
        public DelegateCommand ConnectCommand => new(OnConnect);
        public DelegateCommand ConfigCommand => new(OnConfig);
        public DelegateCommand ImportDatabaseCommand => new(OnImportDatabase);
        public DelegateCommand RunCommand => new(OnRun);
        public DelegateCommand StopCommand => new(OnStop);
        public DelegateCommand SelectAllCommand => new(OnSelectAll);
        public DelegateCommand DeselectAllCommand => new(OnDeselectAll);
        public DelegateCommand<BmsTestCaseItem> PassCommand => new(OnPass);
        public DelegateCommand<BmsTestCaseItem> FailCommand => new(OnFail);
        public DelegateCommand SaveReportCommand => new(OnSaveReport);
        public DelegateCommand GoBackCommand => new(OnGoBack);
        public DelegateCommand LoggingCommand => new(OnToggleLogging);
        public DelegateCommand ClearMonitorCommand => new(OnClearMonitor);
        public DelegateCommand OpenAscViewerCommand => new(OnOpenAscViewer);
        public DelegateCommand OpenAndScanLogCommand => new DelegateCommand(ExecuteOpenAndScanLog);
        public DelegateCommand SaveToCsvCommand => new DelegateCommand(ExecuteSaveToCsv);

        private void OnRefreshCan()
        {
            try { Model.RefreshCanDevices(); }
            catch (Exception ex) { LogHelper.Exception(ex); }
        }

        private void OnConnect()
        {
            try
            {
                if (Model.IsConnected)
                {
                    Model.Disconnect();
                    AppendLog("Disconnected.");
                }
                else
                {
                    Model.Connect(out string msg);
                    AppendLog(msg);
                }
            }
            catch (Exception ex) { LogHelper.Exception(ex); }
        }

        private void OnConfig()
        {
            // Config window placeholder — to be completed later
            AppendLog("Config: not yet implemented.");
        }

        private void OnImportDatabase()
        {
            try
            {
                var window = new ImportDatabaseWindow(Model);
                window.Owner = Application.Current.MainWindow;
                window.ShowDialog();

                if (Model.CanDb != null)
                    AppendLog($"Database loaded: {Model.CanDb.Messages.Count} messages.");
            }
            catch (Exception ex) { LogHelper.Exception(ex); }
        }

        private async void OnRun()
        {
            try
            {
                if (Model.IsRunning) return;
                await Model.RunSelectedTestCases(msg =>
                {
                    Application.Current.Dispatcher.Invoke(() => AppendLog(msg));
                });
            }
            catch (Exception ex) { LogHelper.Exception(ex); }
        }

        private void OnGoBack()
        {
            if (Model.IsConnected)
            {
                Model.Disconnect();
                AppendLog("GoBack disconnected CAN");
            }

            _regionManager.RequestNavigate("CoverRegion", "CoverRegion");
        }

        private void OnStop()
        {
            Model.IsRunning = false;
            AppendLog("Stop requested.");
        }

        private void OnSelectAll()
        {
            foreach (var tc in Model.TestCases)
                tc.IsSelected = true;
        }

        private void OnDeselectAll()
        {
            foreach (var tc in Model.TestCases)
                tc.IsSelected = false;
        }

        private void OnSaveReport()
        {
            try
            {
                string path = Model.SaveTestReport();
                if (!string.IsNullOrEmpty(path))
                    AppendLog($"Report saved: {path}");
                else
                    AppendLog("Failed to save report.");
            }
            catch (Exception ex) { LogHelper.Exception(ex); }
        }

        private void OnPass(BmsTestCaseItem? tc)
        {
            if (tc == null) return;
            tc.Status = BmsTestStatus.Pass;
            tc.Remark = "Manual: PASS";
            AppendLog($"{tc.Name} => PASS (manual)");
        }

        private void OnFail(BmsTestCaseItem? tc)
        {
            if (tc == null) return;
            tc.Status = BmsTestStatus.Fail;
            tc.Remark = "Manual: FAIL";
            AppendLog($"{tc.Name} => FAIL (manual)");
        }

        private void OnToggleLogging()
        {
            try
            {
                if (Model.IsLogging)
                {
                    string ascPath = Model.StopLogging();
                    if (!string.IsNullOrEmpty(ascPath))
                        AppendLog($"Logging stopped. ASC file saved: {ascPath}");
                    else
                        AppendLog("Logging stopped.");
                }
                else
                {
                    if (!Model.IsConnected)
                    {
                        AppendLog("Cannot start logging: not connected.");
                        return;
                    }

                    var dlg = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "Save CAN Log File",
                        Filter = "Text files (*.txt)|*.txt",
                        DefaultExt = ".txt",
                        FileName = $"BmsCanLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                    };

                    if (dlg.ShowDialog() == true)
                    {
                        Model.StartLogging(dlg.FileName);
                        AppendLog($"Logging started: {dlg.FileName}");
                    }
                }
            }
            catch (Exception ex) { LogHelper.Exception(ex); }
        }

        private void OnClearMonitor()
        {
            Model.ClearMonitor();
            AppendLog("Monitor cleared.");
        }

        private void OnOpenAscViewer()
        {
            try
            {
                var window = new AscViewerWindow(Model);
                window.Owner = Application.Current.MainWindow;
                window.Show();
            }
            catch (Exception ex) { LogHelper.Exception(ex); }
        }

        private void AppendLog(string msg)
        {
            LogText += $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n";
        }


        // Add data to CSV
        private string _tempInputPath;

        private List<CanMessageDef> _availableMessagesInLog;
        public List<CanMessageDef> AvailableMessagesInLog
        {
            get => _availableMessagesInLog;
            set => SetProperty(ref _availableMessagesInLog, value);
        }

        private CanMessageDef _selectedMessageForConvert;
        public CanMessageDef SelectedMessageForConvert
        {
            get => _selectedMessageForConvert;
            set => SetProperty(ref _selectedMessageForConvert, value);
        }

        private async void ExecuteOpenAndScanLog()
        {
            var openDlg = new Microsoft.Win32.OpenFileDialog { Filter = "Text files (*.txt)|*.txt" };

            if (openDlg.ShowDialog() == true)
            {
                _tempInputPath = openDlg.FileName;

                // Scan ID
                var idsInLog = await Task.Run(() => Model.GetUniqueIdsFromLog(_tempInputPath));

                if (Model.CanDb != null)
                {
                    AvailableMessagesInLog = Model.CanDb.Messages
                        .Where(m => idsInLog.Contains(m.IdDec))
                        .ToList();

                    if (AvailableMessagesInLog.Count > 0)
                        MessageBox.Show($"Find {AvailableMessagesInLog.Count} message. Please click choose 1 message and export.");
                    else
                        MessageBox.Show("Not found message in Database.");
                }
            }
        }

        private async void ExecuteSaveToCsv()
        {
            if (string.IsNullOrEmpty(_tempInputPath) || SelectedMessageForConvert == null)
            {
                MessageBox.Show("Please load file txt and choose 1 message!");
                return;
            }

            var saveDlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = Path.GetFileNameWithoutExtension(_tempInputPath) + $"_{SelectedMessageForConvert.MessageName}.csv"
            };

            if (saveDlg.ShowDialog() == true)
            {
                try
                {
                    var filterIds = new List<uint> { SelectedMessageForConvert.IdDec };
                    await Task.Run(() => Model.ConvertDataToCsv(_tempInputPath, saveDlg.FileName, filterIds));

                    MessageBox.Show("Convert success!");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error: " + ex.Message);
                }
            }
        }
    }
}
