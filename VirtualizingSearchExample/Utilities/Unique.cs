using System;
using System.Collections.Generic;
using System.Threading;

///<summary>Adds uniqueness to otherwise equal instances of a type, giving them a consistent ordering.</summary>
public struct Unique<T> {
    private static int _idCounter;
    public readonly T Value;
    public readonly long Id;
    public Unique(T value) {
        this.Value = value;
        this.Id = Interlocked.Increment(ref _idCounter);
    }
    public static IComparer<Unique<T>> MakeComparerUnique(IComparer<T> comparer) {
        if (comparer == null) throw new ArgumentNullException("comparer");
        return new AnonymousComparer<Unique<T>>((e1, e2) => {
            var c = comparer.Compare(e1.Value, e2.Value);
            if (c != 0) return c;
            return e1.Id.CompareTo(e2.Id);
        });
    }
}
