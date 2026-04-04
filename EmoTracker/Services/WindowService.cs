using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace EmoTracker.Services
{
    public static class WindowService
    {
        private static IWindowService mInstance = new WpfWindowService();
        public static IWindowService Instance => mInstance;
        public static void SetBackend(IWindowService service) { mInstance = service; }
    }

    public class WpfWindowService : IWindowService
    {
        public double MainWindowWidth
        {
            get => Application.Current.MainWindow.Width;
            set => Application.Current.MainWindow.Width = value;
        }

        public double MainWindowHeight
        {
            get => Application.Current.MainWindow.Height;
            set => Application.Current.MainWindow.Height = value;
        }

        public void FocusMainWindow()
        {
            Keyboard.Focus(Application.Current.MainWindow);
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
