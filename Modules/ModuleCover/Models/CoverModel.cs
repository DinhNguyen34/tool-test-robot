using Common.Core;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace ModuleCover.Models
{
    public class CoverData : BindableBase
    {
        private string _text;
        private BitmapSource _image;
        private TileType _tileType;

        public string Text { get { return _text; } set { SetProperty(ref _text, value); } }
        public BitmapSource Image { get { return _image; } set { SetProperty(ref _image, value); } }
        public TileType TileType { get { return _tileType; } set { SetProperty(ref _tileType, value); } }
    }
    public  class CoverModel : BindableBase
    {
        public ObservableCollection<CoverData> Tiles { get; } = new ObservableCollection<CoverData>();
        public CoverModel()
        {
            Tiles.Add(new CoverData { Text = "Mortor", Image = new BitmapImage(new Uri(@"/Resource;component/Images/AutoTest.png", UriKind.Relative)), TileType = TileType.Mortor });
            Tiles.Add(new CoverData { Text = "Head", Image = new BitmapImage(new Uri(@"/Resource;component/Images/AutoTest.png", UriKind.Relative)), TileType = TileType.Head });
            Tiles.Add(new CoverData { Text = "Arm", Image = new BitmapImage(new Uri(@"/Resource;component/Images/AutoTest.png", UriKind.Relative)), TileType = TileType.Arm });
            Tiles.Add(new CoverData { Text = "Leg", Image = new BitmapImage(new Uri(@"/Resource;component/Images/AutoTest.png", UriKind.Relative)), TileType = TileType.Leg });
            Tiles.Add(new CoverData { Text = "Body", Image = new BitmapImage(new Uri(@"/Resource;component/Images/AutoTest.png", UriKind.Relative)), TileType = TileType.Body });
            Tiles.Add(new CoverData { Text = "Hand", Image = new BitmapImage(new Uri(@"/Resource;component/Images/AutoTest.png", UriKind.Relative)), TileType = TileType.Hand });
            Tiles.Add(new CoverData { Text = "LLB", Image = new BitmapImage(new Uri(@"/Resource;component/Images/AutoTest.png", UriKind.Relative)), TileType = TileType.LLB });
            Tiles.Add(new CoverData { Text = "Network", Image = new BitmapImage(new Uri(@"/Resource;component/Images/AutoTest.png", UriKind.Relative)), TileType = TileType.Network });
            Tiles.Add(new CoverData { Text = "Camera", Image = new BitmapImage(new Uri(@"/Resource;component/Images/AutoTest.png", UriKind.Relative)), TileType = TileType.Camera });
            Tiles.Add(new CoverData { Text = "Lidar", Image = new BitmapImage(new Uri(@"/Resource;component/Images/AutoTest.png", UriKind.Relative)), TileType = TileType.Lidar });
        }
    }
}
