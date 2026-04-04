using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Core
{
    public class Singleton<T> where T : new()
    {
        #region --- Singleton ---
        private static T mInstance;

        public static T Instance
        {
            get
            {
                if (mInstance == null)
                    mInstance = new T();

                return mInstance;
            }
        }

        public static T CreateInstance()
        {
            return Instance;
        }

        #endregion
    }
}
