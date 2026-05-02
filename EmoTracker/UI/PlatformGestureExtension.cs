using Avalonia;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using System;

namespace EmoTracker.UI
{
    /// <summary>
    /// XAML markup extension that produces a <see cref="KeyGesture"/> whose
    /// primary modifier is the platform-default command modifier — i.e.
    /// <see cref="KeyModifiers.Control"/> on Windows / Linux,
    /// <see cref="KeyModifiers.Meta"/> (the ⌘ key) on macOS — read from
    /// <c>Application.Current.PlatformSettings.HotkeyConfiguration.CommandModifiers</c>.
    ///
    /// <para>
    /// Use this in place of a hard-coded <c>"Cmd+X"</c> or <c>"Ctrl+X"</c>
    /// string for a <see cref="MenuItem.InputGesture"/> so the displayed
    /// shortcut and the routed hotkey both follow the host platform's
    /// convention. Avalonia's plain <c>"Cmd"</c> token parses as
    /// <see cref="KeyModifiers.Meta"/> on every platform — that's the Win
    /// key on Windows, which is wrong for application-level shortcuts.
    /// </para>
    ///
    /// <para>
    /// Usage:
    /// <code>
    /// xmlns:ui="clr-namespace:EmoTracker.UI"
    /// ...
    /// &lt;MenuItem InputGesture="{ui:PlatformGesture S}" /&gt;
    /// &lt;MenuItem InputGesture="{ui:PlatformGesture Shift+S}" /&gt;
    /// &lt;MenuItem InputGesture="{ui:PlatformGesture D0}" /&gt;
    /// </code>
    /// The <c>Keys</c> string accepts the same syntax as the right-hand
    /// side of an Avalonia gesture (e.g. <c>Shift+S</c>, <c>Alt+F4</c>) —
    /// the platform command modifier is automatically prepended; do NOT
    /// include <c>Cmd</c>, <c>Ctrl</c>, or <c>Meta</c> in the value.
    /// </para>
    /// </summary>
    public sealed class PlatformGestureExtension : MarkupExtension
    {
        public PlatformGestureExtension() { }
        public PlatformGestureExtension(string keys) { Keys = keys; }

        /// <summary>
        /// Modifiers + key on the right-hand side of the gesture, e.g.
        /// <c>S</c>, <c>Shift+S</c>, <c>D0</c>. Must NOT include the
        /// command modifier (<c>Cmd</c> / <c>Ctrl</c> / <c>Meta</c>) — that's
        /// supplied by the platform automatically.
        /// </summary>
        [ConstructorArgument("keys")]
        public string Keys { get; set; } = string.Empty;

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            // PlatformSettings is available once Application.Current is up
            // — i.e. after AppBuilder.Configure has produced the App instance.
            // XAML markup-extension expansion happens on control instantiation
            // (post-startup), so this is safe. Defensive fallback to Control
            // covers the unit-test / designer path where PlatformSettings
            // may be null.
            var commandModifier = Application.Current?
                .PlatformSettings?
                .HotkeyConfiguration?
                .CommandModifiers
                ?? KeyModifiers.Control;

            // Render the modifier as a string Avalonia's gesture parser
            // understands. The parser maps "Cmd" → Meta and "Ctrl" → Control;
            // when ToString() runs on the parsed gesture for display, it
            // formats the modifier with the platform's native glyph (e.g.
            // ⌘ on macOS), so we get the correct on-screen rendering for
            // free.
            string modifierToken = commandModifier == KeyModifiers.Meta ? "Cmd" : "Ctrl";

            string keys = Keys?.Trim() ?? string.Empty;
            if (keys.Length == 0)
                throw new InvalidOperationException(
                    "PlatformGestureExtension requires a Keys value (e.g. \"S\" or \"Shift+S\").");

            return KeyGesture.Parse(modifierToken + "+" + keys);
        }
    }
}
