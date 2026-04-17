using EmoTracker.Core.Services;
using EmoTracker.Data;
using EmoTracker.Data.AutoTracking;
using EmoTracker.Data.Packages;
using EmoTracker.Data.Scripting;
using NLua;
using System;

namespace EmoTracker.Extensions.AutoTracker
{
    public class MemorySegment : IMemorySegment, IUpdateWithConnector, IDisposable
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

        Func<IMemorySegment, bool> mCallback;
        Action<IMemorySegment> mDisposeCallback;
        string mName;

        DateTime mLastUpdate;
        int mPeriod = 500;
        ulong mStartAddress;
        ulong mLength;
        bool mbDirty;
        bool mbFrozen;
        byte[][] mBuffers;

        public string Name
        {
            get { return mName; }
        }

        public int Period
        {
            get { return mPeriod; }
        }

        public ulong StartAddress
        {
            get { return mStartAddress; }
        }

        public ulong EndAddress
        {
            get { return mStartAddress + (Length - 1); }
        }

        public ulong Length
        {
            get { return mLength; }
        }

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

        private byte[] ReadBuffer
        {
            get { return mBuffers[0]; }
        }

        private byte[] WriteBuffer
        {
            get { return mBuffers[1]; }
        }

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

        private ulong GetOffsetForAddress(ulong address)
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
                    ScriptManager.Instance.OutputError("Address 0x{0:x} is out of range of segment '{3}' = [0x{1:x}:0x{2:x}]", address, StartAddress, StartAddress + Length, Name);
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
                    ulong offset = GetOffsetForAddress(address);

                    byte b0 = ReadUInt8(address);
                    byte b1 = ReadUInt8(address + 1);

                    ushort value = (ushort)((uint)b1 << 8 | b0);
                    return value;
                }
                catch
                {
                    ScriptManager.Instance.OutputError("Address 0x{0:x} is out of range of segment '{3}' = [0x{1:x}:0x{2:x}]", address, StartAddress, StartAddress + Length, Name);
                }
            }

            return 0;
        }

        public short ReadInt16(ulong address, bool bRawRead = false)
        {
            return 0;
        }

        public MemorySegment(string name, ulong startAddress, ulong length, Func<IMemorySegment, bool> callback, Action<IMemorySegment> disposeCallback, int period = 500)
        {
            if (length == 0)
                throw new InvalidOperationException("Buffer must have non-zero size");

            mName = name;
            mbDirty = true;
            mCallback = callback;
            mDisposeCallback = disposeCallback;
            mStartAddress = startAddress;
            mLength = length;
            mPeriod = period;
            mBuffers = new byte[2][];
            mBuffers[0] = new byte[length];
            mBuffers[1] = new byte[length];
        }

        public void Freeze()
        {
            Frozen = true;
        }

        public void Unfreeze()
        {
            Frozen = false;
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

                            Dispatch.BeginInvoke(() =>
                            {
                                lock (this)
                                {
                                    bool bResult = true;
                                    if (mCallback != null)
                                    {
                                        bResult = mCallback(this);
                                    }

                                    if (bResult)
                                        Dirty = false;

                                    DateTime now = DateTime.Now;
                                    mLastUpdate = now;
                                }
                            });
                        }
                    }

                    //  Invoke the segment updated handler
                    OnMemorySegmentUpdated?.Invoke(this, provider, game);

                    return MemoryUpdateResult.Success;
                }
            }
            catch
            {
            }

            return MemoryUpdateResult.Error;
        }

        public void Dispose()
        {
            lock (this)
            {
                if (mDisposeCallback != null)
                    mDisposeCallback(this);
            }
        }

        public void MarkDirty()
        {
            Dirty = true;
        }
    }
}
