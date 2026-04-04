using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Data.Scripting
{
    public interface IMemorySegment
    {
        string Name { get; }
        ulong StartAddress { get; }

        ulong Length { get; }

        byte ReadUInt8(ulong address, bool bRawRead = false);

        sbyte ReadInt8(ulong address, bool bRawRead = false);

        ushort ReadUInt16(ulong address, bool bRawRead = false);

        short ReadInt16(ulong address, bool bRawRead = false);

        void Freeze();
        void Unfreeze();
    }
}
