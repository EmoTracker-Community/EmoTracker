using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Core
{
    public class ObservableCollectionUniqueSetAggregrator<SrcType, DstType> : ObservableObject
    {
        protected ObservableCollection<DstType> mNonUniqueAggregate = new ObservableCollection<DstType>();
        protected ObservableCollection<DstType> mUniqueSet;

        protected ObservableCollectionAggregatorSynchronizer<SrcType, DstType> mSynchronizer;

        bool mEnableDispose = false;
        public bool EnableDispose
        {
        	get { return mEnableDispose; }
        	set
            {
                if (SetProperty(ref mEnableDispose, value))
                {
                    if (mSynchronizer != null)
                        mSynchronizer.EnableDispose = mEnableDispose;
                }
            }
        }

        public ObservableCollectionUniqueSetAggregrator(ObservableCollection<DstType> dest, Func<SrcType, DstType> itemConverter, params ObservableCollection<SrcType>[] sources)
        {
            //  Use the provided dest as our unique set
            mUniqueSet = dest;

            //  Initialize our synchronizer
            mSynchronizer = new ObservableCollectionAggregatorSynchronizer<SrcType, DstType>(mNonUniqueAggregate, itemConverter, sources) { EnableDispose = this.EnableDispose };
            mNonUniqueAggregate.CollectionChanged += mNonUniqueAggregate_CollectionChanged;

            UpdateUniqueSet();
        }

        void mNonUniqueAggregate_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UpdateUniqueSet();
        }

        public void AddSource(IReadOnlyCollection<SrcType> source)
        {
            mSynchronizer.AddSource(source);
        }

        public void RemoveSource(IReadOnlyCollection<SrcType> source)
        {
            mSynchronizer.RemoveSource(source);
        }

        protected void UpdateUniqueSet()
        {
            foreach (DstType item in mNonUniqueAggregate)
            {
                if (!mUniqueSet.Contains(item))
                    mUniqueSet.Add(item);
            }

            List<DstType> toRemove = new List<DstType>();
            foreach (DstType item in mUniqueSet)
            {
                if (!mNonUniqueAggregate.Contains(item))
                    toRemove.Add(item);
            }

            foreach (DstType item in toRemove)
            {
                mUniqueSet.Remove(item);
            }
        }

        public override void Dispose()
        {
            DisposeObjectAndDefault(ref mSynchronizer);
            DisposeCollection(mNonUniqueAggregate);
            DisposeCollection(mUniqueSet);

            base.Dispose();
        }
    }

    public class TrivialObservableCollectionUniqueSetAggregrator<T> : ObservableCollectionUniqueSetAggregrator<T, T>
        where T : class
    {
        public static T ItemConverter(T src)
        {
            return src;
        }

        public TrivialObservableCollectionUniqueSetAggregrator(ObservableCollection<T> dest, params ObservableCollection<T>[] sources) :
            base(dest, ItemConverter, sources)
        {
        }

        public override void Dispose()
        {
            DisposeObjectAndDefault(ref mSynchronizer);
        }
    }
}

