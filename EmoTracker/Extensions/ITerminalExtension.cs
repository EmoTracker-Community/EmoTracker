using EmoTracker.Data.Sessions;
using System;
using System.Collections.Generic;

namespace EmoTracker.Extensions
{
    /// <summary>
    /// One slash command contributed by an <see cref="ITerminalExtension"/>.
    /// Slash commands are typed as <c>/name args...</c> in the developer
    /// terminal's input field; the terminal parses out the name (no
    /// leading slash) and dispatches to the matching command's
    /// <see cref="Execute"/> delegate with the rest of the input
    /// passed as a single arg string. Splitting / parsing of args is
    /// the command's responsibility.
    /// </summary>
    public sealed class TerminalCommand
    {
        /// <summary>Slash-command name, no leading slash. Matched case-insensitively.</summary>
        public string Name { get; init; }

        /// <summary>One-line description shown by <c>/help</c>.</summary>
        public string Description { get; init; }

        /// <summary>
        /// Invoked when the user types <c>/Name args</c>. The
        /// <c>state</c> is the <see cref="TrackerState"/> the terminal
        /// is currently bound to (the user's selected tab via the
        /// terminal's state picker, NOT necessarily the active window's
        /// active tab); commands operate against that state. The
        /// <c>args</c> string is everything after the first whitespace
        /// following the command name, or empty if the user typed only
        /// <c>/Name</c>.
        /// </summary>
        public Action<TrackerState, string> Execute { get; init; }
    }

    /// <summary>
    /// Per-state extension that contributes slash commands to the
    /// developer terminal. One instance per <see cref="TrackerState"/>
    /// — discovered via <c>TypeRegistry&lt;ITerminalExtension&gt;</c>
    /// and instantiated by <see cref="ExtensionManager"/> when a state
    /// is registered with its owning <see cref="PackageInstance"/>.
    /// Disposed when the state is unregistered.
    ///
    /// <para>
    /// Slash commands run with the bound state as their context, so an
    /// extension that wants different behaviour per state (or that
    /// captures per-state references) gets a fresh instance for each
    /// state.
    /// </para>
    ///
    /// <para>
    /// Built-in commands (<c>/clear</c>, <c>/help</c>) are provided by
    /// <c>DeveloperTerminalCoreExtension</c>. Pack-author or
    /// app-feature contributors implement this interface to add their
    /// own.
    /// </para>
    /// </summary>
    public interface ITerminalExtension : IExtension
    {
        /// <summary>Called once when this instance is bound to a state.</summary>
        void OnAttachedToState(TrackerState state);

        /// <summary>Called once when the state is being torn down.</summary>
        void OnDetachedFromState(TrackerState state);

        /// <summary>
        /// The slash commands this extension contributes. Read once
        /// after <see cref="OnAttachedToState"/>; if a command list
        /// changes mid-state-lifetime, the terminal will pick up the
        /// new list on its next render pass.
        /// </summary>
        IReadOnlyList<TerminalCommand> Commands { get; }

        /// <summary>
        /// Allocate a fresh instance bound to <paramref name="destState"/>.
        /// Mirrors <see cref="ITrackerExtension.Fork"/>; called when a
        /// state is forked so per-state command-context state carries
        /// across.
        /// </summary>
        ITerminalExtension Fork(TrackerState destState);
    }
}
