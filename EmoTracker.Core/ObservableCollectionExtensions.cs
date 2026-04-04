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
            List<T> sorted = observable.OrderBy(keyFunc).ToList();

            for (int ptr = 0; ptr < observable.Count; ++ptr)
            {
                if (!observable[ptr].Equals(sorted[ptr]))
                {
                    observable.Move(observable.IndexOf(sorted[ptr]), ptr);
                }
            }
        }

        public static void Sort<T>(this ObservableCollection<T> observable, IComparer<T> compare)
        {
            List<T> sorted = new List<T>(observable);
            sorted.Sort(compare);

            for (int ptr = 0; ptr < observable.Count; ++ptr)
            {
                if (!observable[ptr].Equals(sorted[ptr]))
                {
                    observable.Move(observable.IndexOf(sorted[ptr]), ptr);
                }
            }
        }
    }
}
