using System.Threading.Tasks;

namespace EmoTracker.Services
{
    public interface IDialogService
    {
        /// <summary>Shows a Yes/No/Cancel dialog. Returns true=Yes, false=No, null=Cancel.</summary>
        bool? ShowYesNoCancel(string title, string message);
        /// <summary>Shows a Yes/No dialog. Returns true if the user chose Yes.</summary>
        bool ShowYesNo(string title, string message, bool defaultYes = true);
        /// <summary>Shows an OK dialog (errors/info).</summary>
        void ShowOK(string title, string message);
        /// <summary>Shows an open-file picker. Returns the chosen path, or null if cancelled.</summary>
        string OpenFile(string filter, string initialDirectory);
        /// <summary>Shows a save-file picker. Returns the chosen path, or null if cancelled.</summary>
        string SaveFile(string filter, string initialDirectory);

        // Async variants — preferred for Avalonia targets
        System.Threading.Tasks.Task<bool?> ShowYesNoCancelAsync(string title, string message);
        System.Threading.Tasks.Task<bool> ShowYesNoAsync(string title, string message, bool defaultYes = true);
        System.Threading.Tasks.Task ShowOKAsync(string title, string message);
        System.Threading.Tasks.Task<string> OpenFileAsync(string filter, string initialDirectory);
        System.Threading.Tasks.Task<string> SaveFileAsync(string filter, string initialDirectory);
    }
}
