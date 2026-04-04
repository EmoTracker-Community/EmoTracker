using EmoTracker.Core;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Media;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace EmoTracker.Data.Layout
{
    [JsonTypeTags("tabbed")]
    public class TabPanel : LayoutItem
    {
        public class Tab : ObservableObject
        {
            LayoutItem mContent;
            string mTitle;
            ImageReference mIcon;

            public LayoutItem Content
            {
                get { return mContent; }
                set { SetProperty(ref mContent, value); }
            }

            public string Title
            {
                get { return mTitle; }
                set { SetProperty(ref mTitle, value); }
            }

            public ImageReference Icon
            {
                get { return mIcon; }
                set { SetProperty(ref mIcon, value); }
            }

            public override void Dispose()
            {
                DisposeObjectAndDefault(ref mContent);
                DisposeObjectAndDefault(ref mIcon);

                base.Dispose();
            }
        }

        public override void Dispose()
        {
            DisposeCollection(mTabs);
            mTabs.Clear();

            base.Dispose();
        }

        ObservableCollection<Tab> mTabs = new ObservableCollection<Tab>();

        public IEnumerable<Tab> Tabs
        {
            get { return mTabs; }
        }

        Tab mCurrentTab;
        public Tab CurrentTab
        {
            get { return mCurrentTab; }
            set { SetProperty(ref mCurrentTab, value); }
        }

        protected override bool TryParseInternal(JObject data, IGamePackage package)
        {
            mTabs.Clear();

            JArray tabList = data.GetValue<JArray>("tabs");
            if (tabList != null)
            {
                foreach (JObject entry in tabList)
                {
                    LayoutItem layout = CreateLayoutItem(entry.GetValue<JObject>("content"), package);
                    if (layout != null)
                    {
                        mTabs.Add(new Tab()
                        {
                            Content = layout,
                            Title = entry.GetValue<string>("title"),
                            Icon = ImageReference.FromPackRelativePath(package, entry.GetValue<string>("icon"), entry.GetValue<string>("icon_image_spec"))
                        });
                    }
                }

                CurrentTab = mTabs.FirstOrDefault();
            }

            return true;
        }
    }
}
