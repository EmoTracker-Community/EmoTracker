#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using EmoTracker.Services.Dialogs;

namespace EmoTracker.Services
{
    public static class DialogService
    {
        private static IDialogService mInstance =
            new AvaloniaDialogService();
        public static IDialogService Instance => mInstance;
        public static void SetBackend(IDialogService service) { mInstance = service; }
    }

    /// <summary>
    /// Avalonia dialog service: in-house <see cref="MessageDialog"/> for
    /// message boxes (replacing MsBox.Avalonia, which crashed on Ctrl+C
    /// from a modal — issue #68) and Avalonia <see cref="IStorageProvider"/>
    /// for file pickers.
    /// </summary>
    public class AvaloniaDialogService : IDialogService
    {
        private static Window? GetMainWindow() =>
            (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        // Sync methods are no-ops by design. The interface keeps them for API
        // compat, but a synchronous modal would block the UI thread (and only
        // a handful of pre-async call sites still use them). Async variants
        // are the supported entry points; sync calls are observably no-ops
        // exactly as they were under MsBox.Avalonia.
        public bool? ShowYesNoCancel(string title, string message) => true;
        public bool ShowYesNo(string title, string message, bool defaultYes = true) => defaultYes;
        public void ShowOK(string title, string message) { }
        public string OpenFile(string filter, string initialDirectory) => null;
        public string SaveFile(string filter, string initialDirectory) => null;

        public async Task<bool?> ShowYesNoCancelAsync(string title, string message)
        {
            var result = await MessageDialog.ShowAsync(
                GetMainWindow(), title, message, MessageDialogButtons.YesNoCancel);
            return result switch
            {
                MessageDialogResult.Yes => (bool?)true,
                MessageDialogResult.No  => false,
                _                       => null,   // Cancel / Esc / suppressed
            };
        }

        public async Task<bool> ShowYesNoAsync(string title, string message, bool defaultYes = true)
        {
            var result = await MessageDialog.ShowAsync(
                GetMainWindow(), title, message, MessageDialogButtons.YesNo, defaultYes);
            return result == MessageDialogResult.Yes;
        }

        public async Task ShowOKAsync(string title, string message)
        {
            await MessageDialog.ShowAsync(
                GetMainWindow(), title, message, MessageDialogButtons.Ok);
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
}
