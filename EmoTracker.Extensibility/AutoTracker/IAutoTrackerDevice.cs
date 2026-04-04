using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Extensibility.AutoTracker
{
    public interface IAutoTrackerDevice : INotifyPropertyChanged
    {
        /// <summary>
        /// A device name/description suitable for display in a UI
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Returns the current state of the device.
        /// </summary>
        DeviceState State { get; }

        /// <summary>Reads a number of bytes from the game system.</summary>
        /// <param name="address">The system bus address to begin reading from.</param>
        /// <param name="buffer">The buffer in which the bytes should be placed.</param>
        /// <returns>True if the read was successful, false otherwise.</returns>
        /// <remarks>This function will attempt read exactly enough bytes to fill the buffer.</remarks>
        bool Read(ulong address, byte[] buffer);

        /// <summary>Writes a number of bytes to the game system.</summary>
        /// <param name="address">The system bus address to begin writing from.</param>
        /// <param name="buffer">The buffer containing the bytes to be written.</param>
        /// <returns>True if the write was successful, false otherwise.</returns>
        /// <remarks>This function will attempt to write the full buffer.</remarks>
        bool Write(ulong address, byte[] buffer);
    }
}
