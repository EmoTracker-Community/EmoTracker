using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmoTracker.Data.Notes;

namespace EmoTracker.Data
{
    public interface INoteTaking
    {
        IEnumerable<Note> Notes { get; }

        bool AddNote(Note note);

        bool RemoveNote(Note note);

        void Clear();
    }
}
