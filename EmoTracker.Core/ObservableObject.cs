using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace EmoTracker.Core
{
    /// <summary>
    /// Extends an object with support for property change notifications. This is essential for properties
    /// which may be data-bound to UI elements. It is essential that both field-based and computed
    /// properties implement change notifications properly.
    /// </summary>
    /// <example>
    /// object _field;
    /// [DependentProperty("ComputedProperty)]  // Causes updates to this property to automatically notify
    ///                                         //   a change to ComputedProperty as well
    /// public object FieldBasedProperty
    /// {
    ///     get { return _foo; }
    ///     set
    ///     {
    ///         SetProperty(ref _foo, value);
    ///
    ///         // Need to notify that ComputedProperty2 changed as well, since it's based
    ///         // on this property and was listed via a dependent property attribute
    ///         NotifyPropertyChanged("ComputedProperty2");
    ///     }
    /// }
    /// // Causes update notifications sent for this property to automatically notify
    /// // for ComputedProperty3 as well
    /// [DependentProperty("ComputedProperty3)]  
    /// public object ComputedProperty
    /// {
    ///     get { return FieldBasedProperty;
    /// }
    /// 
    /// public object ComputedProperty2
    /// {
    ///     get { return FieldBasedProperty;
    /// }
    /// 
    /// public object ComputedProperty3
    /// {
    ///     get { return ComputedProperty;
    /// }
    /// </example>
    public abstract class ObservableObject : INotifyPropertyChanged, INotifyPropertyChanging, IDisposable
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public event PropertyChangingEventHandler PropertyChanging;

        /// <summary>
        /// Generates property change notifications for all dependent properties of the
        /// specified property, recursively (depth first).
        /// </summary>
        /// <param name="propInfo"></param>
        void NotifyDependentProperties(PropertyChangedEventHandler handler, System.Type hostType, string propertyName)
        {
            PropertyInfo propInfo = hostType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (propInfo != null)
            {
                foreach (Attribute a in Attribute.GetCustomAttributes(propInfo, typeof(DependentPropertyAttribute)))
                {
                    handler(this, new PropertyChangedEventArgs((a as DependentPropertyAttribute).Property));
                    NotifyDependentProperties(handler, hostType, (a as DependentPropertyAttribute).Property);
                }
            }
        }

        /// <summary>
        /// Generates property change notifications for all dependent properties of the
        /// specified property, recursively (depth first).
        /// </summary>
        /// <param name="propInfo"></param>
        void NotifyDependentProperties(PropertyChangingEventHandler handler, System.Type hostType, string propertyName)
        {
            PropertyInfo propInfo = hostType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (propInfo != null)
            {
                foreach (Attribute a in Attribute.GetCustomAttributes(propInfo, typeof(DependentPropertyAttribute)))
                {
                    handler(this, new PropertyChangingEventArgs((a as DependentPropertyAttribute).Property));
                    NotifyDependentProperties(handler, hostType, (a as DependentPropertyAttribute).Property);
                }
            }
        }

        protected virtual void NotifyPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var pc = PropertyChanged;
            if (pc != null)
            {
                pc(this, new PropertyChangedEventArgs(propertyName));

                //  Notify for dependent properties as well
                NotifyDependentProperties(pc, this.GetType(), propertyName);
            }
        }

        protected virtual void NotifyPropertyChanging([CallerMemberName] string propertyName = null)
        {
            var pc = PropertyChanging;
            if (pc != null)
            {
                pc(this, new PropertyChangingEventArgs(propertyName));

                //  Notify for dependent properties as well
                NotifyDependentProperties(pc, this.GetType(), propertyName);
            }
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                NotifyPropertyChanging(propertyName);
                field = value;
                NotifyPropertyChanged(propertyName);

                return true;
            }

            return false;
        }

        protected void ForceSetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            NotifyPropertyChanging(propertyName);
            field = value;
            NotifyPropertyChanged(propertyName);
        }

        public void ForceRefreshProperty(string propertyName = null)
        {
            NotifyPropertyChanged(propertyName);
        }

        public virtual void Dispose()
        {
        }

        protected void DisposeObject(object obj)
        {
            IDisposable disposable = obj as IDisposable;
            if (disposable != null)
                disposable.Dispose();
        }

        protected void DisposeObjectAndDefault<T>(ref T obj)
        {
            try
            {
                IDisposable disposable = obj as IDisposable;
                if (disposable != null)
                    disposable.Dispose();
            }
            catch
            {
            }
            finally
            {
                obj = default(T);
            }
        }

        protected void DisposeCollection<T>(IEnumerable<T> collection)
        {
            foreach (T item in collection)
            {
                IDisposable d = item as IDisposable;
                if (d != null)
                    d.Dispose();
            }
        }
    }
}
