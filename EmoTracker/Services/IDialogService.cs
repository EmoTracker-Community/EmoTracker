namespace EmoTracker.Services
{
    public interface IDialogService
    {
        /// <summary>
        /// Shows a Yes/No/Cancel dialog. Returns true=Yes, false=No, null=Cancel.
        /// </summary>
        bool? ShowYesNoCancel(string title, string message);

        /// <summary>
        /// Shows a Yes/No dialog. Returns true if the user chose Yes.
        /// </summary>
        bool ShowYesNo(string title, string message, bool defaultYes = true);

        /// <summary>
        /// Shows a dialog with an OK button (used for errors and informational messages).
        /// </summary>
        void ShowOK(string title, string message);

        /// <summary>
        /// Shows an open-file picker. Returns the chosen path, or null if cancelled.
        /// </summary>
        string OpenFile(string filter, string initialDirectory);

        /// <summary>
        /// Shows a save-file picker. Returns the chosen path, or null if cancelled.
        /// </summary>
        string SaveFile(string filter, string initialDirectory);
    }
}
