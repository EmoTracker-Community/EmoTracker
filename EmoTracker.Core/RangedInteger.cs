using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmoTracker.Core
{
    public class RangedInteger : ObservableObject
    {
        int _maxValue = 100;
        int _minValue = 1;
        int _defaultValue;
        int _value;

        public delegate void ValueModifiedEventHandler(RangedInteger source);
        public event ValueModifiedEventHandler ValueModified;

        public int Min
        {
            get { return _minValue; }
        }

        public int Max
        {
            get { return _maxValue; }
        }

        public int Default
        {
            get { return _defaultValue; }
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int newValue = value;
                if (newValue < Min)
                    newValue = Min;
                if (newValue > Max)
                    newValue = Max;                

                if (SetProperty(ref _value, newValue))
                {
                    if (ValueModified != null)
                        ValueModified(this);
                }
            }
        }

        public RangedInteger(int value, int min, int max)
        {
            _minValue = min;
            _maxValue = max;

            Value = _defaultValue = value;
        }
    }
}
