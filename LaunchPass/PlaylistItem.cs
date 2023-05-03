using System.ComponentModel;
using Windows.Graphics.Imaging;
using Windows.UI.Xaml.Media.Imaging;

namespace RetroPass
{
    public class PlaylistItem :INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyyChanged(string propertyname)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyname));
            }
        }

        public Playlist playlist { get; set; }
        public Game game;

        private BitmapImage image;
        public BitmapImage bitmapImage 
        {
            get
            {
                return image;
            }
            set
            {
                image = value;
                OnPropertyyChanged("bitmapImage");
            }
        }
    }
}