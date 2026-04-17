using EmoTracker.Data.Notes;
using Newtonsoft.Json.Linq;

namespace EmoTracker.Extensions.NoteTaking
{
    public class NoteTakingExtension : Extension
    {
        public string Name { get { return "Note Taking"; } }

        public string UID { get { return "emotracker_note_taking"; } }

        public int Priority { get { return -300; } }

        private NoteTakingStatusBarIndicator mStatusBarControl;
        public object StatusBarControl
        {
            get
            {
                if (mStatusBarControl == null)
                    mStatusBarControl = new NoteTakingStatusBarIndicator { DataContext = NoteTakingSite };
                return mStatusBarControl;
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
