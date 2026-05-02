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
    /// <summary>
    /// Synchronizes the contents of a destination ObservableCollection with the contents
    /// of a source ObservableCollection. A converter function is used to create destination
    /// type objects.
    /// 
    /// No field synchronization is done on the destination objects, so if such functionality
    /// is necessary, the destination object type will need to track source changes on its own.
    /// </summary>
    /// <typeparam name="SrcType"></typeparam>
    /// <typeparam name="DstType"></typeparam>
    public class ObservableCollectionSynchronizer<SrcType, DstType> : IDisposable
        where DstType : class
    {
        public static B GenericConstructorConverter<A, B>(A src) where B : class
        {
            try
            {
                return Activator.CreateInstance(typeof(B), src) as B;
            }
            catch
            {
                System.Diagnostics.Debug.Fail("Default Converter Function Not Available", string.Format("Type {0} is not constructable from type {1}", typeof(B), typeof(A)));
            }

            return null;
        }

        private IReadOnlyCollection<SrcType> mSource;
        private INotifyCollectionChanged mSourceChangeTracking;
        private ObservableCollection<DstType> mDest;
        private Func<SrcType, DstType> mItemConverter;
        private Action<DstType> mItemDisposer;
        private CancellationToken mCancellationToken;

        public bool EnableDispose
        {
            get;
            set;
        }

        public ObservableCollectionSynchronizer(IReadOnlyCollection<SrcType> source, ObservableCollection<DstType> dest, Func<SrcType, DstType> itemConverter = null, Action<DstType> itemDisposer = null, CancellationToken cancellationToken = default)
        {
            mSource = source;
            mSourceChangeTracking = mSource as INotifyCollectionChanged;
            mDest = dest;
            mItemConverter = itemConverter ?? GenericConstructorConverter<SrcType, DstType>;
            System.Diagnostics.Debug.Assert(mItemConverter != null);
            mItemDisposer = itemDisposer;
            EnableDispose = true;
            mCancellationToken = cancellationToken;

            if (mSource != null)
            {
                InitializeCollection();
            }
        }

        protected void AddItem(SrcType srcItem)
        {
            DstType dstItem = mItemConverter(srcItem);
            mDest.Add(dstItem);
        }

        protected void InsertItem(int index, SrcType srcItem)
        {
            DstType dstItem = mItemConverter(srcItem);
            mDest.Insert(index, dstItem);
        }

        protected void RemoveItems(int index, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                var item = mDest[index];
                mDest.RemoveAt(index);
                DisposeCreatedItem(item);
            }
        }

        protected void RemoveAllItems()
        {
            foreach (DstType item in mDest)
            {
                DisposeCreatedItem(item);
            }

            mDest.Clear();
        }

        protected void MoveItem(int fromIndex, int toIndex)
        {
            mDest.Move(fromIndex, toIndex);
        }

        protected void ReplaceItem(int index, SrcType newSrcItem)
        {
            DisposeCreatedItem(mDest[index]);
            mDest[index] = mItemConverter(newSrcItem);
        }

        protected void DisposeCreatedItem(DstType item)
        {
            if (mItemDisposer != null)
                mItemDisposer(item);

            IDisposable d = item as IDisposable;
            if (d != null && EnableDispose)
                d.Dispose();
        }

        void InitializeCollection()
        {
            foreach (SrcType srcItem in mSource)
            {
                AddItem(srcItem);
            }

            if (mSourceChangeTracking != null)
                mSourceChangeTracking.CollectionChanged += SourceCollectionChanged;
        }

        protected virtual void SourceCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (mSource == null || mDest == null || mItemConverter == null)
                return;

            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                int idx = e.NewStartingIndex;
                foreach (SrcType srcItem in e.NewItems)
                {
                    InsertItem(idx, srcItem);
                    ++idx;
                }
            }
            else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
            {
                RemoveItems(e.OldStartingIndex, e.OldItems.Count);
            }
            else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move)
            {
                MoveItem(e.OldStartingIndex, e.NewStartingIndex);
            }
            else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace)
            {
                int idx = e.NewStartingIndex;
                foreach (SrcType srcItem in e.NewItems)
                {
                    ReplaceItem(idx, srcItem);
                    ++idx;
                }
            }
            else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                RemoveAllItems();
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        public virtual void Dispose()
        {
            Cancel();

            if (mSourceChangeTracking != null)
                mSourceChangeTracking.CollectionChanged -= SourceCollectionChanged;

            foreach (DstType d in mDest)
            {
                DisposeCreatedItem(d);
            }
            mDest.Clear();

            mSourceChangeTracking = null;
            mDest = null;
            mSource = null;
            mItemConverter = null;
            mItemDisposer = null;
        }

        /// <summary>
        /// Cancels the synchronization by disposing all tracked items and unsubscribing.
        /// </summary>
        public virtual void Cancel()
        {
            if (mSourceChangeTracking != null)
            {
                mSourceChangeTracking.CollectionChanged -= SourceCollectionChanged;
                mSourceChangeTracking = null;
            }
        }
    }

    public class TrivialObservableCollectionSynchronizer<T> : ObservableCollectionSynchronizer<T, T>
        where T : class
    {
        public TrivialObservableCollectionSynchronizer(IReadOnlyCollection<T> source, ObservableCollection<T> dest, Action<T> itemDisposer = null, CancellationToken cancellationToken = default) :
            base(source, dest, PassThroughConverter, itemDisposer, cancellationToken)
        {
            //  Disable dispose, since we're not cloning objects
            EnableDispose = false;
        }

        static T PassThroughConverter(T srcValue)
        {
            return srcValue;
        }
    }

    public class ObservableCollectionSynchronizerMT<SrcType, DstType> : ObservableCollectionSynchronizer<SrcType, DstType>
        where DstType : class
    {
        public ObservableCollectionSynchronizerMT(IReadOnlyCollection<SrcType> source, ObservableCollection<DstType> dest, Func<SrcType, DstType> itemConverter, Action<DstType> itemDisposer = null, CancellationToken cancellationToken = default) :
            base(source, dest, itemConverter, itemDisposer, cancellationToken)
        {
        }

        public override void Cancel()
        {
            if (EnableThreadSafety)
            {
                if (Async)
                    Core.Services.Backends.DispatchService.Backend?.BeginInvoke(new Action(base.Cancel));
                else
                    Core.Services.Backends.DispatchService.Backend?.Invoke(new Action(base.Cancel));
            }
            else
            {
                base.Cancel();
            }
        }

        public override void Dispose()
        {
#if false
            if (EnableThreadSafety)
            {
                if (Async)
                    DispatchService.Instance.BeginInvoke(DispatchPriority.Normal, new Action(() => base.Dispose()));
                else
                    DispatchService.Instance.Invoke(DispatchPriority.Normal, new Action(() => base.Dispose()));
            }
            else
#endif
            {
                base.Dispose();
            }
        }

        bool mbEnableThreadSafety = true;
        public bool EnableThreadSafety
        {
            get { return mbEnableThreadSafety; }
            set { mbEnableThreadSafety = value; }
        }

        bool mbAsync = false;
        public bool Async
        {
            get { return mbAsync; }
            set { mbAsync = value; }
        }

        protected override void SourceCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
#if false
            if (EnableThreadSafety)
            {
                if (!Async)
                {
                    DispatchService.Instance.Invoke(DispatchPriority.Normal, new Action(() =>
                    {
                        base.SourceCollectionChanged(sender, e);
                    }));
                }
                else
                {
                    DispatchService.Instance.BeginInvoke(DispatchPriority.Normal, new Action(() =>
                    {
                        base.SourceCollectionChanged(sender, e);
                    }));
                }
            }
            else
#endif
            {
                base.SourceCollectionChanged(sender, e);
            }
        }
    }

    public class TrivialObservableCollectionSynchronizerMT<T> : ObservableCollectionSynchronizerMT<T, T>
        where T : class
    {
        public TrivialObservableCollectionSynchronizerMT(IReadOnlyCollection<T> source, ObservableCollection<T> dest, Action<T> itemDisposer = null, CancellationToken cancellationToken = default) :
            base(source, dest, PassThroughConverter, itemDisposer, cancellationToken)
        {
            //  Disable dispose, since we're not cloning objects
            EnableDispose = false;
        }

        static T PassThroughConverter(T srcValue)
        {
            return srcValue;
        }
    }

    public class BufferedObservableCollectionSynchronizerMT<SrcType, DstType> : ObservableCollectionSynchronizer<SrcType, DstType>
        where DstType : class
    {
        public BufferedObservableCollectionSynchronizerMT(IReadOnlyCollection<SrcType> source, ObservableCollection<DstType> dest, Func<SrcType, DstType> itemConverter, Action<DstType> itemDisposer = null, CancellationToken cancellationToken = default) :
            base(source, dest, itemConverter, itemDisposer, cancellationToken)
        {
        }

        public override void Cancel()
        {
            if (EnableThreadSafety)
            {
                mBuffer = null;
            }
            base.Cancel();
        }

        bool mbEnableThreadSafety = true;
        public bool EnableThreadSafety
        {
            get { return mbEnableThreadSafety; }
            set
            {
                if (value != mbEnableThreadSafety)
                {
                    mbEnableThreadSafety = value;
                    
                    if (!mbEnableThreadSafety)
                        Flush();
                }
            }
        }

        int mBufferSize = 100;
        public int BufferLength
        {
            get { return mBufferSize; }
            set { mBufferSize = value; }
        }

        List<KeyValuePair<object, System.Collections.Specialized.NotifyCollectionChangedEventArgs>> mBuffer;

        public void Flush()
        {
            if (mBuffer != null)
            {
                List<KeyValuePair<object, System.Collections.Specialized.NotifyCollectionChangedEventArgs>> localBuffer = mBuffer;
                mBuffer = null;

                foreach (var entry in localBuffer)
                {
                    base.SourceCollectionChanged(entry.Key, entry.Value);
                }
#if false
                if (!Async)
                {
                    DispatchService.Instance.Invoke(DispatchPriority, new Action(() =>
                    {
                        foreach (var entry in localBuffer)
                        {
                            base.SourceCollectionChanged(entry.Key, entry.Value);
                        }
                    }));
                }
                else
                {
                    DispatchService.Instance.BeginInvoke(DispatchPriority, new Action(() =>
                    {
                        foreach (var entry in localBuffer)
                        {
                            base.SourceCollectionChanged(entry.Key, entry.Value);
                        }
                    }));
                }
#endif
            }
        }

        protected override void SourceCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (EnableThreadSafety)
            {
                if (mBuffer == null)
                    mBuffer = new List<KeyValuePair<object, NotifyCollectionChangedEventArgs>>();

                mBuffer.Add(new KeyValuePair<object, NotifyCollectionChangedEventArgs>(sender, e));

                if (mBuffer.Count >= BufferLength)
                    Flush();
            }
            else
            {
                base.SourceCollectionChanged(sender, e);
            }
        }
    }

    public class TrivialBufferedObservableCollectionSynchronizerMT<T> : BufferedObservableCollectionSynchronizerMT<T, T>
        where T : class
    {
        public TrivialBufferedObservableCollectionSynchronizerMT(IReadOnlyCollection<T> source, ObservableCollection<T> dest, Action<T> itemDisposer = null, CancellationToken cancellationToken = default) :
            base(source, dest, PassThroughConverter, itemDisposer, cancellationToken)
        {
            //  Disable dispose, since we're not cloning objects
            EnableDispose = false;
        }

        static T PassThroughConverter(T srcValue)
        {
            return srcValue;
        }
    }
}
