using System.Collections.Generic;
using System.ComponentModel;

namespace EmoTracker.Data.AutoTracking
{
    public enum ProviderOptionKind
    {
        Dropdown,
        Toggle
    }

    public interface IProviderOption : INotifyPropertyChanged
    {
        string Key { get; }
        string DisplayName { get; }
        ProviderOptionKind Kind { get; }
        object Value { get; set; }
        IReadOnlyList<object> AvailableValues { get; }
    }
}
