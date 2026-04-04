using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace EmoTracker.UI
{
    /// <summary>
    /// Interaction logic for ChestListControl.xaml
    /// </summary>
    public partial class ChestListControl : UserControl
    {
        public ImageSource ClosedChest
        {
            get { return (ImageSource)GetValue(ClosedChestProperty); }
            set { SetValue(ClosedChestProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Compact.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ClosedChestProperty =
            DependencyProperty.Register("ClosedChest", typeof(ImageSource), typeof(ChestListControl), new PropertyMetadata(null));

        public ImageSource OpenChest
        {
            get { return (ImageSource)GetValue(OpenChestProperty); }
            set { SetValue(OpenChestProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Compact.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty OpenChestProperty =
            DependencyProperty.Register("OpenChest", typeof(ImageSource), typeof(ChestListControl), new PropertyMetadata(null));

        public ImageSource UnavailableClosedChest
        {
            get { return (ImageSource)GetValue(UnavailableClosedChestProperty); }
            set { SetValue(UnavailableClosedChestProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Compact.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty UnavailableClosedChestProperty =
            DependencyProperty.Register("UnavailableClosedChest", typeof(ImageSource), typeof(ChestListControl), new PropertyMetadata(null));

        public ImageSource UnavailableOpenChest
        {
            get { return (ImageSource)GetValue(UnavailableOpenChestProperty); }
            set { SetValue(UnavailableOpenChestProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Compact.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty UnavailableOpenChestProperty =
            DependencyProperty.Register("UnavailableOpenChest", typeof(ImageSource), typeof(ChestListControl), new PropertyMetadata(null));

        protected static void LoadImages()
        {
#if false
            if (OpenChest == null || ClosedChest == null || UnavailableOpenChest == null || UnavailableClosedChest == null)
            {
                OpenChest = Utility.IconUtility.GetImage(new Uri("pack://application:,,,/EmoTracker;component/Resources/chest_open.png"));
                ClosedChest = Utility.IconUtility.GetImage(new Uri("pack://application:,,,/EmoTracker;component/Resources/chest_closed.png"));

                UnavailableOpenChest =  Utility.IconUtility.MakeImageDim(Utility.IconUtility.MakeImageGrayscale(OpenChest));
                UnavailableClosedChest = Utility.IconUtility.MakeImageDim(Utility.IconUtility.MakeImageGrayscale(ClosedChest));
            }
#endif
        }

        public ChestListControl()
        {
            LoadImages();
            InitializeComponent();
        }

        ObservableCollection<bool> mChestStates = new ObservableCollection<bool>();
        public IEnumerable<bool> Chests
        {
            get { return mChestStates; }
        }

        public uint Count
        {
            get { return (uint)GetValue(CountProperty); }
            set { SetValue(CountProperty, value); }
        }

        public bool Accessible
        {
            get { return (bool)GetValue(AccessibleProperty); }
            set { SetValue(AccessibleProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Accessible.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty AccessibleProperty =
            DependencyProperty.Register("Accessible", typeof(bool), typeof(ChestListControl), new PropertyMetadata(true));



        // Using a DependencyProperty as the backing store for Count.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty CountProperty =
            DependencyProperty.Register("Count", typeof(uint), typeof(ChestListControl), new PropertyMetadata((uint)5));

        public uint Available
        {
            get { return (uint)GetValue(AvailableProperty); }
            set { SetValue(AvailableProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Available.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty AvailableProperty =
            DependencyProperty.Register("Available", typeof(uint), typeof(ChestListControl), new FrameworkPropertyMetadata((uint)3, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public bool ClearAsGroup
        {
            get { return (bool)GetValue(ClearAsGroupProperty); }
            set { SetValue(ClearAsGroupProperty, value); }
        }

        // Using a DependencyProperty as the backing store for ClearAsGroup.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ClearAsGroupProperty =
            DependencyProperty.Register("ClearAsGroup", typeof(bool), typeof(ChestListControl), new PropertyMetadata(false));

        public bool Compact
        {
            get { return (bool)GetValue(CompactProperty); }
            set { SetValue(CompactProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Compact.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty CompactProperty =
            DependencyProperty.Register("Compact", typeof(bool), typeof(ChestListControl), new PropertyMetadata(false));


        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            UpdateChests();
            base.OnPropertyChanged(e);
        }

        private void UpdateChests()
        {
            while (mChestStates.Count > Count)
            {
                mChestStates.RemoveAt(0);
            }

            while (mChestStates.Count < Count)
            {
                mChestStates.Add(true);
            }

            for (int i = 0; i < Count; ++i)
            {
                mChestStates[i] = i < Available;
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (IsEnabled)
            {
                if (ClearAsGroup)
                    Available = 0;
                else
                    Available = (uint)Math.Max(0, (int)Available - 1);
            }
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            if (IsEnabled)
            {
                if (ClearAsGroup)
                    Available = Count;
                else
                    Available = (uint)Math.Min(Count, (int)Available + 1);
            }
        }
    }
}
