using EmoTracker.Data.Notes;
using EmoTracker.UI;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Extensions.NoteTaking
{
    public class NoteTakingExtension : Extension
    {
        public string Name { get { return "Note Taking"; } }

        public string UID { get { return "emotracker_note_taking"; } }

        public int Priority { get { return -300; } }

        public object StatusBarControl
        {
            get
            {
                // Note-taking extension UI is currently disabled
                return null;
                //return new NoteTakingIconPopup() { DataContext = NoteTakingSite };
            }
        }

        NoteTakingSite mNoteTakingSite = new NoteTakingSite();
        public NoteTakingSite NoteTakingSite
        {
            get { return mNoteTakingSite; }
        }

        public void OnPackageUnloaded()
        {
            NoteTakingSite.Clear();
        }

        public void OnPackageLoaded()
        {
            NoteTakingSite.Clear();
        }

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public JToken SerializeToJson()
        {
            return NoteTakingSite.AsJsonArray();
        }

        public bool DeserializeFromJson(JToken token)
        {
            return NoteTakingSite.PopulateWithJsonArray(token as JArray);
        }
    }
}
