using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Threading.Tasks;

namespace EmoTracker.Services.Dialogs
{
    /// <summary>
    /// Button configuration for <see cref="MessageDialog"/>.
    /// </summary>
    public enum MessageDialogButtons { Ok, YesNo, YesNoCancel }

    /// <summary>
    /// Outcome returned by <see cref="MessageDialog.ShowAsync"/>.
    /// </summary>
    public enum MessageDialogResult { Ok, Yes, No, Cancel }

    /// <summary>
    /// In-house replacement for the <c>MsBox.Avalonia</c> message-box
    /// dependency. Renders a simple dark-themed modal with a
    /// <see cref="SelectableTextBlock"/>-backed message body so users can
    /// highlight + Ctrl+C the text. The original MsBox path crashed the
    /// entire app on Ctrl+C from a load-failure dialog (issue #68); a
    /// real Avalonia <see cref="Window"/> shown via <see cref="Window.ShowDialog{T}"/>
    /// has its <see cref="TopLevel.Clipboard"/> properly wired by Avalonia,
    /// so Ctrl+C just works.
    /// </summary>
    public partial class MessageDialog : Window
    {
        // The button that should fire on Esc — depends on the button set:
        //   Ok          → OK    (only one button)
        //   YesNo       → No    (the dismissive choice)
        //   YesNoCancel → Cancel
        // Stashed at construction time so the KeyDown handler doesn't have
        // to re-derive the button set.
        private MessageDialogResult _escapeResult = MessageDialogResult.Cancel;

        // The button that should fire on Enter — almost always the
        // default action (positive choice). For YesNo with defaultYes=false
        // this becomes No (matching prior MsBox behaviour where pressing
        // Enter on a "lose unsaved progress?" prompt defaulted to No).
        private MessageDialogResult _enterResult = MessageDialogResult.Ok;

        // Brush palette mirrors CfaWarningWindow so all in-app modals share
        // visual language. Static so each new dialog reuses the same brush
        // instances rather than allocating per-show.
        private static readonly IBrush PrimaryBrush         = new SolidColorBrush(Color.Parse("#35e0b5"));
        private static readonly IBrush PrimaryForegroundBr  = new SolidColorBrush(Color.Parse("#111111"));
        private static readonly IBrush SecondaryBrush       = new SolidColorBrush(Color.Parse("#3a3a3a"));
        private static readonly IBrush SecondaryForegroundBr = new SolidColorBrush(Color.Parse("#e0e0e0"));

        public MessageDialog()
        {
            InitializeComponent();

            // Esc → escape result; Enter → enter result. Wired here rather
            // than in XAML so the result values can vary per call without
            // needing per-instance XAML.
            KeyDown += OnKeyDown;
        }

        /// <summary>
        /// Show a modal message dialog and await its result. <paramref name="owner"/>
        /// must be the parent window (typically the desktop lifetime's MainWindow);
        /// when null the dialog is suppressed and <see cref="MessageDialogResult.Ok"/>
        /// is returned (matches the prior MsBox behaviour for early-init calls
        /// that have no parent yet).
        /// </summary>
        public static Task<MessageDialogResult> ShowAsync(
            Window owner,
            string title,
            string message,
            MessageDialogButtons buttons,
            bool defaultYes = true)
        {
            if (owner == null)
            {
                Serilog.Log.Warning(
                    "[Dialogs] No owner window; suppressing dialog '{Title}'", title);
                return Task.FromResult(MessageDialogResult.Ok);
            }

            var dlg = new MessageDialog
            {
                Title = string.IsNullOrEmpty(title) ? "EmoTracker" : title,
            };
            dlg.TitleText.Text = title ?? string.Empty;
            dlg.MessageText.Text = message ?? string.Empty;
            dlg.BuildButtons(buttons, defaultYes);

            return dlg.ShowDialog<MessageDialogResult>(owner);
        }

        // Build the button row from MessageDialogButtons. The default-action
        // button (right-most, primary-styled) gets the focus and Enter
        // shortcut after Opened so keyboard-only users can dismiss with Enter.
        private void BuildButtons(MessageDialogButtons buttons, bool defaultYes)
        {
            switch (buttons)
            {
                case MessageDialogButtons.Ok:
                    AddButton("OK", MessageDialogResult.Ok, primary: true);
                    _escapeResult = MessageDialogResult.Ok;
                    _enterResult  = MessageDialogResult.Ok;
                    break;

                case MessageDialogButtons.YesNo:
                    AddButton("No",  MessageDialogResult.No,  primary: !defaultYes);
                    AddButton("Yes", MessageDialogResult.Yes, primary: defaultYes);
                    _escapeResult = MessageDialogResult.No;
                    _enterResult  = defaultYes ? MessageDialogResult.Yes : MessageDialogResult.No;
                    break;

                case MessageDialogButtons.YesNoCancel:
                    AddButton("Cancel", MessageDialogResult.Cancel, primary: false);
                    AddButton("No",     MessageDialogResult.No,     primary: false);
                    AddButton("Yes",    MessageDialogResult.Yes,    primary: true);
                    _escapeResult = MessageDialogResult.Cancel;
                    _enterResult  = MessageDialogResult.Yes;
                    break;
            }
        }

        // Append a button to the row. The primary button uses the mint accent
        // from CfaWarningWindow; secondary buttons get a flat dark style so
        // they recede visually.
        private void AddButton(string label, MessageDialogResult result, bool primary)
        {
            var btn = new Button
            {
                Content    = label,
                Padding    = new Thickness(20, 7),
                MinWidth   = 80,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(0),
                // Avalonia's Fluent theme centers Button content via a
                // Style setter, but Style setters only apply to controls
                // declared in XAML. Programmatic `new Button { ... }`
                // instances skip that hookup and inherit the
                // ContentControl default of Left/Top alignment, leaving
                // the label hugging the inner-left edge of the padded
                // button. Set both axes explicitly so labels are centered
                // regardless of how the button is constructed.
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment   = VerticalAlignment.Center,
                Background = primary ? PrimaryBrush         : SecondaryBrush,
                Foreground = primary ? PrimaryForegroundBr  : SecondaryForegroundBr,
                FontWeight = primary ? FontWeight.SemiBold  : FontWeight.Normal,
            };
            btn.Click += (_, _) => Close(result);

            // The primary button gets focus on dialog open so Enter triggers
            // the default action. ButtonRow is filled left-to-right; the
            // primary action is conventionally right-most, so it's the last
            // button added in the YesNo / YesNoCancel cases.
            if (primary)
            {
                Opened += (_, _) =>
                {
                    btn.Focus();
                    // Default-button Enter handling is done in OnKeyDown
                    // against _enterResult; we don't need IsDefault here
                    // because we want Esc/Enter parity regardless of focus.
                };
            }

            ButtonRow.Children.Add(btn);
        }

        // Esc / Enter shortcuts. Bound at class level (not per-button) so they
        // fire whether or not focus is on a button — handy when the user has
        // focus on the message text after selecting + copying.
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close(_escapeResult);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
            {
                Close(_enterResult);
                e.Handled = true;
            }
        }
    }
}
