using EmoTracker.Core;
using EmoTracker.Data.JSON;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace EmoTracker.Data.Notes
{
    public class NoteTakingSite : ObservableObject, INoteTaking
    {
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
                                    string persistableItemRef = ItemDatabase.Instance.GetPersistableItemReference(item);
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
                                ITrackableItem item = ItemDatabase.Instance.ResolvePersistableItemReference(itemRef);
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
