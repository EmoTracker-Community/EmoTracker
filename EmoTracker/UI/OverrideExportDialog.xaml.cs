using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.UI.Media.Utility;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace EmoTracker.UI
{
    /// <summary>
    /// Interaction logic for OverrideExportDialog.xaml
    /// </summary>
    public partial class OverrideExportDialog : Window
    {
        public class FileRecord : ObservableObject
        {
            string mPath;
            bool mbExportOverride = false;

            public bool ExportOverride
            {
                get { return mbExportOverride; }
                set { SetProperty(ref mbExportOverride, value); }
            }

            public string Path
            {
                get { return mPath; }
            }

            public FileRecord(string path)
            {
                mPath = path;
            }

            public FrameworkElement Preview
            {
                get;
                set;
            }

            public FrameworkElement ToolTip
            {
                get;
                set;
            }
        }

        List<FileRecord> mFileRecords;
        ListCollectionView mFileRecordsView;

        public CollectionView Records
        {
            get { return mFileRecordsView; }
        }

        string mFilterText;
        public string FilterText
        {
            get { return mFilterText; }
            set
            {
                mFilterText = value;
                mFileRecordsView.Refresh();
            }
        }

        public OverrideExportDialog()
        {
            mFileRecords = new List<FileRecord>();

            if (Tracker.Instance.ActiveGamePackage != null)
            {
                DropShadowEffect dropShadow = new DropShadowEffect() { ShadowDepth = 0, BlurRadius = 10 };
                SolidColorBrush ttBackground = new SolidColorBrush(Colors.Gray);

                foreach (string file in Tracker.Instance.ActiveGamePackage.Source.Files)
                {
                    FrameworkElement preview = null;
                    FrameworkElement tooltip = null;
                    {
                        ImageSource img = IconUtility.GetImage(Tracker.Instance.ActiveGamePackage.Open(file, true, true));
                        bool bIsImagePreview = img != null;

                        if (img == null)
                        {
                            // not a pack image; try getting the file icon
                            img = IconUtility.GetImage(new Uri(string.Format("pack://application:,,,/EmoTracker;component/Resources/filetype_{0}.png", System.IO.Path.GetExtension(file).TrimStart(new char[] { '.' }).ToLower())));
                        }

                        if (img != null)
                        {
                            preview = new Image() { Source = img };

                            if (bIsImagePreview)
                            {
                                Grid g = new Grid();
                                g.Children.Add(new Image() { Source = img });

                                ToolTip tt = new ToolTip();
                                tt.Effect = dropShadow;
                                tt.Background = ttBackground;
                                tt.BorderBrush = null;
                                tt.Content = g;
                                tooltip = tt;
                            }                            
                        }
                    }                    

                    mFileRecords.Add(new FileRecord(file)
                    {
                        Preview = preview,
                        ToolTip = tooltip
                    });
                }
            }

            mFileRecordsView = new ListCollectionView(mFileRecords);
            mFileRecordsView.Filter = FilterFunc;

            InitializeComponent();
            DataContext = this;
        }

        private bool FilterFunc(object obj)
        {
            if (!string.IsNullOrWhiteSpace(FilterText))
            {
                FileRecord record = obj as FileRecord;
                if (record != null)
                {
                    return record.Path.ToLower().Contains(FilterText.ToLower());
                }
            }

            return true;
        }
        
        private void ExportOverridesButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (FileRecord record in mFileRecords)
            {
                if (record.ExportOverride)
                {
                    Tracker.Instance.ActiveGamePackage.ExportUserOverride(record.Path);
                    Close();
                }
            }
        }
    }
}
