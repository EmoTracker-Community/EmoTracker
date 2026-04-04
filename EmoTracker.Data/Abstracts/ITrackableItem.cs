using EmoTracker.Data.Media;
using Newtonsoft.Json.Linq;
using System;
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

        bool Save(JObject itemData);
        bool Load(JObject itemData);
    }
}
