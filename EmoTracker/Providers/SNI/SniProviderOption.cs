using EmoTracker.Core;
using EmoTracker.Data.AutoTracking;
using System.Collections.Generic;

namespace EmoTracker.Providers.SNI
{
    public class SniProviderOption : ObservableObject, IProviderOption
    {
        string mKey;
        string mDisplayName;
        ProviderOptionKind mKind;
        object mValue;
        List<object> mAvailableValues;

        public SniProviderOption(string key, string displayName, ProviderOptionKind kind, object defaultValue, List<object> availableValues)
        {
            mKey = key;
            mDisplayName = displayName;
            mKind = kind;
            mValue = defaultValue;
            mAvailableValues = availableValues;
        }

        public string Key => mKey;
        public string DisplayName => mDisplayName;
        public ProviderOptionKind Kind => mKind;
        public IReadOnlyList<object> AvailableValues => mAvailableValues;

        public object Value
        {
            get => mValue;
            set { SetProperty(ref mValue, value); }
        }
    }
}
