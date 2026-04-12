#nullable enable annotations
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Media;
using EmoTracker.Data.Settings;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace EmoTracker.UI
{
    /// <summary>
    /// Interaction logic for NoteTakingIconPopup.axaml
    /// </summary>
    public partial class NoteTakingIconPopup : UserControl
    {
        /// <summary>
        /// The foreground color used when the note site has no notes.
        /// Defaults to WhiteSmoke for use in location headers; set to #717171 for the status bar.
        /// </summary>
        public static readonly StyledProperty<string> EmptyForegroundProperty =
            AvaloniaProperty.Register<NoteTakingIconPopup, string>(nameof(EmptyForeground), "#F5F5F5");

        public string EmptyForeground
        {
            get => GetValue(EmptyForegroundProperty);
            set => SetValue(EmptyForegroundProperty, value);
        }

        /// <summary>
        /// Multi-value converter: [0] Empty (bool), [1] EmptyForeground (string).
        /// Returns the empty-foreground brush when no notes exist, otherwise the active color.
        /// </summary>
        public static readonly IMultiValueConverter EmptyToForegroundConverter =
            new FuncMultiValueConverter<object?, IBrush?>(values =>
            {
                var list = new List<object?>(values);
                bool empty = list.Count > 0 && list[0] is true;
                string emptyColor = list.Count > 1 && list[1] is string s ? s : "#F5F5F5";
                if (empty)
                    return new SolidColorBrush(Color.Parse(emptyColor));
                return new SolidColorBrush(Color.Parse(ApplicationColors.Instance.Status_Generic_Active));
            });

        public NoteTakingIconPopup()
        {
            InitializeComponent();
        }

        // Keep a public reference so external code can reach the Popup if needed.
        public Popup PopupInstance => NotesPopup;

        private void OpenNotesButton_Checked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            NotesPopup.IsOpen = true;
        }

        private void OpenNotesButton_Unchecked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            NotesPopup.IsOpen = false;
        }
    }
}
