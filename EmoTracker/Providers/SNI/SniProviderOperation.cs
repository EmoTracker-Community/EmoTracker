using EmoTracker.Data.AutoTracking;
using System;
using System.Threading.Tasks;

namespace EmoTracker.Providers.SNI
{
    public class SniProviderOperation : IProviderOperation
    {
        readonly string mKey;
        readonly string mDisplayName;
        readonly Func<bool> mCanExecute;
        readonly Func<Task> mExecute;

        public SniProviderOperation(string key, string displayName, Func<bool> canExecute, Func<Task> execute)
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
