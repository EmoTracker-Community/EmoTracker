namespace EmoTracker.Data.Locations
{
    /// <summary>
    /// Controls where the badge image appears relative to the center of the location blip.
    /// Imagine a 3×3 grid of positions around the blip; the value names the position at
    /// which the badge will be rendered:
    ///
    ///   TopLeft   | Top    | TopRight
    ///   Left      | Center | Right
    ///   BottomLeft| Bottom | BottomRight
    ///
    /// The default and original behaviour is <c>BottomRight</c>: the badge's top-left corner
    /// sits at the blip center so the badge extends to the lower-right.
    /// </summary>
    public enum BadgeAlignment
    {
        TopLeft,
        Top,
        TopRight,
        Left,
        Center,
        Right,
        BottomLeft,
        Bottom,
        BottomRight
    }
}
