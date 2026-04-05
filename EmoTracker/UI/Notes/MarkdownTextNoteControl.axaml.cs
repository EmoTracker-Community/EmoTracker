using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EmoTracker.UI.Controls;

namespace EmoTracker.UI.Notes
{
    /// <summary>
    /// Interaction logic for MarkdownTextNoteControl.axaml
    /// </summary>
    public partial class MarkdownTextNoteControl : ObservableUserControl
    {
        public MarkdownTextNoteControl()
        {
            InitializeComponent();

            MarkdownSourceEditor.KeyDown += MarkdownSourceEditor_KeyDown;
            MarkdownSourceEditor.LostFocus += MarkdownSourceEditor_LostFocus;

            // Show/hide the edit button based on edit mode and whether the source is empty.
            // The edit button is visible when:
            //   - The control is enabled, AND
            //   - We are NOT in edit mode, AND
            //   - (the mouse is over EditorContainer OR MarkdownSourceEmpty is true)
            // This is managed in code-behind since Avalonia does not support MultiDataTrigger in XAML.
            UpdateEditButtonVisibility();
        }

        bool mbIsEditModeEnabled = false;

        /// <summary>
        /// Gets or sets whether the markdown source editor is active.
        /// Switching from edit mode back to view mode commits the binding.
        /// </summary>
        public bool IsEditModeEnabled
        {
            get => mbIsEditModeEnabled;
            set
            {
                if (SetProperty(ref mbIsEditModeEnabled, value))
                {
                    // When leaving edit mode the TextBox binding uses UpdateSourceTrigger=LostFocus,
                    // so the source is already updated when focus leaves the editor.
                    // We only need to ensure the editor loses focus when edit mode is turned off
                    // programmatically (e.g. Escape key).
                    if (!mbIsEditModeEnabled && MarkdownSourceEditor.IsFocused)
                    {
                        // Moving focus away triggers the LostFocus binding update.
                        TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
                    }

                    UpdateEditButtonVisibility();
                }
            }
        }

        // ------------------------------------------------------------------
        // Edit-button visibility helper
        // ------------------------------------------------------------------

        private bool mbMarkdownSourceEmpty = true;

        /// <summary>
        /// True when the bound MarkdownSource is null or empty.
        /// The XAML DataContext should raise PropertyChanged for this property,
        /// or the edit button visibility can be refreshed explicitly.
        /// </summary>
        public bool MarkdownSourceEmpty
        {
            get => mbMarkdownSourceEmpty;
            set
            {
                if (SetProperty(ref mbMarkdownSourceEmpty, value))
                    UpdateEditButtonVisibility();
            }
        }

        private bool mbEditorContainerIsPointerOver = false;

        private void UpdateEditButtonVisibility()
        {
            if (EditButton == null)
                return;

            bool showEdit = IsEnabled
                            && !mbIsEditModeEnabled
                            && (mbEditorContainerIsPointerOver || mbMarkdownSourceEmpty);

            EditButton.IsVisible = showEdit;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            EditorContainer.PointerEntered += EditorContainer_PointerEntered;
            EditorContainer.PointerExited += EditorContainer_PointerExited;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            EditorContainer.PointerEntered -= EditorContainer_PointerEntered;
            EditorContainer.PointerExited -= EditorContainer_PointerExited;
        }

        private void EditorContainer_PointerEntered(object? sender, PointerEventArgs e)
        {
            mbEditorContainerIsPointerOver = true;
            UpdateEditButtonVisibility();
        }

        private void EditorContainer_PointerExited(object? sender, PointerEventArgs e)
        {
            mbEditorContainerIsPointerOver = false;
            UpdateEditButtonVisibility();
        }

        // ------------------------------------------------------------------
        // Event handlers
        // ------------------------------------------------------------------

        private void MarkdownSourceEditor_LostFocus(object? sender, RoutedEventArgs e)
        {
            // The binding (UpdateSourceTrigger=LostFocus) has already committed the value.
            IsEditModeEnabled = false;
        }

        private void MarkdownSourceEditor_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                IsEditModeEnabled = false;
                e.Handled = true;
            }
        }

        private void EditButton_Click(object? sender, RoutedEventArgs e)
        {
            IsEditModeEnabled = true;
            MarkdownSourceEditor.Focus();
        }
    }
}
