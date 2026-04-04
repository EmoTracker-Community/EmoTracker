using System;
using System.Windows.Input;

namespace EmoTracker.Core
{
    public class DelegateCommand : ICommand
    {
        private readonly Predicate<object> mPredicate;
        private readonly Action<object> mAction;

        public event EventHandler CanExecuteChanged;

        public DelegateCommand(Action<object> action) :
            this(action, null)
        {
        }

        public DelegateCommand(Action<object> action, Predicate<object> predicate)
        {
            mAction = action;
            mPredicate = predicate;
        }

        public bool CanExecute(object parameter)
        {
            if (mPredicate == null)
            {
                return true;
            }

            return mPredicate(parameter);
        }

        public void Execute(object parameter)
        {
            mAction(parameter);
        }

        public void RaiseCanExecuteChanged()
        {
            if (CanExecuteChanged != null)
            {
                CanExecuteChanged(this, EventArgs.Empty);
            }
        }
    }
}
