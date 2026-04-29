using EmoTracker.Data.Media;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace EmoTracker.Data
{
    public interface ITrackableItem : INotifyPropertyChanged, IDisposable
    {
        string Name { get; }

        ImageReference Icon { get; }
        ImageReference PotentialIcon { get; }

        string BadgeText { get; }

        bool Capturable { get; }

        bool IgnoreUserInput { get; }

        void OnLeftClick();
        void OnRightClick();

        uint ProvidesCode(string code);
        bool CanProvideCode(string code);
        void AdvanceToCode(string code = null);

        /// <summary>
        /// Returns the complete set of codes this item could ever provide
        /// across all of its possible states (e.g. every stage of a
        /// progressive item, every branch of a Lua-dispatched provider).
        /// Used by <see cref="ItemDatabase"/> to build a static
        /// code → providers index that avoids iterating dynamic-code
        /// items on every accessibility-rule lookup.
        ///
        /// <para>
        /// Implementations return:
        /// <list type="bullet">
        ///   <item>A complete enumerable of codes (possibly empty) when
        ///         the set is statically determinable.</item>
        ///   <item><c>null</c> when the set is unknown or dynamic — for
        ///         example a <see cref="Scripting.LuaItem"/> whose pack
        ///         did not register a code-listing callback. The item
        ///         database falls back to brute-force
        ///         <see cref="ProvidesCode"/> iteration for every
        ///         lookup against items that return null.</item>
        /// </list>
        /// </para>
        /// </summary>
        IEnumerable<string> GetAllProvidedCodes();

        bool Save(JObject itemData);
        bool Load(JObject itemData);
    }
}
