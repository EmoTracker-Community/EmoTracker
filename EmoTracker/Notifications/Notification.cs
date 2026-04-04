using EmoTracker.Core;
using EmoTracker.Data.Scripting;
using System;

namespace EmoTracker.Notifications
{
    public abstract class Notification : ObservableObject
    {
        NotificationType mType = NotificationType.Message;
        public NotificationType Type
        {
            get { return mType; }
            set { SetProperty(ref mType, value); }
        }

        DateTime mExpirationTime;
        public DateTime ExpirationTime
        {
            get { return mExpirationTime; }
            set { SetProperty(ref mExpirationTime, value); }
        }

        bool mbExpired = false;
        public bool Expired
        {
            get { return mbExpired; }
            set { SetProperty(ref mbExpired, value); }
        }

        DelegateCommand mForceExpireCommand;
        public DelegateCommand ForceExpireCommand
        {
            get { return mForceExpireCommand; }
        }

        protected Notification(int timeout)
        {
            if (timeout < 0)
                ExpirationTime = DateTime.Now.AddSeconds(10);
            else if (timeout > 0)
                ExpirationTime = DateTime.Now.AddMilliseconds(timeout);
            else
                ExpirationTime = DateTime.MaxValue;

            mForceExpireCommand = new DelegateCommand(ForceExpireNotification);
        }
        private void ForceExpireNotification(object obj)
        {
            Expired = true;
        }
    }
}
