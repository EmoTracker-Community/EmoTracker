using EmoTracker.Data.Sessions;
using System.Collections.Generic;
using System.Linq;

namespace EmoTracker.Extensions.DeveloperTerminal
{
    /// <summary>
    /// Built-in terminal extension shipping the universal terminal UX
    /// commands every developer terminal needs: <c>/clear</c>,
    /// <c>/help</c>, <c>/echo</c>, <c>/history</c>. Pack-development
    /// commands (<c>/find</c>, <c>/inspect</c>, <c>/reload</c>, etc.)
    /// live on a sibling extension
    /// (<see cref="DeveloperTerminalPackToolsExtension"/>) so this
    /// surface stays focused on terminal mechanics.
    ///
    /// <para>
    /// Also owns the per-state input history + current draft text so
    /// <c>/history</c> has somewhere to read from. The
    /// <see cref="UI.DeveloperTerminal"/> UI delegates its history /
    /// draft storage here rather than maintaining its own dictionary,
    /// which keeps the per-state state in one place — and means a fork
    /// of the state naturally inherits or resets terminal context
    /// alongside its other per-state extensions.
    /// </para>
    /// </summary>
    public sealed class DeveloperTerminalCoreExtension : ITerminalExtension
    {
        public string Name => "Terminal Core";
        public string UID => "emotracker_terminal_core";
        public int Priority => -1000;

        TrackerState mState;
        readonly List<TerminalCommand> mCommands = new();

        public IReadOnlyList<TerminalCommand> Commands => mCommands;

        // Per-state input history + draft text. Owned here (not on the
        // UI window) so /history can read from it and so a fork of the
        // state cleanly gets a fresh context via the per-state Fork()
        // contract.
        public List<string> InputHistory { get; } = new();
        public int HistoryIndex { get; set; } = -1;   // -1 = past-the-end (current draft)
        public string Draft { get; set; } = "";

        public void OnAttachedToState(TrackerState state)
        {
            mState = state;
            mCommands.Clear();

            mCommands.Add(new TerminalCommand
            {
                Name = "clear",
                Description = "Clear the terminal scrollback for the bound state.",
                Execute = (s, _) => s?.Scripts.ClearLogCommand?.Execute(null),
            });

            mCommands.Add(new TerminalCommand
            {
                Name = "help",
                Description = "List every available slash command.",
                Execute = (s, _) =>
                {
                    if (s?.Scripts == null) return;
                    s.Scripts.Output("Available slash commands:");
                    var all = ExtensionManager.Instance
                        .GetTerminalExtensions(s)
                        .SelectMany(ext => ext.Commands ?? System.Array.Empty<TerminalCommand>())
                        .OrderBy(c => c.Name, System.StringComparer.OrdinalIgnoreCase);
                    foreach (var cmd in all)
                    {
                        var desc = string.IsNullOrEmpty(cmd.Description) ? "" : "  — " + cmd.Description;
                        s.Scripts.Output($"  /{cmd.Name}{desc}");
                    }
                    s.Scripts.Output("Anything that doesn't start with '/' is evaluated as a Lua statement.");
                },
            });

            mCommands.Add(new TerminalCommand
            {
                Name = "echo",
                Description = "Write the argument text directly to the scrollback.",
                Execute = (s, args) => s?.Scripts.Output(args ?? ""),
            });

            mCommands.Add(new TerminalCommand
            {
                Name = "history",
                Description = "Print this tab's input history (most recent last).",
                Execute = (s, _) =>
                {
                    if (s?.Scripts == null) return;
                    if (InputHistory.Count == 0)
                    {
                        s.Scripts.Output("(no history)");
                        return;
                    }
                    int pad = InputHistory.Count.ToString().Length;
                    for (int i = 0; i < InputHistory.Count; ++i)
                    {
                        var idx = (i + 1).ToString().PadLeft(pad);
                        s.Scripts.Output($"  {idx}  {InputHistory[i]}");
                    }
                },
            });
        }

        public void OnDetachedFromState(TrackerState state)
        {
            mState = null;
            mCommands.Clear();
            InputHistory.Clear();
            HistoryIndex = -1;
            Draft = "";
        }

        public ITerminalExtension Fork(TrackerState destState)
        {
            // Fresh terminal context for the fork. We could carry the
            // source's input history across, but a fork is intuitively
            // "a fresh terminal session" — past commands typed against
            // the source aren't necessarily relevant to the fork. Keep
            // it clean.
            return new DeveloperTerminalCoreExtension();
        }
    }
}
