using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Reflection.Metadata;
using System.Windows;
using Common.Core.Helpers;
using ModuleTestLed.Models;
using ModuleTestLed.Views;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation.Regions;


namespace ModuleTestLed.ViewModels
{
    public class TestLedViewModel : BindableBase
    {
        public TestLedModel Model { get; } = new TestLedModel();
        private readonly IRegionManager _regionManager;
        private readonly List<LedTestCaseItem> _trackedTestCases = [];

        private string _logText = string.Empty;
        private bool _isLogging;
        private bool _isUpdatingSelectAllState;
        private StreamWriter? _logWriter;
        private bool? _areAllTestCasesSelected = true;
        public TestLedViewModel(IRegionManager regionManager)
        {
            _regionManager = regionManager;
            Model.TestCases.CollectionChanged += OnTestCasesCollectionChanged;
            RebindTestCaseSelectionTracking();
        }
        public string LogText
        {
            get => _logText;
            set => SetProperty(ref _logText, value);
        }

        public bool? AreAllTestCasesSelected
        {
            get => _areAllTestCasesSelected;
            set
            {
                if (!SetProperty(ref _areAllTestCasesSelected, value))
                    return;

                if (_isUpdatingSelectAllState || !value.HasValue)
                    return;

                SetAllTestCasesSelected(value.Value);
            }
        }

        public DelegateCommand RefreshCanCommand => new(OnRefreshCan);
        public DelegateCommand ConnectCommand => new(OnConnect);
        public DelegateCommand ConfigCommand => new(OnConfig);
        public DelegateCommand RunCommand => new(OnRun);
        public DelegateCommand StopCommand => new(OnStop);
        public DelegateCommand SelectAllCommand => new(OnSelectAll);
        public DelegateCommand DeselectAllCommand => new(OnDeselectAll);
        public DelegateCommand<LedTestCaseItem> PassCommand => new(OnPass);
        public DelegateCommand<LedTestCaseItem> FailCommand => new(OnFail);
        public DelegateCommand TestOneLedCommand => new(OnTestOneLed);
        public DelegateCommand SaveReportCommand => new(OnSaveReport);
        public DelegateCommand GoBackCommand => new(OnGoBack);

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
            try
            {
                var window = new LedConfigWindow(Model.Config);
                if (window.ShowDialog() == true)
                {
                    Model.Config.Save();
                    Model.ReloadConfig();
                    AppendLog("Config saved and test cases rebuilt.");
                }
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
        public void OnGoBack()
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
            SetAllTestCasesSelected(true);
        }

        private void OnDeselectAll()
        {
            SetAllTestCasesSelected(false);
        }

        private void OnTestOneLed()
        {
            try
            {
                var window = new TestOneLedWindow(Model);
                window.Owner = Application.Current.MainWindow;
                window.ShowDialog();
            }
            catch (Exception ex) { LogHelper.Exception(ex); }
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

        private void OnPass(LedTestCaseItem? tc)
        {
            if (tc == null) return;
            tc.Status = LedTestStatus.Pass;
            tc.Remark = "Manual: PASS";
            AppendLog($"{tc.Name} => PASS (manual)");
        }

        private void OnFail(LedTestCaseItem? tc)
        {
            if (tc == null) return;
            tc.Status = LedTestStatus.Fail;
            tc.Remark = "Manual: FAIL";
            AppendLog($"{tc.Name} => FAIL (manual)");
        }

        private void AppendLog(string msg)
        {
            LogText += $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n";
        }

        private void OnTestCasesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RebindTestCaseSelectionTracking();
        }

        private void RebindTestCaseSelectionTracking()
        {
            foreach (var testCase in _trackedTestCases)
            {
                testCase.PropertyChanged -= OnTestCasePropertyChanged;
            }

            _trackedTestCases.Clear();

            foreach (var testCase in Model.TestCases)
            {
                testCase.PropertyChanged += OnTestCasePropertyChanged;
                _trackedTestCases.Add(testCase);
            }

            UpdateAreAllTestCasesSelected();
        }

        private void OnTestCasePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LedTestCaseItem.IsSelected))
            {
                UpdateAreAllTestCasesSelected();
            }
        }

        private void SetAllTestCasesSelected(bool isSelected)
        {
            foreach (var testCase in Model.TestCases)
            {
                testCase.IsSelected = isSelected;
            }

            UpdateAreAllTestCasesSelected();
        }

        private void UpdateAreAllTestCasesSelected()
        {
            bool? nextState;
            if (Model.TestCases.Count == 0)
            {
                nextState = false;
            }
            else
            {
                int selectedCount = Model.TestCases.Count(testCase => testCase.IsSelected);
                nextState = selectedCount switch
                {
                    0 => false,
                    var count when count == Model.TestCases.Count => true,
                    _ => null
                };
            }

            _isUpdatingSelectAllState = true;
            try
            {
                AreAllTestCasesSelected = nextState;
            }
            finally
            {
                _isUpdatingSelectAllState = false;
            }
        }
    }
}
