


using System.Collections.ObjectModel;
using System.Windows.Shapes;
using VCanPLib;

public class MotorModel : BindableBase
{
    private VCANPCtrl cANPCtrl = new VCANPCtrl();

    private CanDevice _selectedCan;
    public CanDevice SelectedCan { get { return _selectedCan; } set { SetProperty(ref _selectedCan, value); } }
    public ObservableCollection<CanDevice> ListcanDevices { get; } = new ObservableCollection<CanDevice>();
    
    public MotorModel()
    {
       
    }
    public void GetListCans()
    {
        ListcanDevices.Clear();
        var listCans = cANPCtrl.GetAllCanAvailable();

        if (listCans != null && listCans.Count > 0)
        {
            foreach (var item in listCans)
            {
                ListcanDevices.Add(new CanDevice { DisplayName = item.DisplayName, CanType = CanType.CAN_FD, Name = item.DisplayName, IsConnected = false });
            }
        }
    }
}