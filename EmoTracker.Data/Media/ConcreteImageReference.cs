using EmoTracker.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    }
}
