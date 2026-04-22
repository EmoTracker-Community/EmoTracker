using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Core
{
    public static class ObservableCollectionExtensions
    {
        public static void Sort<T, K>(this ObservableCollection<T> observable, Func<T, K> keyFunc)
        {
            var sorted = observable.OrderBy(keyFunc).ToList();

            for (int ptr = 0; ptr < sorted.Count; ++ptr)
            {
                var item = sorted[ptr];
                if (!object.ReferenceEquals(observable[ptr], item) && !observable[ptr].Equals(item))
                {
                    var currentIndex = observable.IndexOf(item);
                    if (currentIndex >= 0)
                    {
                        observable.Move(currentIndex, ptr);
                    }
                }
            }
        }

        public static void Sort<T>(this ObservableCollection<T> observable, IComparer<T> compare)
        {
            var sorted = new List<T>(observable);
            sorted.Sort(compare);

            for (int ptr = 0; ptr < sorted.Count; ++ptr)
            {
                var item = sorted[ptr];
                if (!object.ReferenceEquals(observable[ptr], item) && !observable[ptr].Equals(item))
                {
                    var currentIndex = observable.IndexOf(item);
                    if (currentIndex >= 0)
                    {
                        observable.Move(currentIndex, ptr);
                    }
                }
            }
        }
    }
}
