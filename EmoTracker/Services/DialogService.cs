#if WINDOWS
using Microsoft.Win32;
using System.Windows;
#endif

namespace EmoTracker.Services
{
    public static class DialogService
    {
        private static IDialogService mInstance =
#if WINDOWS
            new WpfDialogService();
#else
            new HeadlessDialogService();
#endif
        public static IDialogService Instance => mInstance;
        public static void SetBackend(IDialogService service) { mInstance = service; }
    }

#if WINDOWS
    public class WpfDialogService : IDialogService
    {
        public bool? ShowYesNoCancel(string title, string message)
        {
            var result = MessageBox.Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Exclamation);
            return result switch
            {
                MessageBoxResult.Yes => true,
                MessageBoxResult.No => false,
                _ => null
            };
        }

        public bool ShowYesNo(string title, string message, bool defaultYes = true)
        {
            var defaultButton = defaultYes ? MessageBoxResult.Yes : MessageBoxResult.No;
            var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, defaultButton);
            return result == MessageBoxResult.Yes;
        }

        public void ShowOK(string title, string message)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public string OpenFile(string filter, string initialDirectory)
        {
            var dialog = new OpenFileDialog { Filter = filter, InitialDirectory = initialDirectory };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public string SaveFile(string filter, string initialDirectory)
        {
            var dialog = new SaveFileDialog
            {
                Filter = filter,
                InitialDirectory = initialDirectory,
                AddExtension = true,
                CheckPathExists = true
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
#else
    /// <summary>
    /// Fallback dialog service for the cross-platform Avalonia target.
    /// Replaced by a proper Avalonia StorageProvider + MsBox implementation in Phase 7.
    /// </summary>
    public class HeadlessDialogService : IDialogService
    {
        public bool? ShowYesNoCancel(string title, string message) => true;
        public bool ShowYesNo(string title, string message, bool defaultYes = true) => defaultYes;
        public void ShowOK(string title, string message) { }
        public string OpenFile(string filter, string initialDirectory) => null;
        public string SaveFile(string filter, string initialDirectory) => null;
    }
#endif
}
