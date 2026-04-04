using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using System.Windows.Shapes;

namespace EmoTracker.UI
{
    /// <summary>
    /// Interaction logic for PackageManagerWindow.xaml
    /// </summary>
    public partial class PackageManagerWindow : Window, INotifyPropertyChanged
    {
        public PackageManagerWindow()
        {
            InitializeComponent();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public bool IsShiftHeld
        {
            get
            {
                return Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            }
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs("IsShiftHeld"));

            base.OnPreviewMouseMove(e);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs("IsShiftHeld"));

            base.OnPreviewKeyDown(e);
        }

        protected override void OnPreviewKeyUp(KeyEventArgs e)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs("IsShiftHeld"));

            base.OnPreviewKeyDown(e);
        }
    }
}
