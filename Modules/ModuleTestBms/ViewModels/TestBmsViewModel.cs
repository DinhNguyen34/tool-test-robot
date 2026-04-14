using System.IO;
using System.Windows;
using Common.Core.Helpers;
using ModuleTestBms.Models;
using ModuleTestBms.Views;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation.Regions;

namespace ModuleTestBms.ViewModels
{
    public class TestBmsViewModel : BindableBase
    {
        public TestBmsModel Model { get; } = new TestBmsModel();
        private readonly IRegionManager _regionManager;

        private string _logText = string.Empty;
        private bool _isLogging;
        private StreamWriter? _logWriter;

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
        public DelegateCommand StartMonitorCommand => new(OnStartMonitor);
        public DelegateCommand StopMonitorCommand => new(OnStopMonitor);
        public DelegateCommand ClearMonitorCommand => new(OnClearMonitor);

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

            if (_isLogging)
            {
                _isLogging = false;
                _logWriter?.Close();
                _logWriter = null;
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

        private void OnStartMonitor()
        {
            if (!Model.IsConnected)
            {
                AppendLog("Cannot start monitor: not connected.");
                return;
            }
            Model.StartMonitor();
            AppendLog("CAN Monitor started.");
        }

        private void OnStopMonitor()
        {
            Model.StopMonitor();
            AppendLog("CAN Monitor stopped.");
        }

        private void OnClearMonitor()
        {
            Model.ClearMonitor();
            AppendLog("Monitor cleared.");
        }

        private void AppendLog(string msg)
        {
            LogText += $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n";
        }
    }
}
