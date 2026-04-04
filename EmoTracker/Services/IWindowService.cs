namespace EmoTracker.Services
{
    public interface IWindowService
    {
        double MainWindowWidth { get; set; }
        double MainWindowHeight { get; set; }

        /// <summary>Returns keyboard focus to the main application window.</summary>
        void FocusMainWindow();

        /// <summary>Opens a folder in the platform file manager.</summary>
        void OpenFolder(string path);

        /// <summary>Opens a URL in the default browser.</summary>
        void OpenUrl(string url);
    }
}
