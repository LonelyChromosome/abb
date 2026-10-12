using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HenshinGrid.Models;
using HenshinGrid.Services;
using Microsoft.Win32;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace HenshinGrid;

public partial class MainWindow : Window
{
    private AppSettings _settings = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly DispatcherTimer _rescanTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private DesktopWallpaperWindow? _desktopWindow;
    private bool _ready;
    private bool _favoritesOnly;

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hWnd, out NativeRect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hWnd, System.Text.StringBuilder className, int maxCount);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }

    public MainWindow()
    {
        InitializeComponent();
        _rescanTimer.Tick += (_, _) => { _rescanTimer.Stop(); _ = DiscoverNewGamesAsync(); };
        _fullscreenTimer.Tick += (_, _) => UpdateBackgroundPlayback();
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            foreach (var watcher in _watchers) watcher.Dispose();
            _watchers.Clear();
            _rescanTimer.Stop();
            _fullscreenTimer.Stop();
            _desktopWindow?.Close();
        };
        StateChanged += (_, _) => UpdateBackgroundPlayback();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings = LibraryStorage.Load();
        OledCheck.IsChecked = _settings.OledBlack;
        HologramCheck.IsChecked = _settings.ShowHologram;
        MuteCheck.IsChecked = _settings.MuteWallpaper;
        PauseFullscreenCheck.IsChecked = _settings.PauseWallpaperWhenFullscreen;
        DimSlider.Value = Math.Clamp(_settings.WallpaperDimPercent, 0, 100);
        bool presetSelected = false;
        foreach (var item in AccentPicker.Items.OfType<ComboBoxItem>())
        {
            if (!string.Equals(item.Tag?.ToString(), _settings.AccentHex, StringComparison.OrdinalIgnoreCase)) continue;
            AccentPicker.SelectedItem = item;
            presetSelected = true;
            break;
        }
        if (!presetSelected) AccentPicker.SelectedIndex = -1;
        _ready = true;
        ApplyTheme();
        LoadLauncherWallpaper();
        RenderGames();
        if (_settings.NativeDesktopWallpaper) StartDesktopWallpaper();
        _fullscreenTimer.Start();
        await DiscoverNewGamesAsync();
        CreateWatchers();
    }

    private async Task DiscoverNewGamesAsync()
    {
        SidebarStatus.Text = "ĐANG ĐỌC THƯ VIỆN STEAM / EPIC...";
        List<GameEntry> found;
        try { found = await Task.Run(() => GameDiscovery.Discover().ToList()); }
        catch (Exception ex) { SetStatus("Không thể quét thư viện: " + ex.Message); return; }
        var existing = new HashSet<string>(_settings.Games.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
        int added = 0;
        foreach (var entry in found)
        {
            if (!existing.Add(entry.Id)) continue;
            _settings.Games.Add(entry);
            added++;
        }
        _settings.FirstScanCompleted = true;
        Persist();
        RenderGames();
        SetStatus(added == 0 ? $"SẴN SÀNG  /  {_settings.Games.Count} GAME" : $"ĐÃ THÊM {added} GAME MỚI");
    }

    private void CreateWatchers()
    {
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
        foreach (var folder in GameDiscovery.SteamManifestDirectories().Distinct(StringComparer.OrdinalIgnoreCase))
            WatchFolder(folder, "appmanifest_*.acf");
        WatchFolder(GameDiscovery.EpicManifestDirectory, "*.item");
        foreach (string root in GameDiscovery.SteamRoots())
            WatchSpecificFile(Path.Combine(root, "steamapps"), "libraryfolders.vdf");
    }

    private void WatchFolder(string folder, string filter)
    {
        if (!Directory.Exists(folder)) return;
        try
        {
            var watcher = new FileSystemWatcher(folder, filter)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            watcher.Created += OnInstallManifestChanged;
            watcher.Changed += OnInstallManifestChanged;
            watcher.Renamed += OnManifestRenamed;
            _watchers.Add(watcher);
        }
        catch { /* Access restrictions: next application startup still rescans. */ }
    }

    private void WatchSpecificFile(string folder, string fileName) => WatchFolder(folder, fileName);

    private void OnInstallManifestChanged(object sender, FileSystemEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => { if (_ready) { _rescanTimer.Stop(); _rescanTimer.Start(); } }));

    private void OnManifestRenamed(object sender, RenamedEventArgs e) => Dispatcher.BeginInvoke(new Action(() =>
    {
        CreateWatchers();
        _rescanTimer.Stop();
        _rescanTimer.Start();
    }));

    private void RenderGames()
    {
        string q = SearchBox.Text.Trim();
        var visible = _settings.Games.Where(x => (!_favoritesOnly || x.Favorite) &&
                 (q.Length == 0 || x.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || x.Source.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.Favorite).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        foreach (var game in visible) if (game.Icon is null) TryLoadGameIcon(game);
        GameItems.ItemsSource = visible;
        CountText.Text = $"{visible.Count} TITLES  //  {(_favoritesOnly ? "FAVORITES" : "READY TO LAUNCH")}";
    }

    private static void TryLoadGameIcon(GameEntry game)
    {
        try
        {
            if (!string.IsNullOrEmpty(game.ArtPath) && File.Exists(game.ArtPath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(Path.GetFullPath(game.ArtPath));
                bitmap.EndInit();
                bitmap.Freeze();
                game.Icon = bitmap;
                return;
            }
            string exe = game.ExecutablePath;
            if (!File.Exists(exe)) return;
            using System.Drawing.Icon? icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
            if (icon is null) return;
            var result = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(96, 96));
            result.Freeze();
            game.Icon = result;
        }
        catch { /* Icon is optional: monogram fallback remains available. */ }
    }

    private void Persist()
    {
        try { LibraryStorage.Save(_settings); }
        catch (Exception ex) { SetStatus("KHÔNG LƯU ĐƯỢC THƯ VIỆN: " + ex.Message); }
    }
    private void SetStatus(string value) { SidebarStatus.Text = value; FooterText.Text = value; }

    private void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn file chạy của game",
            Filter = "Game (*.exe;*.lnk;*.url)|*.exe;*.lnk;*.url|Tất cả file (*.*)|*.*",
            Multiselect = false
        };
        if (dlg.ShowDialog() != true) return;
        string path = Path.GetFullPath(dlg.FileName);
        if (_settings.Games.Any(x => string.Equals(x.LaunchTarget, path, StringComparison.OrdinalIgnoreCase)))
        { SetStatus("GAME ĐÃ CÓ TRONG THƯ VIỆN"); return; }
        _settings.Games.Add(new GameEntry
        {
            Id = "manual:" + Guid.NewGuid().ToString("N"),
            Name = Path.GetFileNameWithoutExtension(path),
            Source = "Local",
            LaunchTarget = path,
            ExecutablePath = Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? path : "",
            WorkingDirectory = Path.GetDirectoryName(path) ?? ""
        });
        Persist(); RenderGames(); SetStatus("ĐÃ THÊM GAME THỦ CÔNG");
    }

    private void Play(GameEntry? game)
    {
        if (game is null) return;
        try
        {
            string target = game.LaunchTarget;
            if (string.IsNullOrWhiteSpace(target)) throw new InvalidOperationException("Thiếu đường dẫn khởi chạy.");
            bool uri = target.StartsWith("steam://", StringComparison.OrdinalIgnoreCase) ||
                       target.StartsWith("com.epicgames.launcher://", StringComparison.OrdinalIgnoreCase);
            if (!uri && !File.Exists(target)) throw new FileNotFoundException("Không tìm thấy file game.", target);
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                WorkingDirectory = Directory.Exists(game.WorkingDirectory) ? game.WorkingDirectory : "",
                UseShellExecute = true
            });
            SetStatus("ĐÃ GỬI LỆNH MỞ: " + game.Name);
            WindowState = WindowState.Minimized;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Không mở được {game.Name}:\n{ex.Message}", "Henshin Grid", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void GameTile_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // A tile click opens the game only when the click did not originate on a nested button.
        if (e.OriginalSource is DependencyObject origin &&
            FindAncestor<System.Windows.Controls.Button>(origin) is not null) return;
        Play((sender as FrameworkElement)?.Tag as GameEntry);
    }
    private static T? FindAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        for (DependencyObject? n = child; n is not null; n = VisualTreeHelper.GetParent(n))
            if (n is T match) return match;
        return null;
    }
    private void GamePlay_Click(object sender, RoutedEventArgs e) => Play((sender as FrameworkElement)?.Tag as GameEntry);
    private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not GameEntry game) return;
        game.Favorite = !game.Favorite;
        Persist(); RenderGames();
    }
    private void ShowAll_Click(object sender, RoutedEventArgs e) { _favoritesOnly = false; RenderGames(); }
    private void ShowFavorites_Click(object sender, RoutedEventArgs e) { _favoritesOnly = true; RenderGames(); }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (_ready) RenderGames(); }
    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        await DiscoverNewGamesAsync();
        CreateWatchers();
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void DragRegion_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject node && FindAncestor<System.Windows.Controls.Button>(node) != null) return;
        if (e.ClickCount == 2) { Maximize_Click(sender, e); return; }
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void SelectWallpaper_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn hình hoặc video làm wallpaper",
            Filter = "Ảnh / Video|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif;*.mp4;*.wmv;*.avi;*.m4v|Tất cả file|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        _settings.WallpaperPath = dlg.FileName;
        LoadLauncherWallpaper();
        _desktopWindow?.LoadWallpaper(_settings.WallpaperPath, _settings.MuteWallpaper);
        Persist();
    }

    private void ClearWallpaper_Click(object sender, RoutedEventArgs e)
    {
        _settings.WallpaperPath = "";
        LoadLauncherWallpaper();
        _desktopWindow?.LoadWallpaper("", _settings.MuteWallpaper);
        Persist();
        SetStatus("ĐÃ CHUYỂN SANG NỀN ĐEN THUẦN");
    }

    private void LoadLauncherWallpaper()
    {
        WallpaperVideo.Stop();
        WallpaperVideo.Source = null;
        WallpaperVideo.Visibility = Visibility.Collapsed;
        WallpaperImage.Source = null;
        WallpaperImage.Visibility = Visibility.Collapsed;
        string path = _settings.WallpaperPath;
        if (!File.Exists(path)) { UpdateBackgroundPlayback(); return; }
        try
        {
            if (IsVideo(path))
            {
                WallpaperVideo.Source = new Uri(Path.GetFullPath(path));
                WallpaperVideo.IsMuted = _settings.MuteWallpaper;
                WallpaperVideo.Visibility = Visibility.Visible;
                UpdateBackgroundPlayback();
            }
            else
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(Path.GetFullPath(path));
                bitmap.EndInit();
                WallpaperImage.Source = bitmap;
                WallpaperImage.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex) { SetStatus("WALLPAPER KHÔNG HỖ TRỢ: " + ex.Message); }
    }
    private static bool IsVideo(string path) => new[] { ".mp4", ".wmv", ".avi", ".m4v" }
        .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    private void WallpaperVideo_MediaEnded(object sender, RoutedEventArgs e)
    {
        WallpaperVideo.Position = TimeSpan.Zero;
        UpdateBackgroundPlayback();
    }
    private void WallpaperVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e) =>
        SetStatus("KHÔNG PHÁT ĐƯỢC VIDEO: định dạng/codec không hỗ trợ");

    private void ApplyTheme()
    {
        var color = (Color)System.Windows.Media.ColorConverter.ConvertFromString(_settings.AccentHex);
        System.Windows.Application.Current.Resources["NeonCyan"] = new SolidColorBrush(color);
        Background = new SolidColorBrush(_settings.OledBlack ? Colors.Black : (Color)System.Windows.Media.ColorConverter.ConvertFromString("#071019"));
        WallpaperShade.Fill = new SolidColorBrush(_settings.OledBlack ? Colors.Black : (Color)System.Windows.Media.ColorConverter.ConvertFromString("#081522"));
        WallpaperShade.Opacity = Math.Clamp(_settings.WallpaperDimPercent / 100d, 0, 1);
        HologramCanvas.Visibility = _settings.ShowHologram ? Visibility.Visible : Visibility.Collapsed;
        DataGridOverlay.Visibility = _settings.ShowHologram ? Visibility.Visible : Visibility.Collapsed;
        _desktopWindow?.ApplyStyle(_settings.OledBlack, _settings.ShowHologram, _settings.AccentHex, _settings.WallpaperDimPercent);
    }

    private void OledCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.OledBlack = OledCheck.IsChecked == true; ApplyTheme(); Persist();
    }
    private void HologramCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.ShowHologram = HologramCheck.IsChecked == true; ApplyTheme(); Persist();
    }
    private void DimSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        _settings.WallpaperDimPercent = (int)Math.Round(DimSlider.Value);
        ApplyTheme(); Persist();
    }
    private void AccentPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || AccentPicker.SelectedItem is not ComboBoxItem item) return;
        string hex = item.Tag?.ToString() ?? "";
        if (!System.Text.RegularExpressions.Regex.IsMatch(hex, "^#[0-9a-fA-F]{6}$")) return;
        _settings.AccentHex = hex;
        ApplyTheme(); Persist();
    }
    private void CustomColor_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        _settings.AccentHex = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
        AccentPicker.SelectedIndex = -1;
        ApplyTheme(); Persist();
    }
    private void MuteCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.MuteWallpaper = MuteCheck.IsChecked == true;
        WallpaperVideo.IsMuted = _settings.MuteWallpaper;
        if (_desktopWindow is not null) _desktopWindow.LoadWallpaper(_settings.WallpaperPath, _settings.MuteWallpaper);
        Persist();
    }
    private void PauseFullscreenCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.PauseWallpaperWhenFullscreen = PauseFullscreenCheck.IsChecked == true;
        UpdateBackgroundPlayback(); Persist();
    }

    private void EnableDesktop_Click(object sender, RoutedEventArgs e)
    {
        StartDesktopWallpaper();
        Persist();
    }
    private void StartDesktopWallpaper()
    {
        if (_desktopWindow is not null) return;
        try
        {
            var window = new DesktopWallpaperWindow();
            window.Show();
            if (!window.LastAttachSucceeded)
            {
                window.Close();
                _settings.NativeDesktopWallpaper = false;
                SetStatus("KHÔNG GHÉP ĐƯỢC VÀO DESKTOP; chỉ dùng nền trong launcher");
                return;
            }
            _desktopWindow = window;
            _settings.NativeDesktopWallpaper = true;
            _desktopWindow.ApplyStyle(_settings.OledBlack, _settings.ShowHologram, _settings.AccentHex, _settings.WallpaperDimPercent);
            _desktopWindow.LoadWallpaper(_settings.WallpaperPath, _settings.MuteWallpaper);
            UpdateBackgroundPlayback();
            SetStatus("WALLPAPER DESKTOP ĐÃ BẬT / MÀN HÌNH CHÍNH");
        }
        catch (Exception ex)
        {
            _settings.NativeDesktopWallpaper = false;
            SetStatus("WALLPAPER DESKTOP KHÔNG KHẢ DỤNG: " + ex.Message);
        }
    }
    private void DisableDesktop_Click(object sender, RoutedEventArgs e)
    {
        _desktopWindow?.Close();
        _desktopWindow = null;
        _settings.NativeDesktopWallpaper = false;
        Persist();
        SetStatus("ĐÃ TẮT WALLPAPER DESKTOP");
    }
    private void UpdateBackgroundPlayback()
    {
        if (WallpaperVideo.Source is not null)
        {
            if (WindowState == WindowState.Minimized) WallpaperVideo.Pause();
            else WallpaperVideo.Play();
        }
        if (_desktopWindow is null) return;
        if (_settings.PauseWallpaperWhenFullscreen && HasOtherFullscreenWindow()) _desktopWindow.PauseWallpaper();
        else _desktopWindow.ResumeWallpaper();
    }

    private bool HasOtherFullscreenWindow()
    {
        nint foreground = GetForegroundWindow();
        if (foreground == 0 || foreground == new WindowInteropHelper(this).Handle) return false;
        if (_desktopWindow != null && foreground == new WindowInteropHelper(_desktopWindow).Handle) return false;
        var sb = new System.Text.StringBuilder(128);
        GetClassName(foreground, sb, sb.Capacity);
        if (sb.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        if (!GetWindowRect(foreground, out var rect)) return false;
        var monitor = System.Windows.Forms.Screen.FromHandle(foreground).Bounds;
        return rect.Left <= monitor.Left + 3 && rect.Top <= monitor.Top + 3 &&
               rect.Right >= monitor.Right - 3 && rect.Bottom >= monitor.Bottom - 3;
    }
}