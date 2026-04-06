using System.Threading.Tasks;

namespace EmoTracker.Data.AutoTracking
{
    public interface IProviderOperation
    {
        string Key { get; }
        string DisplayName { get; }
        bool CanExecute { get; }
        Task ExecuteAsync();
    }
}
