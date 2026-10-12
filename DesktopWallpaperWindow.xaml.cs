using HenshinGrid.Services;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace HenshinGrid;

public partial class DesktopWallpaperWindow : Window
{
    private bool _isVideo;
    public DesktopWallpaperWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            if (!DesktopShell.TryAttach(this))
            {
                LastAttachSucceeded = false;
            }
            else LastAttachSucceeded = true;
        };
    }

    public bool LastAttachSucceeded { get; private set; }

    public void LoadWallpaper(string path, bool muted)
    {
        DesktopVideo.Stop();
        DesktopVideo.Source = null;
        DesktopImage.Source = null;
        _isVideo = false;
        if (!File.Exists(path)) return;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".mp4" or ".wmv" or ".avi" or ".m4v")
        {
            _isVideo = true;
            DesktopVideo.IsMuted = muted;
            DesktopVideo.Source = new Uri(path);
            DesktopVideo.Play();
        }
        else
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.UriSource = new Uri(path);
            img.EndInit();
            DesktopImage.Source = img;
        }
    }

    public void ApplyStyle(bool oled, bool showHologram, string accentHex, int dim)
    {
        Background = new SolidColorBrush(oled ? Colors.Black : (Color)System.Windows.Media.ColorConverter.ConvertFromString("#071019"));
        DesktopShade.Fill = new SolidColorBrush(oled ? Colors.Black : (Color)System.Windows.Media.ColorConverter.ConvertFromString("#081522"));
        DesktopShade.Opacity = Math.Clamp(dim / 100d, 0, 1);
        DesktopHud.Visibility = showHologram ? Visibility.Visible : Visibility.Collapsed;
        try
        {
            Color c = (Color)System.Windows.Media.ColorConverter.ConvertFromString(accentHex);
            foreach (var child in DesktopHud.Children)
                if (child is System.Windows.Shapes.Ellipse ellipse) ellipse.Stroke = new SolidColorBrush(c);
        }
        catch { }
    }

    public void PauseWallpaper() { if (_isVideo) DesktopVideo.Pause(); }
    public void ResumeWallpaper() { if (_isVideo) DesktopVideo.Play(); }
    private void DesktopVideo_MediaEnded(object sender, RoutedEventArgs e)
    {
        DesktopVideo.Position = TimeSpan.Zero;
        DesktopVideo.Play();
    }
    private void DesktopVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        DesktopVideo.Stop();
    }

    protected override void OnClosed(EventArgs e)
    {
        DesktopVideo.Stop();
        base.OnClosed(e);
    }
}