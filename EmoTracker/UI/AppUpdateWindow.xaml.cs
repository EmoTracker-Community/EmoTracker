using EmoTracker.Core;
using EmoTracker.Update;
using System;
using System.Collections.Generic;
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
    /// Interaction logic for Applucati.xaml
    /// </summary>
    public partial class AppUpdateWindow : Window
    {
        bool mbAutoClose = false;

        public AppUpdateWindow(bool bAutoClose)
        {
            mbAutoClose = bAutoClose;
            this.Initialized += AppUpdateWindow_Initialized;

            InitializeComponent();
        }

        private void AppUpdateWindow_Initialized(object sender, EventArgs e)
        {
            if (mbAutoClose)
            {
                AppUpdate.Instance.PropertyChanged += Instance_PropertyChanged;
            }

            AppUpdate.Instance.CheckForUpdates();
        }

        private void Instance_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (AppUpdate.Instance.Status)
            {
                case AppUpdate.UpdateStatus.Error:
                case AppUpdate.UpdateStatus.NoUpdateAvailable:
                    this.Close();
                    break;
            }
        }
    }
}
