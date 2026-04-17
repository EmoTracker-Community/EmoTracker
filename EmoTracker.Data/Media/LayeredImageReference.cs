using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace EmoTracker.Data.Media
{
    public class LayeredImageReference : ImageReference
    {
        ObservableCollection<ImageReference> mLayers = new ObservableCollection<ImageReference>();

        public IList<ImageReference> Layers
        {
            get { return mLayers; }
        }

        public override bool Equals(object obj)
            => obj is LayeredImageReference other
               && mLayers.SequenceEqual(other.mLayers);

        public override int GetHashCode()
        {
            var hc = new System.HashCode();
            foreach (var layer in mLayers)
                hc.Add(layer);
            return hc.ToHashCode();
        }
    }
}
