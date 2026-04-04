using System;

namespace EmoTracker.Data.Media
{
    /// <summary>
    /// Used to represent a margin or padding around a rectangle
    /// </summary>
    public struct Thickness : IEquatable<Thickness>
    {
        public Thickness(double uniformLength)
        {
            Left = Top = Right = Bottom = uniformLength;
        }

        public Thickness(double left, double top, double right, double bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public double Left { get; set; }
        public double Top { get; set; }
        public double Right { get; set; }
        public double Bottom { get; set; }

        public override bool Equals(object obj)
        {
            return this == (Thickness)obj;
        }

        public bool Equals(Thickness thickness)
        {
            return this == thickness;
        }

        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }

        public override string ToString()
        {
            return string.Format("{0},{1},{2},{3}", Left, Top, Right, Bottom);
        }

        public static bool operator ==(Thickness t1, Thickness t2)
        {
            return t1.Left == t2.Left &&
                   t1.Top == t2.Top &&
                   t1.Right == t2.Right &&
                   t1.Bottom == t2.Bottom;
        }
        public static bool operator !=(Thickness t1, Thickness t2)
        {
            return t1.Left != t2.Left ||
                   t1.Top != t2.Top ||
                   t1.Right != t2.Right ||
                   t1.Bottom != t2.Bottom;
        }
    }
}
