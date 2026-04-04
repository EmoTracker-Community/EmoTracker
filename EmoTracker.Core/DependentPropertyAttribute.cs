using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Core
{
    /// <summary>
    /// Instructs ObservableObject to propagate change notifications for this property
    /// to the specified dependent property. This is useful when implementing purely
    /// computed properties on an ObservableObject.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
    public class DependentPropertyAttribute : Attribute
    {
        public string Property { get; private set; }

        public DependentPropertyAttribute(string property)
        {
            Property = property;
        }
    }
}
