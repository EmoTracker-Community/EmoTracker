using System.ComponentModel;
using System.Runtime.CompilerServices;

#if WINDOWS
using System.Windows.Controls;
#else
using Avalonia.Controls;
#endif

namespace EmoTracker.UI.Controls
{
#if WINDOWS
    public class ObservableUserControl : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
#else
    public class ObservableUserControl : UserControl
    {
        public new event PropertyChangedEventHandler PropertyChanged;
#endif
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
