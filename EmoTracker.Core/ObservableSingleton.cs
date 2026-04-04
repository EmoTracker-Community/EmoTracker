using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Core
{
    /// <summary>
    /// Add this attribute to ObservableSingleton types if they should not lazily
    /// create their own instance. This requires that external code explicitly
    /// sets the instance at some point.
    /// </summary>
    /// <example>
    /// [ExplicitCreation]
    /// class ServiceSingleton : ObservableSingleton<ServiceSingleton>
    /// {
    /// };
    /// 
    /// ServiceSingleton.Instance = new ServiceSingleton()
    /// </example>
    class ExplicitCreationAttribute : System.Attribute
    {
    }

    /// <summary>
    /// Provides a re-usable base class for Singleton-type objects, which also
    /// affords the (extremely common) ObservableObject base class features.
    /// </summary>
    /// <typeparam name="T">The class being wrapped by ObservableSingleton</typeparam>
    public abstract class ObservableSingleton<T> : ObservableObject where T : class, new()
    {
        private static readonly bool mbExplicit = Attribute.IsDefined(typeof(T), typeof(ExplicitCreationAttribute));
        private static T mInstance;

        public static T Instance
        {
            get
            {
                if (!mbExplicit)
                {
                    if (mInstance == null)
                        mInstance = new T();
                }

                return mInstance;
            }

            set
            {
                mInstance = value;
            }
        }

        public static T CreateInstance()
        {
            if (Instance == null)
                Instance = new T();

            return Instance;
        }
    }
}
