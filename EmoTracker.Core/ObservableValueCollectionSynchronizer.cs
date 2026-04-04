using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Core
{
    public class ObservableValueCollectionSynchronizer<T> : IDisposable
    {
        private IReadOnlyCollection<Observable<T>> mSource;
        private INotifyCollectionChanged mSourceChangeTracking;
        private ObservableCollection<T> mDest;
        private Action<T> mItemDisposer;

        public bool EnableDispose
        {
            get;
            set;
        }

        public ObservableValueCollectionSynchronizer(IReadOnlyCollection<Observable<T>> source, ObservableCollection<T> dest, Action<T> itemDisposer = null)
        {
            mSource = source;
            mSourceChangeTracking = (INotifyCollectionChanged)mSource;
            mDest = dest;
            mItemDisposer = itemDisposer;
            EnableDispose = true;

            if (mSource != null)
            {
                InitializeCollection();
            }
        }

        protected void AddItem(Observable<T> srcItem)
        {
            srcItem.PropertyChanged += srcItem_PropertyChanged;
            mDest.Add(srcItem.Value);
        }

        void srcItem_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            int idx = 0;
            foreach (Observable<T> src in mSource)
            {
                if (object.ReferenceEquals(src, (Observable<T>)sender))
                {
                    mDest[idx] = src.Value;
                    break;
                }

                ++idx;
            }
        }

        protected void InsertItem(int index, Observable<T> srcItem)
        {
            srcItem.PropertyChanged += srcItem_PropertyChanged;
            mDest.Insert(index, srcItem.Value);
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
            foreach (T item in mDest)
            {
                DisposeCreatedItem(item);
            }

            mDest.Clear();
        }

        protected void MoveItem(int fromIndex, int toIndex)
        {
            mDest.Move(fromIndex, toIndex);
        }

        protected void DisposeCreatedItem(T item)
        {
            if (mItemDisposer != null)
                mItemDisposer(item);

            IDisposable d = item as IDisposable;
            if (d != null && EnableDispose)
                d.Dispose();
        }

        void InitializeCollection()
        {
            foreach (Observable<T> srcItem in mSource)
            {
                AddItem(srcItem);
            }

            mSourceChangeTracking.CollectionChanged += SourceCollectionChanged;
        }

        protected virtual void SourceCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                int idx = e.NewStartingIndex;
                foreach (Observable<T> srcItem in e.NewItems)
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
            else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                RemoveAllItems();
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        public void Dispose()
        {
            if (mSourceChangeTracking != null)
                mSourceChangeTracking.CollectionChanged -= SourceCollectionChanged;

            foreach (T d in mDest)
            {
                DisposeCreatedItem(d);
            }
            mDest.Clear();

            mSourceChangeTracking = null;
            mDest = null;
            mSource = null;
            mItemDisposer = null;
        }
    }
}
