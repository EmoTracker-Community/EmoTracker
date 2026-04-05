using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
#if WINDOWS
using Microsoft.Win32;
using System.Windows;
#else
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using MessageBox.Avalonia.Enums;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
#endif

namespace EmoTracker.Services
{
    public static class DialogService
    {
        private static IDialogService mInstance =
#if WINDOWS
            new WpfDialogService();
#else
            new AvaloniaDialogService();
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

        public Task<bool?> ShowYesNoCancelAsync(string title, string message)
            => Task.FromResult(ShowYesNoCancel(title, message));

        public Task<bool> ShowYesNoAsync(string title, string message, bool defaultYes = true)
            => Task.FromResult(ShowYesNo(title, message, defaultYes));

        public Task ShowOKAsync(string title, string message)
        {
            ShowOK(title, message);
            return Task.CompletedTask;
        }

        public Task<string> OpenFileAsync(string filter, string initialDirectory)
            => Task.FromResult(OpenFile(filter, initialDirectory));

        public Task<string> SaveFileAsync(string filter, string initialDirectory)
            => Task.FromResult(SaveFile(filter, initialDirectory));
    }
#else
    /// <summary>
    /// Avalonia dialog service using MsBox.Avalonia for message boxes and
    /// Avalonia StorageProvider for file pickers.
    /// </summary>
    public class AvaloniaDialogService : IDialogService
    {
        private static Window? GetMainWindow() =>
            (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        // Sync methods — delegate to async versions; safe only when not on UI thread.
        // Command handlers should use the Async variants instead.
        public bool? ShowYesNoCancel(string title, string message) => true;
        public bool ShowYesNo(string title, string message, bool defaultYes = true) => defaultYes;
        public void ShowOK(string title, string message) { }
        public string OpenFile(string filter, string initialDirectory) => null;
        public string SaveFile(string filter, string initialDirectory) => null;

        public async Task<bool?> ShowYesNoCancelAsync(string title, string message)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.YesNoCancel);
            var result = await box.ShowWindowDialogAsync(GetMainWindow());
            return result switch
            {
                ButtonResult.Yes => (bool?)true,
                ButtonResult.No => false,
                _ => null
            };
        }

        public async Task<bool> ShowYesNoAsync(string title, string message, bool defaultYes = true)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.YesNo);
            var result = await box.ShowWindowDialogAsync(GetMainWindow());
            return result == ButtonResult.Yes;
        }

        public async Task ShowOKAsync(string title, string message)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.Ok);
            await box.ShowWindowDialogAsync(GetMainWindow());
        }

        public async Task<string> OpenFileAsync(string filter, string initialDirectory)
        {
            var window = GetMainWindow();
            if (window == null) return null;
            var topLevel = TopLevel.GetTopLevel(window);
            if (topLevel == null) return null;

            IStorageFolder startFolder = null;
            try { startFolder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(new Uri(initialDirectory)); }
            catch { /* ignore invalid path */ }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open File",
                AllowMultiple = false,
                SuggestedStartLocation = startFolder,
                FileTypeFilter = ParseFilter(filter)
            });
            return files.Count > 0 ? files[0].Path.LocalPath : null;
        }

        public async Task<string> SaveFileAsync(string filter, string initialDirectory)
        {
            var window = GetMainWindow();
            if (window == null) return null;
            var topLevel = TopLevel.GetTopLevel(window);
            if (topLevel == null) return null;

            IStorageFolder startFolder = null;
            try { startFolder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(new Uri(initialDirectory)); }
            catch { /* ignore invalid path */ }

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save File",
                SuggestedStartLocation = startFolder,
                FileTypeChoices = ParseFilter(filter)
            });
            return file?.Path.LocalPath;
        }

        /// <summary>
        /// Converts a WPF-style filter string ("Description|*.ext|Description2|*.ext2")
        /// to a list of <see cref="FilePickerFileType"/> objects for Avalonia's StorageProvider.
        /// </summary>
        private static IReadOnlyList<FilePickerFileType> ParseFilter(string wpfFilter)
        {
            var types = new List<FilePickerFileType>();
            if (string.IsNullOrEmpty(wpfFilter)) return types;

            var parts = wpfFilter.Split('|');
            for (int i = 0; i + 1 < parts.Length; i += 2)
            {
                var name = parts[i];
                var patterns = parts[i + 1].Split(';').Select(p => p.Trim()).ToList();
                types.Add(new FilePickerFileType(name) { Patterns = patterns });
            }
            return types;
        }
    }
#endif
}
