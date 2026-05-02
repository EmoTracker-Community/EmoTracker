using EmoTracker.Core;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace EmoTracker.Data.Notes
{
    public class NoteTakingSite : ObservableObject, INoteTaking
    {
        // Back-reference to the Location that owns this site. Set by the
        // owning Location during construction; used to resolve item
        // references through the owning state's ItemDatabase without
        // consulting any ambient slot.
        internal EmoTracker.Core.DataModel.ModelTypeBase Owner { get; set; }

        // Direct OwnerState binding for sites that aren't owned by a
        // ModelTypeBase — e.g., the per-state NoteTakingExtension's
        // own site, which lives on a tracker-extension surface
        // rather than under a Location. Takes precedence over the
        // Owner-derived resolution when set.
        Sessions.TrackerState mOwnerStateOverride;

        public void SetOwnerState(Sessions.TrackerState state)
        {
            mOwnerStateOverride = state;
        }

        Sessions.TrackerState OwnerState
            => mOwnerStateOverride ?? (Owner?.OwnerState as Sessions.TrackerState);
        ObservableCollection<Note> mNotes = new ObservableCollection<Note>();

        public IEnumerable<Note> Notes
        {
            get { return mNotes; }
        }

        public bool Any
        {
            get { return mNotes.Count > 0; }
        }

        public bool Empty
        {
            get { return mNotes.Count == 0; }
        }

        public bool AddNote(Note note)
        {
            if (!mNotes.Contains(note))
            {
                note.PropertyChanged += Note_PropertyChanged;
                mNotes.Add(note);
                return true;
            }

            return false;
        }

        public bool RemoveNote(Note note)
        {
            if (mNotes.Contains(note))
            {
                note.PropertyChanged -= Note_PropertyChanged;
                mNotes.Remove(note);
                return true;
            }

            return false;
        }

        public void Clear()
        {
            mNotes.Clear();
        }

        public JArray AsJsonArray()
        {
            try
            {
                JArray notesDataArray = new JArray();
                {
                    foreach (Notes.Note rawNote in Notes)
                    {
                        Notes.MarkdownTextWithItemsNote note = rawNote as Notes.MarkdownTextWithItemsNote;
                        if (note != null)
                        {
                            JObject noteData = new JObject();

                            if (!string.IsNullOrWhiteSpace(note.MarkdownSource))
                                noteData["markdown_source"] = note.MarkdownSource;

                            JArray itemsArray = new JArray();
                            foreach (ITrackableItem item in note.Items)
                            {
                                try
                                {
                                    string persistableItemRef = OwnerState?.Items.GetPersistableItemReference(item);
                                    if (!string.IsNullOrWhiteSpace(persistableItemRef))
                                        itemsArray.Add(JToken.FromObject(persistableItemRef));
                                }
                                catch
                                {
                                }
                            }

                            if (itemsArray.Count > 0)
                                noteData["items"] = itemsArray;

                            notesDataArray.Add(noteData);
                        }
                    }
                }

                if (notesDataArray.Count > 0)
                    return notesDataArray;
            }
            catch
            {
            }

            return null;
        }

        public bool PopulateWithJsonArray(JArray array)
        {
            try
            {
                Clear();

                if (array != null && array.Count > 0)
                {
                    foreach (JObject noteData in array)
                    {
                        Notes.MarkdownTextWithItemsNote note = new Notes.MarkdownTextWithItemsNote();
                        note.MarkdownSource = noteData.GetValue<string>("markdown_source");

                        JArray itemsArray = noteData.GetValue<JArray>("items");
                        if (itemsArray != null)
                        {
                            foreach (string itemRef in itemsArray)
                            {
                                ITrackableItem item = OwnerState?.Items.ResolvePersistableItemReference(itemRef);
                                if (item != null)
                                    note.AddItem(item);
                            }
                        }

                        AddNote(note);
                    }
                }

                return true;
            }
            catch
            {
            }

            return false;
        }
 
        public NoteTakingSite()
        {
            mNotes.CollectionChanged += Notes_CollectionChanged;
        }

        private void Notes_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            NotifyPropertyChanged("Any");
            NotifyPropertyChanged("Empty");
        }

        private void Note_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
        }

        public override void Dispose()
        {
            DisposeCollection(mNotes);
        }
    }
}
