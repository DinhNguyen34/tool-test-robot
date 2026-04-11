using Common.Core.Helpers;
using ModuleTestLed.Models;
using ModuleTestLed.Views;
using Prism.Commands;
using Prism.Mvvm;
using System.Collections.ObjectModel;
using System.Windows;

namespace ModuleTestLed.ViewModels
{
    public class TestLedViewModel : BindableBase
    {
        public TestLedModel Model { get; } = new TestLedModel();

        private string _logText = string.Empty;
        public string LogText
        {
            get => _logText;
            set => SetProperty(ref _logText, value);
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

        public TestLedViewModel()
        {
        }

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
    }
}
