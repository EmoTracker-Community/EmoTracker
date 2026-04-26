using EmoTracker.Core;
using EmoTracker.Data.AutoTracking;
using EmoTracker.Data.Sessions;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmoTracker.Extensions.AutoTracker
{
    /// <summary>
    /// Phase 7.4: <see cref="AutoTrackerExtension"/> is now a factory.
    /// Discovered by <see cref="ExtensionManager"/>'s reflection scan as
    /// the app-wide auto-tracking <see cref="Extension"/>; per-state
    /// runtimes are constructed via <see cref="CreateForState"/> when a
    /// <see cref="TrackerState"/> registers, and disposed when it
    /// unregisters.
    ///
    /// <para>
    /// The factory exposes <i>UI-binding-friendly</i> proxy properties
    /// (<see cref="Connected"/>, <see cref="Error"/>, <see cref="ActiveProvider"/>,
    /// <see cref="SelectedProvider"/>, <see cref="ApplicableProviders"/>,
    /// commands) that forward to the currently-active state's
    /// <see cref="AutoTrackerInstance"/>. Switching the active state
    /// (window tab switch, primary swap, etc.) re-points the proxy at
    /// the new instance and fires PropertyChanged on every forwarded
    /// property — existing XAML bindings against
    /// <c>AutoTrackerExtension</c>-typed DataContexts continue to work
    /// without modification.
    /// </para>
    /// </summary>
    public class AutoTrackerExtension : ObservableObject, Extension, IStateScopedExtensionFactory
    {
        public string Name => "Auto Tracking";
        public string UID => "emotracker_auto_tracking";
        public int Priority => -100;

        public object StatusBarControl
        {
            get { return new AutoTrackerExtensionView { DataContext = this }; }
        }

        public AutoTrackerExtension()
        {
            // Subscribe to PrimaryState changes so we can re-route the
            // proxy to whichever state is currently active.
            ApplicationModel.Instance.PropertyChanged += OnAppModelPropertyChanged;
        }

        // Per-state-extension factory hook.
        public IStateScopedExtension CreateForState(TrackerState state)
        {
            return new AutoTrackerInstance(state);
        }

        // Legacy Extension lifecycle — the heavy lifting moved to per-state
        // instances. These no-ops keep the framework's app-wide hook chain
        // intact for non-state-scoped extensions.
        public void Start() { }
        public void Stop() { }
        public void OnPackageLoaded() { }
        public void OnPackageUnloaded() { }
        public JToken SerializeToJson() => null;
        public bool DeserializeFromJson(JToken token) => true;

        // ---------- Active-state proxy ------------------------------------

        AutoTrackerInstance mSubscribedInstance;

        /// <summary>
        /// The per-state <see cref="AutoTrackerInstance"/> bound to the
        /// currently-active <see cref="TrackerState"/>, or null if no
        /// state is active. Re-evaluates each access; UI bindings
        /// against this property's get-only inner state get
        /// PropertyChanged from the proxy when the active instance
        /// changes (we forward).
        /// </summary>
        public AutoTrackerInstance ActiveInstance
        {
            get
            {
                var state = ApplicationModel.Instance?.PrimaryState;
                if (state == null) return null;
                return ExtensionManager.Instance.GetStateScopedExtension<AutoTrackerInstance>(state);
            }
        }

        void OnAppModelPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ApplicationModel.PrimaryState)
                || e.PropertyName == nameof(ApplicationModel.CurrentlyActiveWindowContext))
            {
                ResubscribeActiveInstance();
            }
        }

        void ResubscribeActiveInstance()
        {
            // Drop subscription to the previous instance.
            if (mSubscribedInstance != null)
            {
                mSubscribedInstance.PropertyChanged -= OnInstancePropertyChanged;
                mSubscribedInstance = null;
            }
            // Subscribe to the new active instance.
            var inst = ActiveInstance;
            mSubscribedInstance = inst;
            if (inst != null)
                inst.PropertyChanged += OnInstancePropertyChanged;

            // Fire PropertyChanged on every proxied property so any UI
            // bound to the factory rebinds against the new instance.
            NotifyPropertyChanged(nameof(Connected));
            NotifyPropertyChanged(nameof(Error));
            NotifyPropertyChanged(nameof(Active));
            NotifyPropertyChanged(nameof(ActiveProvider));
            NotifyPropertyChanged(nameof(SelectedProvider));
            NotifyPropertyChanged(nameof(ApplicableProviders));
            NotifyPropertyChanged(nameof(StartCommand));
            NotifyPropertyChanged(nameof(StopCommand));
            NotifyPropertyChanged(nameof(SetProviderCommand));
            NotifyPropertyChanged(nameof(SetDeviceCommand));
            NotifyPropertyChanged(nameof(ActiveInstance));
        }

        void OnInstancePropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Forward verbatim — same property names on the proxy.
            NotifyPropertyChanged(e.PropertyName);
        }

        // ---------- Forwarded UI-binding properties -----------------------

        public bool Connected => ActiveInstance?.Connected ?? false;
        public bool Error => ActiveInstance?.Error ?? false;
        public bool Active => ActiveInstance?.Active ?? false;

        public IAutoTrackingProvider ActiveProvider => ActiveInstance?.ActiveProvider;
        public IAutoTrackingProvider SelectedProvider => ActiveInstance?.SelectedProvider;
        public IEnumerable<IAutoTrackingProvider> ApplicableProviders
            => ActiveInstance?.ApplicableProviders ?? Enumerable.Empty<IAutoTrackingProvider>();

        public DelegateCommand StartCommand => ActiveInstance?.StartCommand;
        public DelegateCommand StopCommand => ActiveInstance?.StopCommand;
        public DelegateCommand SetProviderCommand => ActiveInstance?.SetProviderCommand;
        public DelegateCommand SetDeviceCommand => ActiveInstance?.SetDeviceCommand;
    }
}
