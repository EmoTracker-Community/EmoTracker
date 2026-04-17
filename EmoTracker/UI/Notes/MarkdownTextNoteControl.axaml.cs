#nullable enable annotations
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EmoTracker.UI.Controls;
using System;
using System.ComponentModel;

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
                if (mbIsEditModeEnabled == value) return;
                mbIsEditModeEnabled = value;

                // Manage visibility directly — RelativeSource bindings on this control
                // cannot observe property changes because ObservableUserControl shadows
                // Avalonia's INotifyPropertyChanged event.
                MarkdownSourceEditor.IsVisible = value;
                MarkdownViewerControl.IsVisible = !value;

                if (!value && MarkdownSourceEditor.IsFocused)
                    TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();

                UpdateEditButtonVisibility();
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

        private INotifyPropertyChanged? _dataContextObservable;

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (_dataContextObservable != null)
                _dataContextObservable.PropertyChanged -= DataContext_PropertyChanged;

            _dataContextObservable = DataContext as INotifyPropertyChanged;

            if (_dataContextObservable != null)
                _dataContextObservable.PropertyChanged += DataContext_PropertyChanged;

            // Sync initial state from the new DataContext.
            SyncMarkdownSourceEmpty();
        }

        private void DataContext_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is "MarkdownSourceEmpty" or "MarkdownSource")
                SyncMarkdownSourceEmpty();
        }

        private void SyncMarkdownSourceEmpty()
        {
            // Read MarkdownSourceEmpty via reflection-free duck-typing on the data model.
            bool empty = true;
            if (DataContext is Data.Notes.MarkdownTextNote note)
                empty = note.MarkdownSourceEmpty;
            MarkdownSourceEmpty = empty;
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
            // Defer focus until the layout pass has made the TextBox visible.
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => MarkdownSourceEditor.Focus(),
                Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }
}
