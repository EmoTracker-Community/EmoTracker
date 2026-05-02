using Avalonia.Controls;
using Avalonia.Input;
using EmoTracker.Data;
using EmoTracker.Extensions;
using EmoTracker.Extensions.NDI;
using System;
using System.ComponentModel;

namespace EmoTracker.UI
{
    public partial class BroadcastView : Window
    {
        private bool _closed;
        private readonly WindowContext _hostContext;

        // Default ctor exists for the XAML designer / legacy callers; falls
        // back to ApplicationModel's app-wide BroadcastLayout. Production
        // callers should use the WindowContext-bound ctor below.
        public BroadcastView() : this(null) { }

        /// <summary>
        /// Constructs a BroadcastView whose content tracks
        /// <paramref name="hostContext"/>'s active tab. The view's
        /// <see cref="Window.DataContext"/> is set to that context's
        /// <see cref="WindowContext.BroadcastLayout"/> and refreshes
        /// when the host's active state changes (e.g. user switches
        /// tabs in the host window).
        ///
        /// <para>
        /// The NDI source name is suffixed with the host context's name
        /// so multiple windows broadcasting simultaneously advertise as
        /// distinct NDI sources rather than colliding on a single name.
        /// </para>
        /// </summary>
        public BroadcastView(WindowContext hostContext)
        {
            InitializeComponent();
            _hostContext = hostContext;

            // Wire the per-host data context. The XAML root no longer sets
            // DataContext to ApplicationModel's app-wide BroadcastLayout;
            // we drive it from the host WindowContext here so each window's
            // broadcast view follows its own active tab.
            if (hostContext != null)
            {
                DataContext = hostContext.BroadcastLayout;
                hostContext.PropertyChanged += OnHostPropertyChanged;
                NDIHost.NdiName = ResolveNdiName(hostContext);
                Title = ResolveTitle(hostContext);
            }
            else
            {
                // Legacy / designer path: bind to app-wide layout so the
                // view still has something to render against.
                DataContext = ApplicationModel.Instance.BroadcastLayout;
                NDIHost.NdiName = "EmoTracker Broadcast View";
            }

            // When background NDI is enabled, the hidden HiddenBroadcastWindow
            // handles NDI broadcasting on all platforms, so the visible container
            // must stay dormant to avoid advertising a duplicate source.  With the
            // setting off, the visible container owns the NDI stream (legacy
            // "NDI only while broadcast view is open" behaviour).
            //
            // NdiEnabled is read once in NdiSendContainer.OnAttachedToVisualTree,
            // so we set it BEFORE the window attaches to the visual tree.
            NDIHost.NdiEnabled = !ApplicationSettings.Instance.EnableBackgroundNdi;

            if (NDIHost.NdiEnabled)
            {
                NDIHost.PropertyChanged += NDIHost_PropertyChanged;
                UpdateNDIExtensionStatus();
            }
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

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            _closed = true;
            if (_hostContext != null)
                _hostContext.PropertyChanged -= OnHostPropertyChanged;
            if (NDIHost.NdiEnabled)
            {
                NDIHost.Dispose();
                UpdateNDIExtensionStatus();
            }
            base.OnClosing(e);
        }

        private void OnHostPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // ActiveState change drives a BroadcastLayout PropertyChanged on
            // the host context; refresh our DataContext so the rendered
            // layout follows.
            if (e.PropertyName == nameof(WindowContext.BroadcastLayout))
            {
                DataContext = _hostContext.BroadcastLayout;
            }
        }

        private void NDIHost_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NdiSendContainer.IsSendPaused))
                UpdateNDIExtensionStatus();
        }

        private void UpdateNDIExtensionStatus()
        {
            // When the hidden window is managing NDI, the visible container is
            // dormant (IsSendPaused stays at its default), and the hidden window
            // drives extension.Active.  Skip writes from this path in that case
            // so the two don't fight over the Active state.
            if (!NDIHost.NdiEnabled)
                return;

            var extension = FindWindowNDIExtension();
            if (extension != null)
                extension.Active = !_closed && !NDIHost.IsSendPaused;
        }

        // Look up this window's NDIExtension instance (the per-window
        // version under the new IWindowExtension scope). Returns null if
        // the host context is unset (legacy ctor path) or no NDIExtension
        // is bound to this window.
        private NDIExtension FindWindowNDIExtension()
        {
            if (_hostContext == null) return null;
            foreach (var ext in ExtensionManager.Instance.GetWindowExtensions(_hostContext))
                if (ext is NDIExtension ndi) return ndi;
            return null;
        }

        // --- Per-window NDI naming ---------------------------------------

        // Build a per-host NDI name so multiple BroadcastView windows
        // (one per app window) advertise as distinct sources on the
        // network. Uses WindowContext.Sequence — a unique, sequential
        // per-process integer — to guarantee no two windows can collide
        // on a single NDI source name. (WindowContext.Name defaults to
        // "primary" for every MainWindow, so naming by Name alone would
        // produce duplicate NDI sources, which is the symptom we're
        // fixing.)
        static string ResolveNdiName(WindowContext host)
        {
            if (host == null) return "EmoTracker Broadcast";
            return $"EmoTracker Broadcast {host.Sequence}";
        }

        static string ResolveTitle(WindowContext host)
        {
            if (host == null) return "EmoTracker: Broadcast View";
            return $"EmoTracker: Broadcast View {host.Sequence}";
        }
    }
}
