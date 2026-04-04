using EmoTracker.Core;

namespace EmoTracker.Data.Notes
{
    public class Note : ObservableObject
    {
        public virtual bool ReadOnly
        {
            get { return false; }
        }
    }
}
