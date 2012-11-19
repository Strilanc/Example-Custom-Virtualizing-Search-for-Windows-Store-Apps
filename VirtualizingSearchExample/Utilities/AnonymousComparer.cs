using System;
using System.Collections.Generic;
using System.Diagnostics;

[DebuggerStepThrough]
public sealed class AnonymousComparer<T> : IComparer<T> {
    private readonly Func<T, T, int> _compare;
    public AnonymousComparer(Func<T, T, int> compare) {
        if (compare == null) throw new ArgumentNullException("compare");
        this._compare = compare;
    }
    public int Compare(T x, T y) {
        return _compare(x, y);
    }
}
