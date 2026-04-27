using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using EmoTracker.Data;
using EmoTracker.Data.Sessions;
using System;
using System.ComponentModel;
using System.Linq;

namespace EmoTracker.UI
{
    /// <summary>
    /// Phase 7.7: bottom-bar state manager. Surfaces the list of loaded
    /// PackageInstances and their TrackerStates with create / switch /
    /// open-in-new-window / close per-state plus a "Load New Pack" pack
    /// list and "Save Bundle" / "Load Bundle" actions.
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
            var w = this.GetVisualRoot() as MainWindow;
            return w?.WindowContext;
        }

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
            try
            {
                var state = StateFromButton(sender);
                var ctx = ResolveWindowContext();
                if (state == null || ctx == null) return;
                if (!ctx.OpenStates.Contains(state))
                    ctx.AddState(state);
                else
                    ctx.ActiveState = state;
                // Drive the in-Data layer's active state slot to match.
                ApplicationModel.Instance.OnActiveStateSwitched(state);
            }
            catch (Exception)
            {
            }
            ClosePopup();
        }

        void OnOpenStateInNewWindow(object sender, RoutedEventArgs e)
        {
            try
            {
                var state = StateFromButton(sender);
                var ctx = ResolveWindowContext();
                if (state == null || ctx == null) return;
                ApplicationModel.Instance.OpenStateInNewWindow(ctx, state);
            }
            catch (Exception)
            {
            }
            ClosePopup();
        }

        void OnCloseState(object sender, RoutedEventArgs e)
        {
            try
            {
                var state = StateFromButton(sender);
                if (state == null) return;
                // Snapshot windows first to avoid concurrent modification.
                var winSnap = new System.Collections.Generic.List<WindowContext>(ApplicationModel.Instance.Windows);
                foreach (var ctx in winSnap)
                    ctx.RemoveState(state);
                foreach (var pi in ApplicationModel.Instance.PackageInstances)
                {
                    if (pi.States.ContainsKey(state.Id))
                    {
                        pi.RemoveState(state.Id);
                        break;
                    }
                }
            }
            catch (Exception)
            {
            }
            ClosePopup();
        }

        void OnCreateAdditionalState(object sender, RoutedEventArgs e)
        {
            try
            {
                var pi = (sender as Control)?.Tag as PackageInstance;
                var ctx = ResolveWindowContext();
                if (pi == null || ctx == null) return;
                var state = ApplicationModel.Instance.CreateAdditionalState(pi);
                ctx.AddState(state);
                ApplicationModel.Instance.OnActiveStateSwitched(state);
            }
            catch (Exception)
            {
            }
            ClosePopup();
        }

        // Phase 7.7 polish: load a new pack into a fresh PackageInstance
        // and add its primary state as a tab on the current window.
        void OnLoadNewPack(object sender, RoutedEventArgs e)
        {
            try
            {
                var pkg = (sender as Control)?.Tag as IGamePackage;
                var ctx = ResolveWindowContext();
                if (pkg == null || ctx == null) return;

                ClosePopup();

                // Defer the actual pack-load to the next dispatcher tick so
                // the popup closes before the (possibly slow) load runs.
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        // For Phase 7.5 forward, LoadNewPack creates a
                        // separate PackageInstance with its own catalogs.
                        // To keep the existing UI bindings (which root
                        // through Tracker.Instance singletons) coherent,
                        // we drive the load via the existing single-pack
                        // Activate flow when this is the only pack —
                        // otherwise the new pack lives alongside but its
                        // content won't be visible until the binding
                        // migration lands.
                        bool firstActivation =
                            ApplicationModel.Instance.PackageInstances.Count == 0
                            || (ApplicationModel.Instance.PackageInstances.Count == 1
                                && !ApplicationModel.Instance.PackageInstances[0].States.Values.Any(s => s.Package != null));
                        if (firstActivation)
                        {
                            // First-pack activation: drive through ApplicationModel
                            // which routes into the primary state's pack-load.
                            ApplicationModel.Instance.ActivatePackage(pkg, null);
                        }
                        else
                        {
                            var primary = ApplicationModel.Instance.LoadNewPack(pkg, null);
                            ctx.AddState(primary);
                            // Drive bindings to the new pack's content.
                            ApplicationModel.Instance.OnActiveStateSwitched(primary);
                        }
                    }
                    catch (Exception ex)
                    {
                        ApplicationModel.Instance.PushMarkdownNotification(
                            EmoTracker.Data.Scripting.NotificationType.Error,
                            "Failed to load pack: " + ex.Message);
                    }
                });
            }
            catch (Exception)
            {
            }
        }

        async void OnSaveBundle(object sender, RoutedEventArgs e)
        {
            ClosePopup();
            try
            {
                await BundleService.SaveBundleInteractiveAsync(this.GetVisualRoot() as Window);
            }
            catch (Exception)
            {
            }
        }

        async void OnLoadBundle(object sender, RoutedEventArgs e)
        {
            ClosePopup();
            try
            {
                await BundleService.LoadBundleInteractiveAsync(this.GetVisualRoot() as Window);
            }
            catch (Exception)
            {
            }
        }

        void ClosePopup()
        {
            var toggle = this.FindControl<ToggleButton>("StateManagerToggle");
            if (toggle != null) toggle.IsChecked = false;
        }
    }
}
