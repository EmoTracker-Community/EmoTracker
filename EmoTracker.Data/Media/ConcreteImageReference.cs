using EmoTracker.Core;
using System;

namespace EmoTracker.Data.Media
{
    public class ConcreteImageReference : ImageReference
    {
        Uri mURI;
        public Uri URI
        {
            get { return mURI; }
            set { SetProperty(ref mURI, value); }
        }

        string mFilter;
        public string Filter
        {
            get { return mFilter; }
            set { SetProperty(ref mFilter, value); }
        }

        public override bool Equals(object obj)
            => obj is ConcreteImageReference other
               && Equals(mURI, other.mURI)
               && mFilter == other.mFilter;

        public override int GetHashCode()
            => HashCode.Combine(mURI, mFilter);
    }
}
