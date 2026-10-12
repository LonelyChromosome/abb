namespace HenshinGrid.Models;

public class AppSettings
{
    public bool FirstScanCompleted { get; set; }
    public string WallpaperPath { get; set; } = "";
    public bool NativeDesktopWallpaper { get; set; }
    public bool MuteWallpaper { get; set; } = true;
    public bool OledBlack { get; set; } = true;
    public bool ShowHologram { get; set; } = true;
    public string AccentHex { get; set; } = "#35F3E8";
    // 0 = wallpaper is completely visible, 100 = pure black above wallpaper.
    public int WallpaperDimPercent { get; set; } = 36;
    public bool PauseWallpaperWhenFullscreen { get; set; } = true;
    public List<GameEntry> Games { get; set; } = new();
}