using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
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
    /// Interaction logic for GroupedLocationListControl.xaml
    /// </summary>
    public partial class GroupedLocationListControl : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void NotifyPropertyChanged([CallerMemberName] string propertyName = null)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }

        public GroupedLocationListControl()
        {
            InitializeComponent();
        }

        private int mScale = 100;

        public int Scale
        {
            get { return mScale; }
            set { mScale = value; }
        }

        private double mScaleMultiplier = 1.0;

        public double ScaleMultiplier
        {
            get { return mScaleMultiplier; }
            set { mScaleMultiplier = value; NotifyPropertyChanged(); }
        }

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                Scale += e.Delta / 60;
                Scale = Math.Max(50, Math.Min(Scale, 100));
                ScaleMultiplier = Scale / 100.0;
                e.Handled = true;
            }

            base.OnPreviewMouseWheel(e);
        }
    }
}
