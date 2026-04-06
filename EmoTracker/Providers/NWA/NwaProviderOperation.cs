using EmoTracker.Data.AutoTracking;
using System;
using System.Threading.Tasks;

namespace EmoTracker.Providers.NWA
{
    public class NwaProviderOperation : IProviderOperation
    {
        readonly string mKey;
        readonly string mDisplayName;
        readonly Func<bool> mCanExecute;
        readonly Func<Task> mExecute;

        public NwaProviderOperation(string key, string displayName, Func<bool> canExecute, Func<Task> execute)
        {
            mKey = key;
            mDisplayName = displayName;
            mCanExecute = canExecute;
            mExecute = execute;
        }

        public string Key => mKey;
        public string DisplayName => mDisplayName;
        public bool CanExecute => mCanExecute();
        public Task ExecuteAsync() => mExecute();
    }
}
