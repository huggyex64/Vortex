using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Vortex;

/// <summary>
/// Represents a hierarchical priority as an ordered array of integers.
/// Comparison is lexicographic: <c>[0,34,5]</c> &lt; <c>[1,0,2]</c> &lt; <c>[1,0,4]</c>.
/// A <c>default</c> instance is equivalent to <c>[0]</c>.
/// </summary>
public readonly struct EventPriority : IComparable<EventPriority>, IEquatable<EventPriority>
{
    private static readonly int[] s_default = [0];

    private readonly int[]? _levels;

    /// <summary>The priority levels. Never null; defaults to a single-element <c>[0]</c>.</summary>
    public ReadOnlySpan<int> Levels => _levels ?? s_default;

    public EventPriority(params int[] levels)
    {
        _levels = levels is { Length: > 0 } ? levels : null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CompareTo(EventPriority other)
    {
        int[]  a = _levels ?? s_default;
        int[]  b = other._levels ?? s_default;
        if (ReferenceEquals(a, b)) return 0;
        return CompareLevels(a, b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CompareLevels(int[] a, int[] b)
    {
        int len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            int d = a[i] - b[i];
            if (d != 0) return d;
        }
        return a.Length - b.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(EventPriority other)
    {
        int[] a = _levels ?? s_default;
        int[] b = other._levels ?? s_default;
        if (ReferenceEquals(a, b)) return true;
        return a.AsSpan().SequenceEqual(b);
    }

    public override bool Equals(object? obj) => obj is EventPriority other && Equals(other);

    public override int GetHashCode()
    {
        int[] levels = _levels ?? s_default;
        HashCode hc = new();
        for (int i = 0; i < levels.Length; i++)
            hc.Add(levels[i]);
        return hc.ToHashCode();
    }

    public static bool operator ==(EventPriority left, EventPriority right) => left.Equals(right);
    public static bool operator !=(EventPriority left, EventPriority right) => !left.Equals(right);
    public static bool operator <(EventPriority left, EventPriority right) => left.CompareTo(right) < 0;
    public static bool operator >(EventPriority left, EventPriority right) => left.CompareTo(right) > 0;
    public static bool operator <=(EventPriority left, EventPriority right) => left.CompareTo(right) <= 0;
    public static bool operator >=(EventPriority left, EventPriority right) => left.CompareTo(right) >= 0;

    /// <summary>Allows <c>int</c> to be used where <see cref="EventPriority"/> is expected.</summary>
    public static implicit operator EventPriority(int single) => new(single);

    /// <summary>Allows <c>int[]</c> to be used where <see cref="EventPriority"/> is expected.</summary>
    public static implicit operator EventPriority(int[] levels) => new(levels);

    public override string ToString()
    {
        int[] levels = _levels ?? s_default;
        if (levels.Length == 1) return levels[0].ToString();
        return "[" + string.Join(",", levels) + "]";
    }

    /// <summary>
    /// Sorts a <see cref="List{EventPriority}"/> in-place using an insertion sort
    /// optimized for the small lists typical in event systems. Avoids repeated
    /// <see cref="ReadOnlySpan{T}"/> construction by working directly with the
    /// underlying <c>int[]</c> arrays and uses binary search on the already-sorted
    /// prefix to find the insertion point, reducing the number of comparisons from
    /// O(n²) to O(n log n) while keeping O(n²) moves (which dominate only at
    /// larger sizes where the list would be unusual for an event system).
    /// </summary>
    public static void Sort(List<EventPriority> list)
    {
        Span<EventPriority> span = CollectionsMarshal.AsSpan(list);
        int count = span.Length;
        if (count <= 1) return;

        for (int i = 1; i < count; i++)
        {
            EventPriority key = span[i];
            int[] keyLevels = key._levels ?? s_default;

            // Binary search in the sorted region [0..i)
            int lo = 0, hi = i;
            while (lo < hi)
            {
                int mid = (lo + hi) >>> 1;
                if (CompareLevels(span[mid]._levels ?? s_default, keyLevels) <= 0)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            // Shift elements [lo..i) right by one
            if (lo < i)
            {
                span.Slice(lo, i - lo).CopyTo(span.Slice(lo + 1));
                span[lo] = key;
            }
        }
    }
}
