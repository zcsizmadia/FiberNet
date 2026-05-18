using System;

namespace FiberNet.Core;

/// <summary>
/// Strongly-typed monotonically-increasing fiber identifier. Zero-alloc — backed by a plain <see langword="long"/>.
/// </summary>
public readonly struct FiberId : IEquatable<FiberId>, IComparable<FiberId>
{
    private static long _counter;

    public long Value { get; }

    private FiberId(long value) => Value = value;

    public static FiberId Next() =>
        new(System.Threading.Interlocked.Increment(ref _counter));

    public bool Equals(FiberId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is FiberId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public int CompareTo(FiberId other) => Value.CompareTo(other.Value);
    public override string ToString() => $"Fiber#{Value}";

    public static bool operator ==(FiberId left, FiberId right) => left.Equals(right);
    public static bool operator !=(FiberId left, FiberId right) => !left.Equals(right);
    public static bool operator <(FiberId left, FiberId right) => left.Value < right.Value;
    public static bool operator <=(FiberId left, FiberId right) => left.Value <= right.Value;
    public static bool operator >(FiberId left, FiberId right) => left.Value > right.Value;
    public static bool operator >=(FiberId left, FiberId right) => left.Value >= right.Value;
}
