using Avalonia.Controls;
using Avalonia.Input;

namespace EmoTracker.UI
{
    public partial class BroadcastView : Window
    {
        public BroadcastView()
        {
            InitializeComponent();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                ApplicationModel.Instance.RefreshCommand.Execute(null);
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }
    }
}
