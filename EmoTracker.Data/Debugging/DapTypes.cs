using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace EmoTracker.Data.Debugging
{
    // Debug Adapter Protocol (DAP) message types.
    //
    // The wire format is JSON with a Content-Length-prefixed framing
    // (same as LSP). We use Newtonsoft.Json (already a project
    // dependency) and dynamic JObject for request/response bodies so
    // we can grow the surface incrementally without churning record
    // definitions every time a new field surfaces.
    //
    // Spec: https://microsoft.github.io/debug-adapter-protocol/specification

    /// <summary>
    /// Common envelope for every DAP message: request, response, or
    /// event. The <see cref="Type"/> discriminates; the rest of the
    /// fields are populated based on which kind it is.
    /// </summary>
    public sealed class DapMessage
    {
        [JsonProperty("seq")]
        public int Seq { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        // Request fields:
        [JsonProperty("command", NullValueHandling = NullValueHandling.Ignore)]
        public string Command { get; set; }

        [JsonProperty("arguments", NullValueHandling = NullValueHandling.Ignore)]
        public JObject Arguments { get; set; }

        // Response fields:
        [JsonProperty("request_seq", NullValueHandling = NullValueHandling.Ignore)]
        public int? RequestSeq { get; set; }

        [JsonProperty("success", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Success { get; set; }

        [JsonProperty("message", NullValueHandling = NullValueHandling.Ignore)]
        public string Message { get; set; }

        [JsonProperty("body", NullValueHandling = NullValueHandling.Ignore)]
        public JObject Body { get; set; }

        // Event fields:
        [JsonProperty("event", NullValueHandling = NullValueHandling.Ignore)]
        public string Event { get; set; }
    }

    /// <summary>
    /// DAP "Source" descriptor. We populate <see cref="Path"/> with the
    /// absolute on-disk path when we can resolve it (dev-mode pack
    /// loaded from a folder), or fall back to the pack-relative
    /// <see cref="Name"/> for archive-loaded packs.
    /// </summary>
    public sealed class DapSource
    {
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }

        [JsonProperty("path", NullValueHandling = NullValueHandling.Ignore)]
        public string Path { get; set; }

        [JsonProperty("sourceReference", NullValueHandling = NullValueHandling.Ignore)]
        public int? SourceReference { get; set; }
    }

    /// <summary>
    /// DAP "Breakpoint" — what we send back from setBreakpoints. The
    /// <see cref="Verified"/> flag tells VS Code whether the line is
    /// known to map to executable code; for v1 we always set true and
    /// let the hook silently miss if the line isn't reachable.
    /// </summary>
    public sealed class DapBreakpoint
    {
        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public int? Id { get; set; }

        [JsonProperty("verified")]
        public bool Verified { get; set; }

        [JsonProperty("line", NullValueHandling = NullValueHandling.Ignore)]
        public int? Line { get; set; }

        [JsonProperty("source", NullValueHandling = NullValueHandling.Ignore)]
        public DapSource Source { get; set; }

        [JsonProperty("message", NullValueHandling = NullValueHandling.Ignore)]
        public string Message { get; set; }
    }

    /// <summary>
    /// DAP "Thread" — DAP's term for an independent execution context.
    /// We map one DAP thread to one tracked <see cref="ScriptManager"/>
    /// (definitional state, primary state, each fork). VS Code's
    /// thread-picker doubles as our state-picker.
    /// </summary>
    public sealed class DapThread
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }

    /// <summary>
    /// One frame in a Lua call stack. <see cref="Id"/> is a server-side
    /// handle that subsequent <c>scopes</c> requests use to pull the
    /// frame's locals/upvalues.
    /// </summary>
    public sealed class DapStackFrame
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("source", NullValueHandling = NullValueHandling.Ignore)]
        public DapSource Source { get; set; }

        [JsonProperty("line")]
        public int Line { get; set; }

        [JsonProperty("column")]
        public int Column { get; set; }

        [JsonProperty("presentationHint", NullValueHandling = NullValueHandling.Ignore)]
        public string PresentationHint { get; set; }
    }

    /// <summary>
    /// "Locals" / "Upvalues" / "Globals" buckets exposed against a
    /// stack frame. The <see cref="VariablesReference"/> is a server
    /// handle to fetch the bucket's contents on demand (DAP is
    /// pull-driven; we never push entire variable trees up front).
    /// </summary>
    public sealed class DapScope
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("variablesReference")]
        public int VariablesReference { get; set; }

        [JsonProperty("expensive")]
        public bool Expensive { get; set; }

        [JsonProperty("presentationHint", NullValueHandling = NullValueHandling.Ignore)]
        public string PresentationHint { get; set; }
    }

    /// <summary>
    /// One leaf or branch in the variable tree. A non-zero
    /// <see cref="VariablesReference"/> marks the row as expandable;
    /// VS Code follows up with a <c>variables</c> request using that
    /// handle to list the children.
    /// </summary>
    public sealed class DapVariable
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
        public string Type { get; set; }

        [JsonProperty("variablesReference")]
        public int VariablesReference { get; set; }

        [JsonProperty("namedVariables", NullValueHandling = NullValueHandling.Ignore)]
        public int? NamedVariables { get; set; }

        [JsonProperty("indexedVariables", NullValueHandling = NullValueHandling.Ignore)]
        public int? IndexedVariables { get; set; }

        [JsonProperty("presentationHint", NullValueHandling = NullValueHandling.Ignore)]
        public JObject PresentationHint { get; set; }
    }

    /// <summary>
    /// DAP "stopped" event body. Sent when a debuggee pauses (hit a
    /// breakpoint, finished a step, raised a Lua error with exception
    /// break enabled, or got a manual pause).
    /// </summary>
    public static class DapStopReason
    {
        public const string Step = "step";
        public const string Breakpoint = "breakpoint";
        public const string Exception = "exception";
        public const string Pause = "pause";
        public const string Entry = "entry";
    }
}
