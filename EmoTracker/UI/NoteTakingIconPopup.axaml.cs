#nullable enable annotations
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Media;
using EmoTracker.Data.Settings;
using System;
using System.Globalization;

namespace EmoTracker.UI
{
    /// <summary>
    /// Interaction logic for NoteTakingIconPopup.axaml
    /// </summary>
    public partial class NoteTakingIconPopup : UserControl
    {
        /// <summary>
        /// Converts the note-taking <c>Empty</c> bool to a foreground brush:
        /// <c>true</c>  → WhiteSmoke (no notes)
        /// <c>false</c> → Status_Generic_Active color (has notes)
        /// </summary>
        public static readonly IValueConverter EmptyToForegroundConverter =
            new FuncValueConverter<bool, IBrush>(empty =>
            {
                if (empty)
                    return new SolidColorBrush(Color.Parse("#717171"));

                Color active = Color.Parse(ApplicationColors.Instance.Status_Generic_Active);
                return new SolidColorBrush(active);
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
