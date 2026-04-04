namespace EmoTracker.Data.Media
{
    public class FilterImageReference : ImageReference
    {
        ImageReference mReference;
        public ImageReference Reference
        {
            get { return mReference; }
            set { SetProperty(ref mReference, value); }
        }

        string mFilter;
        public string Filter
        {
            get { return mFilter; }
            set { SetProperty(ref mFilter, value); }
        }
    }
}
