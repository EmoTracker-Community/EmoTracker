using System;
using System.Diagnostics;
using Avalonia.Controls.ApplicationLifetimes;

namespace EmoTracker.Services
{
    public static class WindowService
    {
        private static IWindowService mInstance =
            new AvaloniaWindowService();
        public static IWindowService Instance => mInstance;
        public static void SetBackend(IWindowService service) { mInstance = service; }
    }

    public class AvaloniaWindowService : IWindowService
    {
        private static Avalonia.Controls.Window MainWindow =>
            (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        public double MainWindowWidth
        {
            get => MainWindow?.Width ?? 0;
            set { if (MainWindow != null) MainWindow.Width = value; }
        }

        public double MainWindowHeight
        {
            get => MainWindow?.Height ?? 0;
            set { if (MainWindow != null) MainWindow.Height = value; }
        }

        public void FocusMainWindow()
        {
            MainWindow?.Focus();
        }

        public void OpenFolder(string path)
        {
            if (OperatingSystem.IsWindows())
                Process.Start("explorer.exe", path);
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", path);
            else
                Process.Start("xdg-open", path);
        }

        public void OpenUrl(string url)
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", url);
            else
                Process.Start("xdg-open", url);
        }
    }
}
