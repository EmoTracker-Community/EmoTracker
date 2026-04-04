using EmoTracker.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Data.Media
{
    public class LayeredImageReference : ImageReference
    {
        ObservableCollection<ImageReference> mLayers = new ObservableCollection<ImageReference>();

        public IList<ImageReference> Layers
        {
            get { return mLayers; }
        }
    }
}
