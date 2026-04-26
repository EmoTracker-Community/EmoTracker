using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using EmoTracker.Data.Sessions;
using System;
using System.ComponentModel;
using System.Linq;

namespace EmoTracker.UI
{
    /// <summary>
    /// Phase 7.7: bottom-bar state manager. Surfaces the list of loaded
    /// PackageInstances and their TrackerStates with create / switch /
    /// open-in-new-window / close per-state plus a "load new pack" /
    /// "save bundle" / "load bundle" actions section.
    /// </summary>
    public partial class StateManagerControl : UserControl
    {
        public StateManagerControl()
        {
            InitializeComponent();
            ApplicationModel.Instance.PropertyChanged += OnAppModelPropertyChanged;
            ApplicationModel.Instance.Windows.CollectionChanged += (_, __) => RefreshLabel();
            this.AttachedToVisualTree += (_, __) =>
            {
                var ctx = ResolveWindowContext();
                if (ctx != null)
                    ctx.PropertyChanged += OnWindowContextPropertyChanged;
                RefreshLabel();
            };
            this.DetachedFromVisualTree += (_, __) =>
            {
                var ctx = ResolveWindowContext();
                if (ctx != null)
                    ctx.PropertyChanged -= OnWindowContextPropertyChanged;
            };
        }

        WindowContext ResolveWindowContext()
        {
            // Walk up the visual tree to find the hosting MainWindow.
            var w = this.GetVisualRoot() as MainWindow;
            return w?.WindowContext;
        }

        // ---------- Active-state label binding helper -----------------------

        public static readonly StyledProperty<string> ActiveStateLabelProperty =
            AvaloniaProperty.Register<StateManagerControl, string>(nameof(ActiveStateLabel), "(no state)");

        public string ActiveStateLabel
        {
            get => GetValue(ActiveStateLabelProperty);
            set => SetValue(ActiveStateLabelProperty, value);
        }

        void OnAppModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ApplicationModel.CurrentlyActiveWindowContext)
                || e.PropertyName == nameof(ApplicationModel.PrimaryState))
            {
                RefreshLabel();
            }
        }

        void OnWindowContextPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WindowContext.ActiveState))
                RefreshLabel();
        }

        void RefreshLabel()
        {
            var ctx = ResolveWindowContext();
            var state = ctx?.ActiveState;
            ActiveStateLabel = state != null
                ? state.Name ?? "(unnamed)"
                : "(no state)";
        }

        // ---------- Per-state actions ---------------------------------------

        TrackerState StateFromButton(object sender)
        {
            return (sender as Control)?.Tag as TrackerState;
        }

        void OnSwitchToState(object sender, RoutedEventArgs e)
        {
            var state = StateFromButton(sender);
            var ctx = ResolveWindowContext();
            if (state == null || ctx == null) return;
            if (!ctx.OpenStates.Contains(state))
                ctx.AddState(state);
            else
                ctx.ActiveState = state;
            ClosePopup();
        }

        void OnOpenStateInNewWindow(object sender, RoutedEventArgs e)
        {
            var state = StateFromButton(sender);
            var ctx = ResolveWindowContext();
            if (state == null || ctx == null) return;
            ApplicationModel.Instance.OpenStateInNewWindow(ctx, state);
            ClosePopup();
        }

        void OnCloseState(object sender, RoutedEventArgs e)
        {
            var state = StateFromButton(sender);
            if (state == null) return;
            // Find owning PackageInstance + remove the state from it.
            foreach (var pi in ApplicationModel.Instance.PackageInstances)
            {
                if (pi.States.ContainsKey(state.Id))
                {
                    pi.RemoveState(state.Id);
                    break;
                }
            }
            // Also remove from any window's OpenStates.
            foreach (var ctx in ApplicationModel.Instance.Windows)
                ctx.RemoveState(state);
            ClosePopup();
        }

        void OnCreateAdditionalState(object sender, RoutedEventArgs e)
        {
            var pi = (sender as Control)?.Tag as PackageInstance;
            var ctx = ResolveWindowContext();
            if (pi == null || ctx == null) return;
            var state = ApplicationModel.Instance.CreateAdditionalState(pi);
            ctx.AddState(state);
            ClosePopup();
        }

        // ---------- Bundle save / load (Phase 7.10) -------------------------

        async void OnSaveBundle(object sender, RoutedEventArgs e)
        {
            ClosePopup();
            await BundleService.SaveBundleInteractiveAsync(this.GetVisualRoot() as Window);
        }

        async void OnLoadBundle(object sender, RoutedEventArgs e)
        {
            ClosePopup();
            await BundleService.LoadBundleInteractiveAsync(this.GetVisualRoot() as Window);
        }

        void ClosePopup()
        {
            var toggle = this.FindControl<ToggleButton>("StateManagerToggle");
            if (toggle != null) toggle.IsChecked = false;
        }
    }
}
