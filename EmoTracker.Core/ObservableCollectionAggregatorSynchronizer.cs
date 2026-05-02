using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EmoTracker.Core
{
    public class ObservableCollectionAggregatorSynchronizer<SrcType, DstType> : IDisposable
    {
        class ListSegment
        {
            public IReadOnlyCollection<SrcType> Source;
            public INotifyCollectionChanged SourceChangeTracking;
            public int BaseIndex = 0;

            public ListSegment(IReadOnlyCollection<SrcType> source, int baseIdx)
            {
                Source = source;
                SourceChangeTracking = (INotifyCollectionChanged)Source;
                BaseIndex = baseIdx;
            }
        }

        private List<ListSegment> Segments = new List<ListSegment>();
        private ObservableCollection<DstType> mDest;
        private Func<SrcType, DstType> mItemConverter;
        private CancellationToken mCancellationToken;

        public bool EnableDispose
        {
            get;
            set;
        }

        public ObservableCollectionAggregatorSynchronizer(ObservableCollection<DstType> dest, Func<SrcType, DstType> itemConverter, params IReadOnlyCollection<SrcType>[] sources)
        {
            mDest = dest;
            mItemConverter = itemConverter;

            foreach (IReadOnlyCollection<SrcType> source in sources)
            {
                AddSource(source);
            }
        }

        public ObservableCollectionAggregatorSynchronizer(ObservableCollection<DstType> dest, Func<SrcType, DstType> itemConverter, CancellationToken cancellationToken, params IReadOnlyCollection<SrcType>[] sources)
        {
            mDest = dest;
            mItemConverter = itemConverter;
            mCancellationToken = cancellationToken;

            foreach (IReadOnlyCollection<SrcType> source in sources)
            {
                AddSource(source);
            }
        }

        public void AddSource(IReadOnlyCollection<SrcType> source)
        {
            if (source != null)
            {
                ListSegment newSegment = new ListSegment(source, mDest.Count);
                Segments.Add(newSegment);

                foreach (SrcType item in source)
                {
                    AddItem(source, item);
                }

                newSegment.SourceChangeTracking.CollectionChanged += SourceCollectionChanged;
            }
        }

        public void RemoveSource(IReadOnlyCollection<SrcType> source)
        {
            foreach (SrcType item in source)
            {
                RemoveItem(source, 0, item);
            }

            foreach (ListSegment segment in Segments)
            {
                if (segment.Source == source)
                {
                    segment.SourceChangeTracking.CollectionChanged -= SourceCollectionChanged;
                    Segments.Remove(segment);
                    break;
                }
            }
        }

        void OffsetSegmentBaseIndices(int startingSegmentIdx, int offset)
        {
            for (int i = startingSegmentIdx; i < Segments.Count; ++i)
            {
                Segments[i].BaseIndex += offset;
            }
        }

        void AddItem(IReadOnlyCollection<SrcType> source, SrcType srcItem)
        {
            for (int nSegIdx = 0; nSegIdx < Segments.Count; ++nSegIdx)
            {
                ListSegment segment = Segments[nSegIdx];
                if (segment.Source == source)
                {
                    DstType dstItem = mItemConverter(srcItem);

                    if (nSegIdx < Segments.Count - 1)
                    {
                        //  We're an intermediate segment; grab the next segment, insert
                        //  and update the following segments
                        ListSegment nextSegment = Segments[nSegIdx + 1];
                        mDest.Insert(nextSegment.BaseIndex, dstItem);

                        OffsetSegmentBaseIndices(nSegIdx + 1, 1);
                    }
                    else
                    {
                        //  We're the last segment; just append to the list
                        mDest.Add(dstItem);
                    }
                }
            }
        }

        void InsertItem(IReadOnlyCollection<SrcType> source, int index, SrcType srcItem)
        {
            for (int nSegIdx = 0; nSegIdx < Segments.Count; ++nSegIdx)
            {
                ListSegment segment = Segments[nSegIdx];
                if (segment.Source == source)
                {
                    DstType dstItem = mItemConverter(srcItem);
                    
                    int dstIdx = segment.BaseIndex + index;
                    if (dstIdx < mDest.Count)
                    {
                        mDest.Insert(dstIdx, dstItem);
                    }
                    else
                    {
                        System.Diagnostics.Debug.Assert(dstIdx == mDest.Count);
                        mDest.Add(dstItem);
                    }

                    OffsetSegmentBaseIndices(nSegIdx + 1, 1);
                }
            }
        }

        protected void DisposeCreatedItem(DstType item)
        {
            IDisposable d = item as IDisposable;
            if (d != null && EnableDispose)
                d.Dispose();
        }

        void RemoveItem(IReadOnlyCollection<SrcType> source, int idx, SrcType item)
        {
            for (int nSegIdx = 0; nSegIdx < Segments.Count; ++nSegIdx)
            {
                ListSegment segment = Segments[nSegIdx];
                if (segment.Source == source)
                {
                    DstType dstItem = mDest.ElementAt(segment.BaseIndex + idx);
                    mDest.RemoveAt(segment.BaseIndex + idx);
                    OffsetSegmentBaseIndices(nSegIdx + 1, -1);

                    DisposeCreatedItem(dstItem);
                }
            }
        }

        void MoveItem(IReadOnlyCollection<SrcType> source, int fromIdx, int toIdx)
        {
            for (int nSegIdx = 0; nSegIdx < Segments.Count; ++nSegIdx)
            {
                ListSegment segment = Segments[nSegIdx];
                if (segment.Source == source)
                {
                    mDest.Move(fromIdx + segment.BaseIndex, toIdx + segment.BaseIndex);
                }
            }
        }

        void Clear(IReadOnlyCollection<SrcType> source)
        {
            for (int nSegIdx = 0; nSegIdx < Segments.Count; ++nSegIdx)
            {
                ListSegment segment = Segments[nSegIdx];
                if (segment.Source == source)
                {
                    if (nSegIdx < Segments.Count - 1)
                    {
                        //  We're an intermediate segment; grab the next segment, insert
                        //  and update the following segments
                        ListSegment nextSegment = Segments[nSegIdx + 1];

                        while (nextSegment.BaseIndex > segment.BaseIndex)
                        {
                            mDest.RemoveAt(segment.BaseIndex);
                            OffsetSegmentBaseIndices(nSegIdx + 1, -1);
                        }
                    }
                    else
                    {
                        //  We're the last segment; just delete the rest of the list
                        while (mDest.Count != segment.BaseIndex)
                        {
                            mDest.RemoveAt(mDest.Count - 1);
                        }
                    }
                }
            }
        }

        void SourceCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            IReadOnlyCollection<SrcType> source = sender as IReadOnlyCollection<SrcType>;

            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                int idx = e.NewStartingIndex;
                foreach (SrcType srcItem in e.NewItems)
                {
                    InsertItem(source, idx, srcItem);
                    ++idx;
                }
            }
            else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
            {
                int idx = e.OldStartingIndex;
                foreach (SrcType item in e.OldItems)
                {
                    RemoveItem(source, idx, item);
                    ++idx;
                }
            }
            else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move)
            {
                MoveItem(source, e.OldStartingIndex, e.NewStartingIndex);
            }
            else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                Clear(source);
            }
        }

        public void Dispose()
        {
            foreach (var segment in Segments.ToArray())
            {
                RemoveSource(segment.Source);
            }
        }

        /// <summary>
        /// Cancels the synchronization by unsubscribing all source change handlers.
        /// </summary>
        public void Cancel()
        {
            foreach (var segment in Segments.ToArray())
            {
                RemoveSource(segment.Source);
            }
        }
    }

    public class TrivialObservableCollectionAggregatorSynchronizer<T> : ObservableCollectionAggregatorSynchronizer<T, T>
    {
        public static T ItemConverter(T src)
        {
            return src;
        }

        public TrivialObservableCollectionAggregatorSynchronizer(ObservableCollection<T> dest, params IReadOnlyCollection<T>[] sources) :
            base(dest, ItemConverter, sources)
        {
            EnableDispose = false;
        }

        public TrivialObservableCollectionAggregatorSynchronizer(ObservableCollection<T> dest, CancellationToken cancellationToken, params IReadOnlyCollection<T>[] sources) :
            base(dest, ItemConverter, cancellationToken, sources)
        {
            EnableDispose = false;
        }
    }
}
