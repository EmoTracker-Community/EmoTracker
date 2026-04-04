using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Core
{
    public class Observable<T> : ObservableObject
    {
        private T _value = default(T);
        public T Value
        {
            get { return _value; }
            set { SetProperty(ref _value, value); }
        }

        public Observable()
        {
        }

        public Observable(T value)
        {
            Value = value;
        }

        public static implicit operator Observable<T>(T value)
        {
            return new Observable<T>(value);
        }

        public static implicit operator T(Observable<T> value)
        {
            return value.Value;
        }

        public override string ToString()
        {
            if (Value != null)
                return Value.ToString();

            return base.ToString();
        }
// 
//         public override bool Equals(object obj)
//         {
//             Observable<T> rhs = obj as Observable<T>;
//             if (rhs != null)
//             {
//                 return object.Equals(rhs.Value, Value);
//             }
// 
//             return base.Equals(obj);
//         }
// 
//         public override int GetHashCode()
//         {
//             if (Value != null)
//                 return Value.GetHashCode();
// 
//             return base.GetHashCode();
//         }
    }
}
