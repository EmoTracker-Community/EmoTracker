using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Notifications
{
    public class MarkdownNotification : Notification
    {
        public MarkdownNotification(int timeout = -1) :
            base(timeout)
        {
        }

        string mMarkdown;
        public string Markdown
        {
            get { return mMarkdown; }
            set { SetProperty(ref mMarkdown, value); }
        }
    }
}
