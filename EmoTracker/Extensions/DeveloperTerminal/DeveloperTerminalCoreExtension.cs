using EmoTracker.Data.Sessions;
using System.Collections.Generic;
using System.Linq;

namespace EmoTracker.Extensions.DeveloperTerminal
{
    /// <summary>
    /// Built-in terminal extension shipping the universally-useful
    /// slash commands every developer terminal needs: <c>/clear</c>,
    /// <c>/help</c>. Allocated per <see cref="TrackerState"/> so the
    /// commands fire in the bound state's context.
    /// </summary>
    public sealed class DeveloperTerminalCoreExtension : ITerminalExtension
    {
        public string Name => "Terminal Core";
        public string UID => "emotracker_terminal_core";
        public int Priority => -1000;

        TrackerState mState;
        readonly List<TerminalCommand> mCommands = new();

        public IReadOnlyList<TerminalCommand> Commands => mCommands;

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
                    // Aggregate every terminal extension's commands across
                    // this state — including ones contributed by other
                    // ITerminalExtension implementations.
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
        }

        public void OnDetachedFromState(TrackerState state)
        {
            mState = null;
            mCommands.Clear();
        }

        public ITerminalExtension Fork(TrackerState destState)
        {
            // Stateless aside from the bound state and command list; a
            // fresh instance suffices.
            return new DeveloperTerminalCoreExtension();
        }
    }
}
