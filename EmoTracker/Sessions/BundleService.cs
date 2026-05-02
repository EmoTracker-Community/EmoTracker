using Avalonia.Controls;
using System.Threading.Tasks;

// Avalonia 11 recommends StorageProvider, but the legacy dialogs still work.
// Phase 7.10 minimum: ship with the legacy API; a polish pass migrates.
#pragma warning disable CS0618

namespace EmoTracker
{
    /// <summary>
    /// Phase 7.10: bundle save / load — serialises every loaded
    /// PackageInstance + its TrackerStates into a folder + sibling
    /// <c>name.bundle.json</c>. The interactive variants present a file
    /// dialog and drive the save/load through ApplicationModel.
    ///
    /// <para>
    /// Phase 7.7 surfaces the Save Bundle / Load Bundle UI buttons; this
    /// class is the implementation behind them.
    /// </para>
    /// </summary>
    public static class BundleService
    {
        public static async Task SaveBundleInteractiveAsync(Window owner)
        {
            if (owner == null) return;
            var dlg = new SaveFileDialog
            {
                Title = "Save Bundle",
                DefaultExtension = "bundle.json",
                Filters = new System.Collections.Generic.List<FileDialogFilter>
                {
                    new FileDialogFilter { Name = "EmoTracker bundle", Extensions = { "bundle.json" } }
                }
            };
            var path = await dlg.ShowAsync(owner);
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                Bundle.Save(path);
            }
            catch (System.Exception ex)
            {
                ApplicationModel.Instance.PushMarkdownNotification(
                    EmoTracker.Data.Scripting.NotificationType.Error,
                    "Failed to save bundle: " + ex.Message);
            }
        }

        public static async Task LoadBundleInteractiveAsync(Window owner)
        {
            if (owner == null) return;
            var dlg = new OpenFileDialog
            {
                Title = "Load Bundle",
                AllowMultiple = false,
                Filters = new System.Collections.Generic.List<FileDialogFilter>
                {
                    new FileDialogFilter { Name = "EmoTracker bundle", Extensions = { "bundle.json" } }
                }
            };
            var paths = await dlg.ShowAsync(owner);
            if (paths == null || paths.Length == 0) return;
            try
            {
                Bundle.Load(paths[0]);
            }
            catch (System.Exception ex)
            {
                ApplicationModel.Instance.PushMarkdownNotification(
                    EmoTracker.Data.Scripting.NotificationType.Error,
                    "Failed to load bundle: " + ex.Message);
            }
        }
    }
}
