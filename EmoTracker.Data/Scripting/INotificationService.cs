using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Data.Scripting
{
    public enum NotificationType
    {
        Message,
        Celebration,
        Warning,
        Error
    }

    public interface INotificationService
    {
        void PushMarkdownNotification(NotificationType type, string markdown, int timeout = -1);
    }
}
