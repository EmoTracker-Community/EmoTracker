#nullable enable annotations
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.UI.Media.Utility;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace EmoTracker.UI
{
    /// <summary>
    /// Interaction logic for OverrideExportDialog.axaml
    /// </summary>
    public partial class OverrideExportDialog : Window
    {
        // ------------------------------------------------------------------
        // Inner record type
        // ------------------------------------------------------------------

        public class FileRecord : EmoTracker.Core.ObservableObject
        {
            string mPath;
            bool mbExportOverride = false;
            IImage? mPreviewSource;
            string? mToolTip;

            public bool ExportOverride
            {
                get => mbExportOverride;
                set => SetProperty(ref mbExportOverride, value);
            }

            public string Path => mPath;

            public FileRecord(string path)
            {
                mPath = path;
            }

            /// <summary>
            /// Image source for the file preview icon.
            /// </summary>
            public IImage? PreviewSource
            {
                get => mPreviewSource;
                set => SetProperty(ref mPreviewSource, value);
            }

            /// <summary>
            /// Tooltip string shown next to the path.
            /// </summary>
            public string? ToolTip
            {
                get => mToolTip;
                set => SetProperty(ref mToolTip, value);
            }
        }

        // ------------------------------------------------------------------
        // Converters
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns a highlight background when <c>ExportOverride</c> is true.
        /// </summary>
        public static readonly IValueConverter ExportOverrideToBackgroundConverter =
            new FuncValueConverter<bool, IBrush>(v =>
                v ? new SolidColorBrush(Color.Parse("#353535")) : Brushes.Transparent);

        /// <summary>
        /// Returns bright-green foreground when <c>ExportOverride</c> is true.
        /// </summary>
        public static readonly IValueConverter ExportOverrideToForegroundConverter =
            new FuncValueConverter<bool, IBrush>(v =>
                v ? new SolidColorBrush(Color.Parse("#00ff00")) : Brushes.WhiteSmoke);

        // ------------------------------------------------------------------
        // State
        // ------------------------------------------------------------------

        private readonly List<FileRecord> mFileRecords;
        private readonly ObservableCollection<FileRecord> mFilteredRecords;

        // Backing store for FilterText so we can refresh on change.
        private string? mFilterText;

        public ObservableCollection<FileRecord> Records => mFilteredRecords;

        public string? FilterText
        {
            get => mFilterText;
            set
            {
                mFilterText = value;
                RefreshFilter();
            }
        }

        // ------------------------------------------------------------------
        // Constructor
        // ------------------------------------------------------------------

        public OverrideExportDialog()
        {
            mFileRecords = new List<FileRecord>();

            if (Tracker.Instance.ActiveGamePackage != null)
            {
                foreach (string file in Tracker.Instance.ActiveGamePackage.Source.Files)
                {
                    mFileRecords.Add(new FileRecord(file));
                }
            }

            mFilteredRecords = new ObservableCollection<FileRecord>(mFileRecords);

            InitializeComponent();
            DataContext = this;

            // Load file icons asynchronously so the window opens quickly
            _ = LoadFileIconsAsync();
        }

        private async Task LoadFileIconsAsync()
        {
            var package = Tracker.Instance.ActiveGamePackage;
            if (package == null) return;

            foreach (var record in mFileRecords)
            {
                var file = record.Path;
                IImage? img = null;
                bool isImagePreview = false;

                await Task.Run(() =>
                {
                    img = IconUtility.GetImage(
                        package.Open(file, true, true));
                    isImagePreview = img != null;

                    if (img == null)
                    {
                        string ext = System.IO.Path.GetExtension(file)
                            .TrimStart('.')
                            .ToLowerInvariant();
                        img = IconUtility.GetImage(
                            new Uri($"avares://EmoTracker/Resources/filetype_{ext}.png"));
                    }
                });

                if (img != null)
                {
                    record.PreviewSource = img;
                    if (isImagePreview)
                        record.ToolTip = file;
                }
            }
        }

        // ------------------------------------------------------------------
        // Filter logic
        // ------------------------------------------------------------------

        private void RefreshFilter()
        {
            var filtered = string.IsNullOrWhiteSpace(mFilterText)
                ? mFileRecords
                : mFileRecords.Where(r =>
                    r.Path.Contains(mFilterText, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            mFilteredRecords.Clear();
            foreach (var record in filtered)
                mFilteredRecords.Add(record);
        }

        // ------------------------------------------------------------------
        // Button handler
        // ------------------------------------------------------------------

        private void ExportOverridesButton_Click(object? sender, RoutedEventArgs e)
        {
            foreach (FileRecord record in mFileRecords)
            {
                if (record.ExportOverride)
                {
                    Tracker.Instance.ActiveGamePackage?.ExportUserOverride(record.Path);
                    Close();
                }
            }
        }
    }
}
