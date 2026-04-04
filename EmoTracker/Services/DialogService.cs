using Microsoft.Win32;
using System.Windows;

namespace EmoTracker.Services
{
    public static class DialogService
    {
        private static IDialogService mInstance = new WpfDialogService();
        public static IDialogService Instance => mInstance;
        public static void SetBackend(IDialogService service) { mInstance = service; }
    }

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
}
