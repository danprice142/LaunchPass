using RetroPass;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace LaunchPass
{
    public sealed partial class LPMediaElement : UserControl
    {
        static string[] imageExt = new string[] { ".png", ".jpg", ".jxr", ".wdp", ".dds", ".jpeg", ".webp" };
        static string[] videoExt = new string[] { ".mp4", ".mov", ".webp", ".webm", ".mkv" };

        private Dictionary<string, Uri> mediaCache = new Dictionary<string, Uri>();

        public string MediaPath
        {
            get => (string)GetValue(MediaPathProperty);
            set => SetValue(MediaPathProperty, value);
        }

        // Using a DependencyProperty as the backing store for MediaPath.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty MediaPathProperty =
            DependencyProperty.Register("MediaPath", typeof(string), typeof(LPMediaElement), new PropertyMetadata(string.Empty, OnMediaPathChangedCallBack));

        private async static void OnMediaPathChangedCallBack(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            LPMediaElement element = sender as LPMediaElement;
            string path = Convert.ToString(e.NewValue);

            if (element != null && !string.IsNullOrEmpty(path))
            {
                string extension = Path.GetExtension(path);

                if (imageExt.Contains(extension))
                {
                    element.image1.Source = await ThumbnailCache.Instance.GetImageFromPath(path);
                    element.mediaElement1.Source = null;

                    element.image1.Visibility = Visibility.Visible;
                    element.mediaElement1.Visibility = Visibility.Collapsed;
                }
                else if (videoExt.Contains(extension))
                {
                    element.image1.Source = null;

                    // Check if media is already in cache
                    if (element.mediaCache.TryGetValue(path, out Uri cachedUri))
                    {
                        // If media is in cache, set the source from cache
                        element.mediaElement1.Source = cachedUri;
                    }
                    else
                    {
                        // If media is not in cache, load it into cache and set the source
                        await element.LoadMediaIntoCache(element, path);
                        element.mediaElement1.Source = element.mediaCache[path];

                        element.image1.Visibility = Visibility.Collapsed;
                        element.mediaElement1.Visibility = Visibility.Visible;
                    }
                }
            }
        }

        private async ValueTask LoadMediaIntoCache(LPMediaElement element, string path)
        {
            await Task.Run(() =>
            {
                var file = StorageFile.GetFileFromPathAsync(path).AsTask().Result;
                var stream = file.OpenAsync(FileAccessMode.Read).AsTask().Result;
                element.mediaCache[path] = new Uri(path);
            });
        }

        public LPMediaElement()
        {
            this.InitializeComponent();
        }
    }
}