using Common.Core;
using Common.Core.Auth;
using Common.Core.Helpers;
using ModuleCover.Models;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation.Regions;
using System;

namespace ModuleCover.ViewModels
{
    public class CoverRegionViewModel : BindableBase
    {
        public CoverModel Model { get; } = new CoverModel();

        private readonly IRegionManager _regionManager;
        private readonly IUserSession   _session;

        private string _currentUserText = string.Empty;
        public string CurrentUserText
        {
            get => _currentUserText;
            private set => SetProperty(ref _currentUserText, value);
        }

        public DelegateCommand<object> OnCommandBtn { get; }
        public DelegateCommand LogoutCommand { get; }

        public CoverRegionViewModel(IRegionManager regionManager, IUserSession session)
        {
            _regionManager = regionManager;
            _session       = session;

            OnCommandBtn  = new DelegateCommand<object>(_onCommandBtn);
            LogoutCommand = new DelegateCommand(_onLogout);

            _session.SessionChanged += OnSessionChanged;
            UpdateTilePermissions();
        }

        // ── session handling ────────────────────────────────────────────────

        private void OnSessionChanged(object? sender, EventArgs e)
        {
            UpdateTilePermissions();
        }

        private void UpdateTilePermissions()
        {
            CurrentUserText = _session.IsAuthenticated
                ? $"{_session.CurrentUser!.DisplayName}  [{_session.Role}]"
                : string.Empty;

            foreach (var tile in Model.Tiles)
            {
                tile.IsEnabled = _session.HasPermission(TileTypeToPermission(tile.TileType));
            }
        }

        private static Permission TileTypeToPermission(TileType tile) => tile switch
        {
            TileType.Mortor  => Permission.NavigateMotor,
            TileType.Network => Permission.NavigateNetwork,
            TileType.Led     => Permission.NavigateLed,
            TileType.BMS     => Permission.NavigateBms,
            TileType.Camera  => Permission.NavigateCamera,
            TileType.Lidar   => Permission.NavigateLidar,
            // Tiles without a mapped module (Head, Arm, Leg, Body, Hand, LLB) follow Motor permission
            _                => Permission.NavigateMotor
        };

        private void _onLogout()
        {
            _session.Logout();
            _regionManager.RequestNavigate("CoverRegion", "LoginView");
        }

        // ── navigation ──────────────────────────────────────────────────────

        private void _onCommandBtn(object obj)
        {
            try
            {
                if (obj is not TileType tile) return;

                // Block navigation if tile is disabled (no permission)
                var data = Model.Tiles.FirstOrDefault(t => t.TileType == tile);
                if (data is not null && !data.IsEnabled) return;

                switch (tile)
                {
                    case TileType.Mortor:
                        _regionManager.RequestNavigate("CoverRegion", "MotorView");
                        break;
                    case TileType.Network:
                        _regionManager.RequestNavigate("CoverRegion", "NetworkView");
                        break;
                    case TileType.Camera:
                        _regionManager.RequestNavigate("CoverRegion", "CameraView");
                        break;
                    case TileType.Lidar:
                        _regionManager.RequestNavigate("CoverRegion", "LidarView");
                        break;
                    case TileType.Led:
                        _regionManager.RequestNavigate("CoverRegion", "TestLedView");
                        break;
                    case TileType.BMS:
                        _regionManager.RequestNavigate("CoverRegion", "TestBmsView");
                        break;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
        }
    }
}
