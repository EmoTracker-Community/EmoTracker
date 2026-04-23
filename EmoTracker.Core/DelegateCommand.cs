using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace EmoTracker.Core
{
    public abstract class DelegateCommandBase : ICommand
    {
        private readonly Predicate<object> m_predicate;
        private bool m_isExecuting;

        public bool IsExecuting { get { return m_isExecuting; } }

        public event EventHandler CanExecuteChanged;

        protected DelegateCommandBase(Predicate<object> predicate)
        {
            m_predicate = predicate;
        }

        public abstract void Execute(object parameter);

        public bool CanExecute(object parameter)
        {
            if (m_isExecuting)
                return false;

            if (m_predicate == null)
                return true;

            return m_predicate(parameter);
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        protected void SetExecuting(bool value)
        {
            m_isExecuting = value;
            RaiseCanExecuteChanged();
        }
    }

    public class DelegateCommand : DelegateCommandBase
    {
        private readonly Action<object> m_action;

        public DelegateCommand(Action<object> action)
            : this(action, null)
        {
        }

        public DelegateCommand(Action<object> action, Predicate<object> predicate)
            : base(predicate)
        {
            m_action = action;
        }

        public override void Execute(object parameter)
        {
            SetExecuting(true);
            try
            {
                m_action(parameter);
            }
            finally
            {
                SetExecuting(false);
            }
        }
    }

    /// <summary>
    /// A command that wraps async handlers with reentry protection.
    /// The handler receives a TaskCompletionSource so it can signal completion.
    /// </summary>
    public class AsyncDelegateCommand : DelegateCommandBase
    {
        private readonly Action<object, TaskCompletionSource<object>> m_action;

        public AsyncDelegateCommand(Action<object, TaskCompletionSource<object>> action)
            : this(action, null)
        {
        }

        public AsyncDelegateCommand(Action<object, TaskCompletionSource<object>> action, Predicate<object> predicate)
            : base(predicate)
        {
            m_action = action;
        }

        public override void Execute(object parameter)
        {
            SetExecuting(true);
            var tcs = new TaskCompletionSource<object>();
            try
            {
                m_action(parameter, tcs);
            }
            catch
            {
                SetExecuting(false);
                tcs.SetException(new Exception());
                throw;
            }

            var task = tcs.Task;
            if (task.IsCompleted)
            {
                SetExecuting(false);
            }
            else
            {
                task.GetAwaiter().OnCompleted(() =>
                {
                    SetExecuting(false);
                });
            }
        }
    }
}
