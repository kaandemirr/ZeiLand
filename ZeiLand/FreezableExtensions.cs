using System.Windows;

namespace ZeiLand;

internal static class FreezableExtensions
{
    /// <summary>Freezes a brush/geometry/pen so WPF can share it without change tracking.</summary>
    public static T Frozen<T>(this T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
