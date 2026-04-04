using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Collections.Specialized;

namespace EmoTracker.Core
{
    public class MultiSelectionSet<T> : ObservableObject
        where T : class
    {
        public delegate void SelectionSetModifiedEvent(MultiSelectionSet<T> sender, EventArgs e);
        public event SelectionSetModifiedEvent SelectionSetModified;

        internal void NotifySelectionSetModified()
        {
            if (AreAllSelected)
            {
                mAllEntriesCheckedValue = true;
            }
            else if (AreAnySelected)
            {
                mAllEntriesCheckedValue = null;
            }
            else
            {
                mAllEntriesCheckedValue = false;
            }
            NotifyPropertyChanged("AllEntriesCheckedValue");

            NotifyPropertyChanged("DisplayText");
            NotifyPropertyChanged("AreAnySelected");
            NotifyPropertyChanged("AreAllSelected");

            if (SelectionSetModified != null)
                SelectionSetModified(this, EventArgs.Empty);
        }

        #region -- Properties --

        IReadOnlyCollection<T> mSource;
        public IReadOnlyCollection<T> Source
        {
            get { return mSource; }
            set
            {
                if (SetProperty(ref mSource, value))
                {
                    DisposeObjectAndDefault(ref mEntrySynchronizer);
                    DisposeCollection(mEntries);
                    mEntries.Clear();

                    mEntrySynchronizer = new ObservableCollectionSynchronizer<T, Entry>(mSource, mEntries, EntryConverterFunc);

                    NotifyPropertyChanged("DisplayText");
                    NotifySelectionSetModified();
                }
            }
        }

        ObservableCollection<T> mSelected = new ObservableCollection<T>();
        public ObservableCollection<T> Selected
        {
        	get { return mSelected; }
        	protected set { SetProperty(ref mSelected, value); }
        }

        ObservableCollection<T> mNotSelected = new ObservableCollection<T>();
        public ObservableCollection<T> NotSelected
        {
            get { return mNotSelected; }
            protected set { SetProperty(ref mNotSelected, value); }
        }

        private Entry EntryConverterFunc(T arg)
        {
            return new Entry(this, arg);
        }

        public class Entry : ObservableObject
        {
            MultiSelectionSet<T> mOwner;

            T mValue;
            public T Value
            {
                get { return mValue; }
                private set
                {
                    if (mValue != null)
                    {
                        INotifyPropertyChanged npc = mValue as INotifyPropertyChanged;
                        if (npc != null)
                        {
                            npc.PropertyChanged -= npc_PropertyChanged;
                        }
                    }

                    SetProperty(ref mValue, value);
                    mOwner.NotifyPropertyChanged("DisplayText");

                    if (mValue != null)
                    {
                        INotifyPropertyChanged npc = mValue as INotifyPropertyChanged;
                        if (npc != null)
                        {
                            npc.PropertyChanged += npc_PropertyChanged;
                        }
                    }
                }
            }

            public override void Dispose()
            {
                INotifyPropertyChanged npc = mValue as INotifyPropertyChanged;
                if (npc != null)
                {
                    npc.PropertyChanged -= npc_PropertyChanged;
                }

                base.Dispose();
            }

            void npc_PropertyChanged(object sender, PropertyChangedEventArgs e)
            {
                NotifyPropertyChanged("DisplayText");
                mOwner.NotifyPropertyChanged("DisplayText");
            }

            bool mSelected = false;
            public bool Selected
            {
                get { return mSelected; }
                set
                {
                    if (SetProperty(ref mSelected, value))
                    {
                        if (mSelected)
                        {
                            mOwner.NotSelected.Remove(Value);
                            mOwner.Selected.Add(Value);
                        }
                        else
                        {
                            mOwner.NotSelected.Add(Value);
                            mOwner.Selected.Remove(Value);
                        }

                        mOwner.NotifyPropertyChanged("DisplayText");
                        mOwner.NotifySelectionSetModified();
                    }
                }
            }

            public string DisplayText
            {
                get { return Value.ToString(); }
            }

            public Entry(MultiSelectionSet<T> owner, T value)
            {
                mOwner = owner;
                Value = value;
            }
        }

        ObservableCollectionSynchronizer<T, Entry> mEntrySynchronizer;
        ObservableCollection<Entry> mEntries = new ObservableCollection<Entry>();
        public ObservableCollection<Entry> Entries
        {
            get { return mEntries; }
            protected set { SetProperty(ref mEntries, value); }
        }

        public string DisplayText
        {
            get
            {
                string result = null;

                bool bFirst = true;
                foreach (Entry entry in Entries)
                {
                    if (entry.Selected)
                    {
                        if (bFirst)
                            result = entry.DisplayText;
                        else
                            result = string.Format("{0}, {1}", result, entry.DisplayText);

                        bFirst = false;
                    }
                }

                return result;
            }
        }

        #endregion

        public bool IsSelected(T value)
        {
            foreach (Entry entry in Entries)
            {
                if (object.Equals(entry.Value, value))
                    return entry.Selected;
            }

            return false;
        }

        public void Select(T value)
        {
            foreach (Entry entry in Entries)
            {
                if (object.Equals(entry.Value, value))
                {
                    entry.Selected = true;
                    break;
                }
            }
        }

        public void SelectAs(string value)
        {
            foreach (Entry entry in Entries)
            {
                string entryAs = entry.Value.ToString();

                if (string.Equals(entryAs, value))
                {
                    entry.Selected = true;
                    break;
                }
            }
        }

        public void Deselect(T value)
        {
            foreach (Entry entry in Entries)
            {
                if (object.Equals(entry.Value, value))
                {
                    entry.Selected = false;
                    break;
                }
            }
        }

        public void ClearSelection()
        {
            foreach (Entry entry in Entries)
            {
                entry.Selected = false;
            }
        }

        public void SelectAll()
        {
            foreach (Entry entry in Entries)
            {
                entry.Selected = true;
            }
        }

        public bool AreAnySelected
        {
            get
            {
                foreach (Entry entry in Entries)
                {
                    if (entry.Selected)
                        return true;
                }

                return false;
            }
        }

        public bool AreAllSelected
        {
            get
            {
                foreach (Entry entry in Entries)
                {
                    if (!entry.Selected)
                        return false;
                }

                return true;
            }
        }

        string mAllEntriesCheckedText;
        public string AllEntriesCheckedText
        {
            get { return mAllEntriesCheckedText; }
            set { SetProperty(ref mAllEntriesCheckedText, value); }
        }

        object mAllEntriesCheckedValue;
        public object AllEntriesCheckedValue
        {
            get { return mAllEntriesCheckedValue; }
            set
            {
                if (SetProperty(ref mAllEntriesCheckedValue, value))
                {
                    if (mAllEntriesCheckedValue != null)
                    {
                        if ((bool)mAllEntriesCheckedValue)
                            SelectAll();
                        else
                            ClearSelection();
                    }
                }
                else
                {
                    AllEntriesCheckedValue = false;
                }
            }
        }

        public MultiSelectionSet(IReadOnlyCollection<T> source = null)
        {
            Source = source;
            mEntries.CollectionChanged += mEntries_CollectionChanged;
        }

        public override void Dispose()
        {
            DisposeObjectAndDefault(ref mEntrySynchronizer);

            mEntries.CollectionChanged -= mEntries_CollectionChanged;
            DisposeCollection(mEntries);
            mEntries.Clear();

            base.Dispose();
        }

        void mEntries_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            foreach (var selectedEntry in Selected.ToArray())
            {
                if (Entries.Any(entry => object.ReferenceEquals(entry.Value, selectedEntry)) == false)
                    Selected.Remove(selectedEntry);
            }

            foreach (var notSelectedEntry in NotSelected.ToArray())
            {
                if (Entries.Any(entry => object.ReferenceEquals(entry.Value, notSelectedEntry)) == false)
                    NotSelected.Remove(notSelectedEntry);
            }

            Selected.Sort(OrderToMatchSource);
            NotSelected.Sort(OrderToMatchSource);

            NotifySelectionSetModified();
        }

        private int OrderToMatchSource(T arg)
        {
            int count = Entries.Count;
            for (int i = 0; i < count; ++i)
            {
                if (Entries[i].Value == arg)
                    return i;
            }

            return count;
        }
    }
}
