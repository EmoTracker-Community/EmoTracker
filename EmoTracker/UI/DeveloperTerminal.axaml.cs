using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using EmoTracker.Data;
using EmoTracker.Data.Sessions;
using EmoTracker.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;

namespace EmoTracker.UI
{
    /// <summary>
    /// Developer terminal — VS Code-styled multiline scrollback over
    /// the bound <see cref="TrackerState"/>'s <c>ScriptManager.LogOutput</c>,
    /// plus an input field that parses slash commands (dispatched
    /// against <see cref="ITerminalExtension"/> instances on the bound
    /// state) and Lua statements (executed via the bound state's
    /// <c>ScriptManager.ExecuteLuaString</c>).
    ///
    /// <para>
    /// Per-tab persistence: each <see cref="TrackerState"/> the user
    /// has bound the terminal to retains its own input history (up /
    /// down recall) and current input-field text. Switching the bound
    /// state via the header dropdown swaps in that state's context;
    /// the scrollback follows naturally because it's read straight
    /// from the bound state's ScriptManager.
    /// </para>
    /// </summary>
    public partial class DeveloperTerminal : Window
    {
        // -------- Per-tab terminal context ---------------------------------

        sealed class TerminalContext
        {
            public List<string> History = new();   // user input history (most-recent appended)
            public int HistoryIndex = -1;          // -1 = past-the-end (current draft)
            public string Draft = "";              // user's in-progress edit at the bottom
        }

        readonly Dictionary<TrackerState, TerminalContext> mContexts = new();
        TrackerState mBoundState;
        TerminalContext mBoundContext;

        public DeveloperTerminal()
        {
            InitializeComponent();
            Opened += OnOpened;
            Closed += OnClosed;

            // Bind the scrollback widget's MinHeight to the scroller's
            // viewport height so empty space below the last log line
            // is part of the SelectableTextBlock's hit area — drag-
            // selecting from anywhere in the visible scrollback (not
            // just on top of text) starts a real selection. We update
            // on every viewport property change.
            var scroller = this.FindControl<ScrollViewer>("ScrollbackScroller");
            var text = this.FindControl<Avalonia.Controls.SelectableTextBlock>("ScrollbackText");
            if (scroller != null && text != null)
            {
                scroller.PropertyChanged += (_, ev) =>
                {
                    if (ev.Property == ScrollViewer.ViewportProperty)
                    {
                        // Subtract a couple of pixels so the SelectableTextBlock
                        // never grows past the viewport (which would push the
                        // text up and create a redundant scrollbar).
                        text.MinHeight = System.Math.Max(0, scroller.Viewport.Height - 2);
                    }
                };
            }
        }

        // ---- Lifecycle ---------------------------------------------------

        void OnOpened(object sender, EventArgs e)
        {
            // Default to the active window's active tab.
            var seed = ApplicationModel.Instance?.CurrentlyActiveWindowContext?.ActiveState
                      ?? ApplicationModel.Instance?.PrimaryState;
            BindToState(seed);
            // Focus the input so the terminal feels immediately usable.
            this.FindControl<TextBox>("InputField")?.Focus();
        }

        void OnClosed(object sender, EventArgs e)
        {
            UnbindCurrentState();
        }

        // ---- State binding -----------------------------------------------

        void BindToState(TrackerState state)
        {
            if (ReferenceEquals(state, mBoundState)) return;

            UnbindCurrentState();
            mBoundState = state;
            mBoundContext = state == null ? null : GetOrCreateContext(state);

            // Render the bound state's LogOutput into the single
            // SelectableTextBlock. Cross-line selection works because
            // it's all one widget. We rebuild the Inlines on every
            // change (additions are O(1) and the scrollback caps at
            // 500 lines, so even a full rebuild is cheap). Listening
            // for collection changes also drives auto-scroll.
            RenderScrollback();

            if (state?.Scripts?.LogOutput is INotifyCollectionChanged ncc)
                ncc.CollectionChanged += OnLogOutputChanged;

            // Restore the saved input draft for this state (defaults to
            // "" on first visit).
            var input = this.FindControl<TextBox>("InputField");
            if (input != null && mBoundContext != null)
            {
                input.Text = mBoundContext.Draft;
                input.CaretIndex = input.Text.Length;
            }

            UpdateStatePickerLabel();
            ScrollToEnd();
        }

        void UnbindCurrentState()
        {
            // Save the current input draft into the bound context so a
            // future re-bind restores it.
            if (mBoundContext != null)
            {
                var input = this.FindControl<TextBox>("InputField");
                if (input != null)
                    mBoundContext.Draft = input.Text ?? "";
            }
            if (mBoundState?.Scripts?.LogOutput is INotifyCollectionChanged ncc)
                ncc.CollectionChanged -= OnLogOutputChanged;
        }

        TerminalContext GetOrCreateContext(TrackerState state)
        {
            if (!mContexts.TryGetValue(state, out var ctx))
            {
                ctx = new TerminalContext();
                mContexts[state] = ctx;
            }
            return ctx;
        }

        void UpdateStatePickerLabel()
        {
            var label = this.FindControl<TextBlock>("StatePickerLabel");
            if (label == null) return;
            label.Text = mBoundState == null
                ? "(no state)"
                : (mBoundState.Name ?? "(unnamed)");
        }

        // ---- Scrollback auto-scroll --------------------------------------

        DateTime mLastUserScrollTime = DateTime.MinValue;

        void OnLogOutputChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Re-render the SelectableTextBlock's inlines and re-scroll.
            // The OutputRaw path can fire from any thread; ensure the UI
            // mutation happens on the dispatcher.
            Dispatcher.UIThread.Post(() =>
            {
                RenderScrollback();
                ScrollToEnd();
            });
        }

        // Build a single colored-Run-per-line stream into the
        // SelectableTextBlock. One widget = native cross-line selection
        // (drag from line N's middle through line M's end and Ctrl+C
        // captures the whole range, including line breaks). Per-line
        // colors carried via Run.Foreground.
        void RenderScrollback()
        {
            var tb = this.FindControl<Avalonia.Controls.SelectableTextBlock>("ScrollbackText");
            if (tb == null) return;
            tb.Inlines = null; // reset between binds

            var lines = mBoundState?.Scripts?.LogOutput;
            if (lines == null) return;

            var inlines = new Avalonia.Controls.Documents.InlineCollection();
            bool first = true;
            foreach (var line in lines)
            {
                if (!first) inlines.Add(new Avalonia.Controls.Documents.LineBreak());
                first = false;

                var run = new Avalonia.Controls.Documents.Run(line.Text ?? "");
                if (!string.IsNullOrEmpty(line.Color))
                {
                    try
                    {
                        run.Foreground = Avalonia.Media.SolidColorBrush.Parse(line.Color);
                    }
                    catch
                    {
                        // Unknown color string → fall back to the default foreground.
                    }
                }
                inlines.Add(run);
            }
            tb.Inlines = inlines;
        }

        void ScrollToEnd()
        {
            var scroll = this.FindControl<ScrollViewer>("ScrollbackScroller");
            if (scroll == null) return;
            // If the user scrolled away from the bottom recently, don't
            // yank them back — only auto-scroll when they were at (or
            // near) the bottom already.
            double maxScroll = scroll.Extent.Height - scroll.Viewport.Height;
            if ((DateTime.Now - mLastUserScrollTime).TotalSeconds < 30 &&
                scroll.Offset.Y < maxScroll - 32)
            {
                return;
            }
            scroll.Offset = new Vector(scroll.Offset.X, maxScroll);
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            mLastUserScrollTime = DateTime.Now;
            base.OnPointerWheelChanged(e);
        }

        // ---- Input parsing ------------------------------------------------

        void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            var input = sender as TextBox;
            if (input == null) return;

            // Auto-complete popup intercepts arrow / Tab / Escape /
            // Enter while it's open and the input is mid-slash-command.
            // The completion popup stays open as the user types and
            // closes on Escape or successful accept.
            bool acOpen = mAutoCompletePopupOpen;
            switch (e.Key)
            {
                case Key.Enter:
                    if (acOpen && mAutoCompleteSelectedIndex >= 0)
                    {
                        // Accept the highlighted suggestion. If the
                        // command takes no further args, submit
                        // immediately; otherwise leave a trailing
                        // space and let the user finish typing.
                        AcceptAutoComplete(input, submitOnAccept: true);
                        e.Handled = true;
                        return;
                    }
                    SubmitInput(input);
                    e.Handled = true;
                    break;

                case Key.Tab:
                    if (acOpen)
                    {
                        AcceptAutoComplete(input, submitOnAccept: false);
                        e.Handled = true;
                    }
                    break;

                case Key.Escape:
                    if (acOpen)
                    {
                        CloseAutoComplete();
                        e.Handled = true;
                    }
                    break;

                case Key.Up:
                    if (acOpen)
                    {
                        MoveAutoCompleteSelection(-1);
                        e.Handled = true;
                    }
                    else
                    {
                        NavigateHistory(input, -1);
                        e.Handled = true;
                    }
                    break;

                case Key.Down:
                    if (acOpen)
                    {
                        MoveAutoCompleteSelection(+1);
                        e.Handled = true;
                    }
                    else
                    {
                        NavigateHistory(input, +1);
                        e.Handled = true;
                    }
                    break;

                case Key.L when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                    // Ctrl+L = clear, terminal convention
                    mBoundState?.Scripts?.ClearLogCommand?.Execute(null);
                    e.Handled = true;
                    break;
            }
        }

        // ---- Slash auto-complete -----------------------------------------

        bool mAutoCompletePopupOpen;
        int mAutoCompleteSelectedIndex = -1;
        readonly System.Collections.Generic.List<TerminalCommand> mAutoCompleteCandidates = new();

        void OnInputTextChanged(object sender, TextChangedEventArgs e)
        {
            var input = sender as TextBox;
            if (input == null) return;
            RefreshAutoComplete(input);
        }

        void RefreshAutoComplete(TextBox input)
        {
            var text = input.Text ?? "";
            // Auto-complete is slash-command-only. We trigger it once
            // the user has typed `/` at the start of the input. Once
            // they hit a space (which means they've moved past the
            // command name onto its args) we close the popup.
            if (!text.StartsWith("/") || text.Contains(' ') || mBoundState == null)
            {
                CloseAutoComplete();
                return;
            }

            string prefix = text.Substring(1);   // strip leading '/'

            // Aggregate every command from every ITerminalExtension on
            // the bound state, filter by the typed prefix (case-insensitive
            // StartsWith), order by name, dedupe by name.
            var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            mAutoCompleteCandidates.Clear();
            foreach (var ext in ExtensionManager.Instance.GetTerminalExtensions(mBoundState))
            {
                var cmds = ext.Commands;
                if (cmds == null) continue;
                foreach (var cmd in cmds)
                {
                    if (cmd?.Name == null) continue;
                    if (!seen.Add(cmd.Name)) continue;
                    if (prefix.Length == 0 ||
                        cmd.Name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    {
                        mAutoCompleteCandidates.Add(cmd);
                    }
                }
            }
            mAutoCompleteCandidates.Sort((a, b) =>
                System.StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));

            if (mAutoCompleteCandidates.Count == 0)
            {
                CloseAutoComplete();
                return;
            }

            // Reset selection to top whenever the candidate list changes.
            mAutoCompleteSelectedIndex = 0;
            RenderAutoCompleteItems();
            OpenAutoComplete();
        }

        void RenderAutoCompleteItems()
        {
            var host = this.FindControl<ItemsControl>("SlashCompleteItems");
            if (host == null) return;

            var rows = new System.Collections.Generic.List<Control>();
            for (int i = 0; i < mAutoCompleteCandidates.Count; ++i)
            {
                rows.Add(BuildAutoCompleteRow(mAutoCompleteCandidates[i], i));
            }
            host.ItemsSource = rows;
        }

        Border BuildAutoCompleteRow(TerminalCommand cmd, int index)
        {
            bool isSelected = index == mAutoCompleteSelectedIndex;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            var nameTb = new TextBlock
            {
                Text = "/" + cmd.Name,
                FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x4F, 0xC1, 0xFF)),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                MinWidth = 100,
            };
            Grid.SetColumn(nameTb, 0);
            grid.Children.Add(nameTb);

            if (!string.IsNullOrEmpty(cmd.Description))
            {
                var descTb = new TextBlock
                {
                    Text = cmd.Description,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90)),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                Grid.SetColumn(descTb, 1);
                grid.Children.Add(descTb);
            }

            var row = new Border
            {
                Padding = new Thickness(10, 5, 10, 5),
                Background = isSelected
                    ? new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D))
                    : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = grid,
            };

            int capturedIndex = index;
            row.PointerEntered += (_, __) =>
            {
                if (mAutoCompleteSelectedIndex != capturedIndex)
                {
                    mAutoCompleteSelectedIndex = capturedIndex;
                    RenderAutoCompleteItems();
                }
            };
            row.PointerReleased += (_, ev) =>
            {
                if (ev.InitialPressMouseButton != MouseButton.Left) return;
                mAutoCompleteSelectedIndex = capturedIndex;
                var input = this.FindControl<TextBox>("InputField");
                if (input != null) AcceptAutoComplete(input, submitOnAccept: false);
            };

            return row;
        }

        void OpenAutoComplete()
        {
            var popup = this.FindControl<Avalonia.Controls.Primitives.Popup>("SlashCompletePopup");
            if (popup == null) return;
            popup.IsOpen = true;
            mAutoCompletePopupOpen = true;
        }

        void CloseAutoComplete()
        {
            var popup = this.FindControl<Avalonia.Controls.Primitives.Popup>("SlashCompletePopup");
            if (popup != null) popup.IsOpen = false;
            mAutoCompletePopupOpen = false;
            mAutoCompleteSelectedIndex = -1;
            mAutoCompleteCandidates.Clear();
        }

        void MoveAutoCompleteSelection(int delta)
        {
            if (mAutoCompleteCandidates.Count == 0) return;
            mAutoCompleteSelectedIndex =
                (mAutoCompleteSelectedIndex + delta + mAutoCompleteCandidates.Count) %
                mAutoCompleteCandidates.Count;
            RenderAutoCompleteItems();
        }

        // Accepts the currently-highlighted candidate. If
        // submitOnAccept is true (Enter), submit the now-completed
        // command directly. Otherwise (Tab / mouse click), leave the
        // command in the input field with a trailing space and let
        // the user keep typing args.
        void AcceptAutoComplete(TextBox input, bool submitOnAccept)
        {
            if (mAutoCompleteSelectedIndex < 0 ||
                mAutoCompleteSelectedIndex >= mAutoCompleteCandidates.Count) return;
            var cmd = mAutoCompleteCandidates[mAutoCompleteSelectedIndex];
            string completion = "/" + cmd.Name + " ";
            input.Text = completion;
            input.CaretIndex = completion.Length;
            CloseAutoComplete();

            if (submitOnAccept)
            {
                SubmitInput(input);
            }
            else
            {
                // Re-focus the input so further typing routes here
                // (a popup item click would otherwise leave focus on
                // the popup's row).
                input.Focus();
            }
        }

        void SubmitInput(TextBox input)
        {
            var text = input.Text ?? "";
            if (string.IsNullOrWhiteSpace(text))
            {
                // Echo a blank prompt line so successive Enters look
                // like a real terminal cadence.
                mBoundState?.Scripts?.OutputRaw("");
                return;
            }

            // Record into history (dedupe consecutive duplicates so
            // hammering Enter doesn't bloat recall).
            if (mBoundContext != null)
            {
                if (mBoundContext.History.Count == 0 ||
                    !string.Equals(mBoundContext.History[^1], text, StringComparison.Ordinal))
                {
                    mBoundContext.History.Add(text);
                }
                mBoundContext.HistoryIndex = -1;
                mBoundContext.Draft = "";
            }

            // Echo the user's input back to the scrollback so the
            // session reads as a transcript. Use the bridge prompt
            // glyph to make it visually distinct.
            mBoundState?.Scripts?.OutputRaw("❯ " + text, "DeepSkyBlue");

            input.Text = "";

            // Slash command vs Lua statement.
            if (text.StartsWith("/"))
            {
                ExecuteSlashCommand(text.Substring(1));
            }
            else
            {
                ExecuteLuaStatement(text);
            }
        }

        void ExecuteSlashCommand(string commandLine)
        {
            // Parse "name args..." — split on first whitespace.
            string name;
            string args;
            int sp = commandLine.IndexOfAny(new[] { ' ', '\t' });
            if (sp < 0) { name = commandLine; args = ""; }
            else { name = commandLine.Substring(0, sp); args = commandLine.Substring(sp + 1); }

            if (mBoundState == null)
            {
                mBoundState?.Scripts?.OutputError("No state bound; cannot dispatch slash command.");
                return;
            }

            // Find the command across every ITerminalExtension on the
            // bound state. Match case-insensitively.
            foreach (var ext in ExtensionManager.Instance.GetTerminalExtensions(mBoundState))
            {
                var cmds = ext.Commands;
                if (cmds == null) continue;
                foreach (var cmd in cmds)
                {
                    if (string.Equals(cmd.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            cmd.Execute?.Invoke(mBoundState, args);
                        }
                        catch (Exception ex)
                        {
                            mBoundState.Scripts?.OutputError(
                                $"Slash command /{name} threw: {ex.Message}");
                        }
                        return;
                    }
                }
            }

            mBoundState.Scripts?.OutputError(
                $"Unknown slash command: /{name}. Type /help for a list.");
        }

        void ExecuteLuaStatement(string text)
        {
            if (mBoundState?.Scripts == null)
            {
                mBoundState?.Scripts?.OutputError("No state bound; cannot execute Lua.");
                return;
            }

            try
            {
                // Try as an expression first by wrapping in `return ...`
                // so the user can type `Tracker.SwapLeftRight` and see
                // its value. If that doesn't compile, fall through to
                // executing as a statement (assignment, function call,
                // multi-line block).
                object[] result = null;
                bool ranAsExpression = false;
                try
                {
                    result = mBoundState.Scripts.ExecuteLuaString("return " + text);
                    ranAsExpression = true;
                }
                catch
                {
                    // Not a valid expression; fall through to statement.
                }

                if (!ranAsExpression)
                {
                    result = mBoundState.Scripts.ExecuteLuaString(text);
                }

                if (result != null && result.Length > 0)
                {
                    foreach (var r in result)
                        mBoundState.Scripts.Output(FormatLuaValue(r));
                }
            }
            catch (Exception ex)
            {
                mBoundState.Scripts.OutputError(ex.Message);
            }
        }

        static string FormatLuaValue(object v)
        {
            if (v == null) return "nil";
            if (v is bool b) return b ? "true" : "false";
            if (v is string s) return "\"" + s + "\"";
            return v.ToString();
        }

        void NavigateHistory(TextBox input, int direction)
        {
            if (mBoundContext == null) return;
            var hist = mBoundContext.History;
            if (hist.Count == 0) return;

            if (mBoundContext.HistoryIndex == -1)
            {
                // Save the current draft so the user can return to it
                // by pressing Down past the most recent entry.
                mBoundContext.Draft = input.Text ?? "";
            }

            int newIdx;
            if (mBoundContext.HistoryIndex == -1)
            {
                if (direction > 0) return;   // Down at the draft = no-op
                newIdx = hist.Count - 1;
            }
            else
            {
                newIdx = mBoundContext.HistoryIndex + direction;
                if (newIdx < 0) newIdx = 0;
                if (newIdx >= hist.Count) newIdx = -1;
            }

            mBoundContext.HistoryIndex = newIdx;
            input.Text = newIdx == -1 ? mBoundContext.Draft : hist[newIdx];
            input.CaretIndex = input.Text.Length;
        }

        // ---- Header buttons ----------------------------------------------

        void OnCopyAllClicked(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (mBoundState?.Scripts?.LogOutput == null) return;
            // The SelectableTextBlock supports drag-select for partial
            // copy. This button copies the entire scrollback regardless
            // of current selection.
            var sb = new StringBuilder();
            foreach (var line in mBoundState.Scripts.LogOutput)
                sb.AppendLine(line.Text ?? "");
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                _ = clipboard.SetTextAsync(sb.ToString());
        }

        void OnClearClicked(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            mBoundState?.Scripts?.ClearLogCommand?.Execute(null);
        }

        // ---- State picker popup ------------------------------------------

        void OnStatePickerClicked(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var popup = this.FindControl<Popup>("StatePickerPopup");
            var items = this.FindControl<ItemsControl>("StatePickerItems");
            if (popup == null || items == null) return;

            // Build entries grouped by PackageInstance, since multiple
            // tabs sharing a pack are visually clearer when grouped.
            var entries = new List<Control>();
            foreach (var pi in ApplicationModel.Instance.PackageInstances)
            {
                var pkgLabel = pi.GamePackage?.DisplayName ?? "(no pack)";
                entries.Add(new TextBlock
                {
                    Text = pkgLabel,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    FontSize = 11,
                    Padding = new Thickness(10, 6, 10, 2),
                });

                foreach (var kvp in pi.States)
                {
                    var state = kvp.Value;
                    if (state == null) continue;
                    entries.Add(BuildStatePickerEntry(state, popup));
                }
            }
            items.ItemsSource = entries;

            popup.IsOpen = !popup.IsOpen;
        }

        Border BuildStatePickerEntry(TrackerState state, Popup popup)
        {
            bool isBound = ReferenceEquals(state, mBoundState);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var dot = new TextBlock
            {
                Text = "●",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0x54)),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                IsVisible = state.IsDirty,
            };
            Grid.SetColumn(dot, 0);
            grid.Children.Add(dot);

            var name = new TextBlock
            {
                Text = state.Name ?? "(unnamed)",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                MinWidth = 160,
            };
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);

            if (isBound)
            {
                var check = new TextBlock
                {
                    Text = "✓",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x4F, 0xC1, 0xFF)),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0),
                };
                Grid.SetColumn(check, 2);
                grid.Children.Add(check);
            }

            var entry = new Border
            {
                Padding = new Thickness(18, 6, 10, 6),
                Background = isBound
                    ? new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D))
                    : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = grid,
            };
            entry.PointerEntered += (_, __) =>
            {
                if (!isBound) entry.Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35));
            };
            entry.PointerExited += (_, __) =>
            {
                if (!isBound) entry.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            };

            var capturedState = state;
            entry.PointerReleased += (_, ev) =>
            {
                if (ev.InitialPressMouseButton != MouseButton.Left) return;
                BindToState(capturedState);
                popup.IsOpen = false;
                this.FindControl<TextBox>("InputField")?.Focus();
            };

            return entry;
        }
    }
}
