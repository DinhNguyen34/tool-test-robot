using Common.Core.Helpers;
using ModuleNetwork.Models;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ModuleNetwork.ViewModels
{
    public class NetworkViewModel : BindableBase
    {
        private readonly IRegionManager _regionManager;

        // ── UDP state ──────────────────────────────────────────
        private UdpClient? _listener;
        private CancellationTokenSource? _cts;

        // ── Bindable properties ────────────────────────────────
        private string _remoteIp = "127.0.0.1";
        public string RemoteIp
        {
            get => _remoteIp;
            set => SetProperty(ref _remoteIp, value);
        }

        private int _remotePort = 5000;
        public int RemotePort
        {
            get => _remotePort;
            set => SetProperty(ref _remotePort, value);
        }

        private int _localPort = 5001;
        public int LocalPort
        {
            get => _localPort;
            set => SetProperty(ref _localPort, value);
        }

        private string _sendMessage = string.Empty;
        public string SendMessage
        {
            get => _sendMessage;
            set => SetProperty(ref _sendMessage, value);
        }

        private bool _isListening;
        public bool IsListening
        {
            get => _isListening;
            set
            {
                SetProperty(ref _isListening, value);
                RaisePropertyChanged(nameof(ListenButtonLabel));
                RaisePropertyChanged(nameof(StatusText));
            }
        }

        public string ListenButtonLabel => IsListening ? "Stop Listen" : "Start Listen";
        public string StatusText => IsListening ? $"Listening on port {LocalPort}" : "Not listening";

        public ObservableCollection<UdpLogEntry> LogEntries { get; } = new();

        // ── Commands ───────────────────────────────────────────
        public DelegateCommand SendCommand { get; }
        public DelegateCommand ToggleListenCommand { get; }
        public DelegateCommand ClearLogCommand { get; }
        public DelegateCommand GoBackCommand { get; }

        // ── Constructor ────────────────────────────────────────
        public NetworkViewModel(IRegionManager regionManager)
        {
            _regionManager = regionManager;

            SendCommand        = new DelegateCommand(OnSend);
            ToggleListenCommand = new DelegateCommand(OnToggleListen);
            ClearLogCommand    = new DelegateCommand(() => LogEntries.Clear());
            GoBackCommand      = new DelegateCommand(OnGoBack);
        }

        // ── Send ───────────────────────────────────────────────
        private void OnSend()
        {
            if (string.IsNullOrWhiteSpace(SendMessage)) return;

            try
            {
                using var client = new UdpClient();
                var bytes = Encoding.UTF8.GetBytes(SendMessage);
                client.Send(bytes, bytes.Length, RemoteIp, RemotePort);

                AppendLog("TX", $"{RemoteIp}:{RemotePort}", SendMessage);
                LogHelper.Debug($"UDP TX -> {RemoteIp}:{RemotePort}  [{SendMessage}]");
            }
            catch (Exception ex)
            {
                AppendLog("ERR", "Send", ex.Message);
                LogHelper.Exception(ex);
            }
        }

        // ── Listen toggle ──────────────────────────────────────
        private void OnToggleListen()
        {
            if (IsListening)
                StopListen();
            else
                StartListen();
        }

        private void StartListen()
        {
            try
            {
                _cts      = new CancellationTokenSource();
                _listener = new UdpClient(LocalPort);
                IsListening = true;

                AppendLog("INFO", "Local", $"Started listening on port {LocalPort}");
                LogHelper.Debug($"UDP listener started on port {LocalPort}");

                Task.Run(() => ReceiveLoopAsync(_cts.Token));
            }
            catch (Exception ex)
            {
                AppendLog("ERR", "Listen", ex.Message);
                LogHelper.Exception(ex);
                IsListening = false;
            }
        }

        private void StopListen()
        {
            try
            {
                _cts?.Cancel();
                _listener?.Close();
                _listener = null;
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
            finally
            {
                IsListening = false;
                AppendLog("INFO", "Local", "Listener stopped");
                LogHelper.Debug("UDP listener stopped");
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && _listener != null)
                {
                    var result = await _listener.ReceiveAsync(token);
                    var text   = Encoding.UTF8.GetString(result.Buffer);
                    var source = result.RemoteEndPoint.ToString();

                    Application.Current.Dispatcher.Invoke(()
                        => AppendLog("RX", source, text));

                    LogHelper.Debug($"UDP RX <- {source}  [{text}]");
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException)    { }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(()
                    => AppendLog("ERR", "Receive", ex.Message));
                LogHelper.Exception(ex);
            }
        }

        // ── Helpers ────────────────────────────────────────────
        private void AppendLog(string direction, string source, string message)
        {
            LogEntries.Insert(0, new UdpLogEntry
            {
                Timestamp = DateTime.Now.ToString("HH:mm:ss.fff"),
                Direction = direction,
                Source    = source,
                Message   = message,
            });
        }

        private void OnGoBack()
        {
            StopListen();
            _regionManager.RequestNavigate("CoverRegion", "CoverRegion");
        }
    }
}
