using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;

namespace EmoTracker.UI.Controls
{
    public class ObservableUserControl : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void NotifyPropertyChanged([CallerMemberName] string propertyName = null)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (!object.Equals(field, value))
            {
                field = value;
                NotifyPropertyChanged(propertyName);
                return true;
            }

            return false;
        }
    }
}
