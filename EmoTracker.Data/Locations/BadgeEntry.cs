using EmoTracker.Core;
using EmoTracker.Data.Media;

namespace EmoTracker.Data.Locations
{
    public class BadgeEntry : ObservableObject
    {
        private string mKey;
        private ImageReference mImage;
        private double mOffsetX;
        private double mOffsetY;

        public BadgeEntry(string key, ImageReference image, double offsetX = 0, double offsetY = 0)
        {
            mKey = key;
            mImage = image;
            mOffsetX = offsetX;
            mOffsetY = offsetY;
        }

        public string Key
        {
            get { return mKey; }
        }

        public ImageReference Image
        {
            get { return mImage; }
            set { SetProperty(ref mImage, value); }
        }

        public double OffsetX
        {
            get { return mOffsetX; }
            set { SetProperty(ref mOffsetX, value); }
        }

        public double OffsetY
        {
            get { return mOffsetY; }
            set { SetProperty(ref mOffsetY, value); }
        }
    }
}
