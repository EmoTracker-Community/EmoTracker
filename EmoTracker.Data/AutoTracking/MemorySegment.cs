using EmoTracker.Core.DataModel;
using EmoTracker.Core.Services;
using EmoTracker.Data.Packages;
using EmoTracker.Data.Scripting;
using NLua;
using System;

namespace EmoTracker.Data.AutoTracking
{
    /// <summary>
    /// A live window into a region of game memory monitored by the
    /// autotracker. Stores a pair of byte buffers (most-recent-known +
    /// working) and surfaces <see cref="ReadUInt8"/> / <see cref="ReadUInt16"/>
    /// for pack scripts.
    ///
    /// <para>
    /// <b>Per-state data-model object (Phase 7.13):</b> inherits
    /// <see cref="ModelTypeBase"/> so each segment has a stable
    /// <see cref="ModelTypeBase.DefinitionId"/> that survives state forks.
    /// Pack scripts cache segment references in Lua tables (e.g.
    /// CodeTracker's <c>SEGMENTS.ItemData = ScriptHost:AddMemoryWatch(...)</c>);
    /// when a state forks, the LuaStateCloner's <c>ModelTypeBase</c> branch
    /// resolves the source's DefinitionId in the fork's
    /// <see cref="IModelResolver"/> and remaps the reference to the fork's
    /// own segment, so subsequent reads on the fork hit the fork's buffer
    /// (driven by the fork's autotracker).
    /// </para>
    ///
    /// <para>
    /// The Lua-callback variant lives in <see cref="LuaMemorySegment"/>;
    /// instances of bare <see cref="MemorySegment"/> are useful for tests
    /// and for non-Lua subscribers that just want to read fresh memory
    /// data (override <see cref="OnSegmentDataUpdated"/>).
    /// </para>
    /// </summary>
    public class MemorySegment : ModelTypeBase, IMemorySegment, IUpdateWithConnector, IDisposable
    {
        #region -- Global Event Hooks --

        public delegate void MemorySegmentUpdatedHandler(MemorySegment segment, IAutoTrackingProvider provider, PackageManager.Game game);

        /// <summary>
        /// Invoked when a memory segment's contents (in watched memory) have changed
        /// </summary>
        public static event MemorySegmentUpdatedHandler OnMemorySegmentModified;

        /// <summary>
        /// Invoked when a memory segment's contents have been read from watched memory
        /// </summary>
        public static event MemorySegmentUpdatedHandler OnMemorySegmentUpdated;

        #endregion

        // Identity (immutable; carried across fork via shared ImmutableData seed below).
        string mName;
        ulong mStartAddress;
        ulong mLength;
        int mPeriod = 500;

        // Per-state runtime state (fresh on each fork).
        DateTime mLastUpdate;
        bool mbDirty;
        bool mbFrozen;
        byte[][] mBuffers;

        public string Name => mName;
        public int Period => mPeriod;
        public ulong StartAddress => mStartAddress;
        public ulong EndAddress => mStartAddress + (Length - 1);
        public ulong Length => mLength;

        public bool Dirty
        {
            get { lock (this) { return mbDirty; } }
            set { lock (this) { mbDirty = value; } }
        }

        public bool Frozen
        {
            get { return mbFrozen; }
            protected set { mbFrozen = value; }
        }

        byte[] ReadBuffer => mBuffers[0];
        byte[] WriteBuffer => mBuffers[1];

        public bool ContainsAddress(ulong address)
        {
            if (address >= mStartAddress)
            {
                ulong offset = address - mStartAddress;
                if (offset < mLength)
                    return true;
            }

            return false;
        }

        ulong GetOffsetForAddress(ulong address)
        {
            if (address >= mStartAddress)
            {
                ulong offset = address - mStartAddress;
                if (offset < mLength)
                    return offset;
            }

            throw new InvalidOperationException("Address is not contained within this segment");
        }

        public byte ReadUInt8(ulong address, bool bRawRead = false)
        {
            if (ReadBuffer != null)
            {
                try
                {
                    ulong offset = GetOffsetForAddress(address);
                    return ReadBuffer[offset];
                }
                catch
                {
                    var scripts = (this.OwnerState as Sessions.TrackerState)?.Scripts;
                    scripts?.OutputError("Address 0x{0:x} is out of range of segment '{3}' = [0x{1:x}:0x{2:x}]",
                        address, StartAddress, StartAddress + Length, Name);
                }
            }

            return 0;
        }

        public sbyte ReadInt8(ulong address, bool bRawRead = false)
        {
            return unchecked((sbyte)ReadUInt8(address));
        }

        public ushort ReadUInt16(ulong address, bool bRawRead = false)
        {
            if (ReadBuffer != null)
            {
                try
                {
                    GetOffsetForAddress(address);

                    byte b0 = ReadUInt8(address);
                    byte b1 = ReadUInt8(address + 1);

                    ushort value = (ushort)((uint)b1 << 8 | b0);
                    return value;
                }
                catch
                {
                    var scripts = (this.OwnerState as Sessions.TrackerState)?.Scripts;
                    scripts?.OutputError("Address 0x{0:x} is out of range of segment '{3}' = [0x{1:x}:0x{2:x}]",
                        address, StartAddress, StartAddress + Length, Name);
                }
            }

            return 0;
        }

        public short ReadInt16(ulong address, bool bRawRead = false)
        {
            return 0;
        }

        public MemorySegment(string name, ulong startAddress, ulong length, int period = 500)
        {
            if (length == 0)
                throw new InvalidOperationException("Buffer must have non-zero size");

            mName = name;
            mbDirty = true;
            mStartAddress = startAddress;
            mLength = length;
            mPeriod = period;
            mBuffers = new byte[2][];
            mBuffers[0] = new byte[length];
            mBuffers[1] = new byte[length];
        }

        /// <summary>
        /// Parameterless ctor used by <see cref="ModelTypeBase.Fork"/>'s
        /// activator path. The newly-allocated instance is barren until
        /// <see cref="OnForked"/> copies the source's identity (name +
        /// addresses + period) and allocates fresh buffers.
        /// </summary>
        public MemorySegment()
        {
        }

        /// <summary>
        /// Per-state fork: produce a fresh segment pinned to
        /// <paramref name="destOwnerState"/> that shares the source's
        /// ImmutableData (DefinitionId) so the
        /// <see cref="LuaStateCloner"/> can remap source-segment references
        /// stored in pack-script Lua tables to the fork's counterpart by
        /// DefinitionId. Buffers + dirty flag are fresh — buffer state is
        /// rebuilt by the fork's own autotracker from its own connector.
        /// </summary>
        public override ModelTypeBase Fork(ITrackerStateContext destOwnerState)
        {
            if (destOwnerState == null) throw new ArgumentNullException(nameof(destOwnerState));
            var copy = (MemorySegment)System.Activator.CreateInstance(this.GetType());
            copy.OwnerState = destOwnerState;
            copy.InitializeAsForkOf(this);
            return copy;
        }

        /// <summary>
        /// Fork-time hook invoked on the freshly-allocated copy: copies
        /// over the source's identity fields (Name, StartAddress, Length,
        /// Period) and allocates fresh per-fork buffers.
        /// </summary>
        protected override void OnForked(ModelTypeBase source)
        {
            base.OnForked(source);
            if (!(source is MemorySegment src)) return;

            mName = src.mName;
            mStartAddress = src.mStartAddress;
            mLength = src.mLength;
            mPeriod = src.mPeriod;
            mbDirty = true;
            mbFrozen = false;
            mBuffers = new byte[2][];
            mBuffers[0] = new byte[mLength];
            mBuffers[1] = new byte[mLength];
        }

        public void Freeze()
        {
            Frozen = true;
        }

        public void Unfreeze()
        {
            Frozen = false;
        }

        public void MarkDirty()
        {
            Dirty = true;
        }

        [LuaHide]
        public bool ShouldUpdate(DateTime now)
        {
            lock (this)
            {
                if (Frozen)
                    return false;

                if (Dirty)
                    return true;

                if (mLastUpdate.ToBinary() != 0)
                {
                    if ((now - mLastUpdate).CompareTo(TimeSpan.FromMilliseconds(Period)) < 0)
                        return false;
                }

                return true;
            }
        }

        [LuaHide]
        public MemoryUpdateResult UpdateWithConnector(IAutoTrackingProvider provider, PackageManager.Game game)
        {
            if (Frozen)
                return MemoryUpdateResult.Success;

            try
            {
                bool bReadResult = false;
                {
                    int attempts = 0;
                    while (attempts < 5 && !bReadResult)
                    {
                        try
                        {
                            bReadResult = provider.Read(StartAddress, WriteBuffer);
                        }
                        catch { }

                        ++attempts;
                    }
                }

                if (bReadResult)
                {
                    bool bModified = Dirty;
                    for (ulong i = 0; !bModified && i < Length; ++i)
                    {
                        if (ReadBuffer[i] != WriteBuffer[i])
                        {
                            bModified = true;
                            break;
                        }
                    }

                    if (bModified)
                    {
                        lock (this)
                        {
                            Buffer.BlockCopy(WriteBuffer, 0, ReadBuffer, 0, (int)Length);

                            //  Invoke the segment modified handler
                            OnMemorySegmentModified?.Invoke(this, provider, game);

                            // Hand off the per-segment-update reaction to
                            // a virtual hook. Subclasses (notably
                            // LuaMemorySegment) override to dispatch into
                            // pack scripts on the UI thread.
                            OnSegmentDataUpdated();
                        }
                    }

                    //  Invoke the segment updated handler
                    OnMemorySegmentUpdated?.Invoke(this, provider, game);

                    // Stamp the throttling timer so ShouldUpdate skips
                    // re-reading until at least mPeriod ms have elapsed.
                    mLastUpdate = DateTime.Now;

                    return MemoryUpdateResult.Success;
                }
            }
            catch
            {
            }

            return MemoryUpdateResult.Error;
        }

        /// <summary>
        /// Hook invoked (under the segment's lock) whenever a fresh read
        /// from the connector produced different data than the previous
        /// snapshot. Default: no-op. <see cref="LuaMemorySegment"/>
        /// overrides to dispatch onto the UI thread and SafeCall the
        /// pack-supplied LuaFunction callback.
        /// </summary>
        protected virtual void OnSegmentDataUpdated()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}
