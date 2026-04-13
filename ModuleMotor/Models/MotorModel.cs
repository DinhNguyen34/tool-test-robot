using System.Collections.ObjectModel;
using System.Globalization;
using ModuleMotor.Models;
using VCanPLib;

public class MotorModel : BindableBase
{
    private static bool _isVCanLoggerInitialized;
    private readonly VCANPCtrl _canCtrl = new();
    public ObservableCollection<CanDevice> ListcanDevices { get; } = new();
    private CanDevice? _selectedCan;
    public CanDevice? SelectedCan { get => _selectedCan; set => SetProperty(ref _selectedCan, value); }

    public MotorModel()
    {
        EnsureVCanLoggerInitialized();
    }

    private static void EnsureVCanLoggerInitialized()
    {
        if (_isVCanLoggerInitialized)
            return;

        VCanPLib.LogHelper.Init();
        _isVCanLoggerInitialized = true;
    }


    public void GetListCans()
    {
        ListcanDevices.Clear();
        var listCans = _canCtrl.GetAllCanAvailable();

        if (listCans == null || listCans.Count == 0)
        {
            SelectedCan = null;
            return;
        }

        foreach (var item in listCans)
            ListcanDevices.Add(item);

        if (SelectedCan == null || !ListcanDevices.Contains(SelectedCan))
            SelectedCan = ListcanDevices[0];
    }

    public bool Connect(int requestedBaud, string rawLogPath, out string message)
        => Connect(requestedBaud, rawLogPath, useCanFd: false, out message);

    public bool Connect(int requestedBaud, string rawLogPath, bool useCanFd, out string message)
    {
        if (SelectedCan == null)
        {
            message = "No CAN device selected.";
            return false;
        }

        var hasExactBaud = TryMapBaudrate(requestedBaud, out var canBaud);
        if (!hasExactBaud)
            canBaud = CanBaudrate.BAUDRATE_1000;

        var connected = _canCtrl.Connect(SelectedCan, rawLogPath, CanBaudrate.BAUDRATE_1000, CanBaudrate.BAUDRATE_1000, bitrateSwitch.SW_OFF,  CanType.CAN_STD);
        SelectedCan.IsConnected = connected;

        if (connected)
        {
            _canCtrl.EnableReadLog(true);
            message = hasExactBaud
                ? $"Connected to {SelectedCan.DisplayName} at {canBaud} ({canType})."
                : $"Connected to {SelectedCan.DisplayName} using fallback bitrate {canBaud} ({canType}) for unsupported setting {requestedBaud}.";
        }
        else
        {
            message = $"Failed to connect to {SelectedCan.DisplayName}.";
        }

        return connected;
    }

    public void Close()
    {
        if (_canCtrl.GetOpenStatus())
            _canCtrl.Close();

        if (SelectedCan != null)
            SelectedCan.IsConnected = false;
    }

    public bool GetOpenStatus() => _canCtrl.GetOpenStatus();

    public bool SendMessage(string canId, byte[] data, out string message)
        => SendMessage(canId, data, isExtendedId: true, out message);

    public bool SendMessage(string canId, byte[] data, bool isExtendedId, out string message)
    {
        if (!_canCtrl.GetOpenStatus())
        {
            message = "CAN device is not connected.";
            return false;
        }

        if (!TryParseCanId(canId, out uint numericId))
        {
            message = $"Invalid CAN ID: {canId}";
            return false;
        }

        string sanitizedId = $"0x{numericId:X}";
        bool sent = _canCtrl.SendMessage(sanitizedId, data, isExtendedId);

        message = sent ? string.Empty : $"Failed to send CAN frame to ID {sanitizedId}.";
        return sent;
    }

    public bool SendFrame(CanFrameSpec frame, out string message)
        => SendMessage(frame.CanId, frame.Payload, frame.IsExtendedId, out message);

    public IReadOnlyList<RawDataCan> GetCanMessages()
    {
        var messages = _canCtrl.GetCanMessegers();
        return messages is not null ? messages : Array.Empty<RawDataCan>();
    }

    private static bool TryMapBaudrate(int requestedBaud, out CanBaudrate canBaud)
    {
        canBaud = requestedBaud switch
        {
            5 or 5000 => CanBaudrate.BAUDRATE_5,
            10 or 10000 => CanBaudrate.BAUDRATE_10,
            20 or 20000 => CanBaudrate.BAUDRATE_20,
            50 or 50000 => CanBaudrate.BAUDRATE_50,
            100 or 100000 => CanBaudrate.BAUDRATE_100,
            120 or 120000 => CanBaudrate.BAUDRATE_120,
            200 or 200000 => CanBaudrate.BAUDRATE_200,
            250 or 250000 => CanBaudrate.BAUDRATE_250,
            400 or 400000 => CanBaudrate.BAUDRATE_400,
            500 or 500000 => CanBaudrate.BAUDRATE_500,
            800 or 800000 => CanBaudrate.BAUDRATE_800,
            1000 or 1000000 => CanBaudrate.BAUDRATE_1000,
            1200 or 1200000 => CanBaudrate.BAUDRATE_1200,
            1500 or 1500000 => CanBaudrate.BAUDRATE_1500,
            2000 or 2000000 => CanBaudrate.BAUDRATE_2000,
            _ => CanBaudrate.Unknow
        };

        return canBaud != CanBaudrate.Unknow;
    }

    private const uint CanExtendedIdMask = 0x1FFF_FFFF;

    private static bool TryParseCanId(string canId, out uint numericCanId)
    {
        numericCanId = 0;

        if (string.IsNullOrWhiteSpace(canId))
            return false;

        canId = canId.Trim();
        bool parsed = canId.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.TryParse(canId[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out numericCanId)
            : uint.TryParse(canId, NumberStyles.Integer, CultureInfo.InvariantCulture, out numericCanId);

        if (parsed)
            numericCanId &= CanExtendedIdMask;

        return parsed;
    }
}
