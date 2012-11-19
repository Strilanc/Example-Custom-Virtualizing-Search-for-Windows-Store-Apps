using System;
using System.Collections.Generic;
using System.Diagnostics;

internal delegate int RangeComparer<in T>(T value);
internal sealed class PersistentAggregatingRedBlackTree<TKey, TVal, TAggregate> {
    [DebuggerDisplay("{ToString()}")]
    private sealed class KeyVal {
        public readonly TKey Key;
        public readonly TVal Val;
        public KeyVal(TKey key, TVal val) {
            this.Key = key;
            this.Val = val;
        }
        public override string ToString() {
            return String.Format("{0}: {1}", Key, Val);
        }
    }
    [DebuggerDisplay("{ToString()}")]
    private sealed class LeafwardNode {
        public static readonly LeafwardNode Nil = new LeafwardNode();

        public readonly KeyVal Data;
        public readonly bool IsRed;
        public bool IsBlack { get { return !IsRed; } }
        public readonly LeafwardNode Left;
        public readonly LeafwardNode Right;
        public readonly TAggregate Aggregate;
        public readonly int Count;
        private LeafwardNode() {
            this.Left = this.Right = this;
        }
        public LeafwardNode(KeyVal data, bool isRed, LeafwardNode left, LeafwardNode right, Func<TAggregate, TKey, TVal, TAggregate, TAggregate> aggregator) {
            this.Data = data;
            this.Left = left;
            this.Right = right;
            this.IsRed = isRed;
            this.Aggregate = data == null ? default(TAggregate) : aggregator(left.Aggregate, data.Key, data.Val, right.Aggregate);
            this.Count = data == null ? 0 : left.Count + 1 + right.Count;
        }
        public static LeafwardNode From(KeyVal data, bool isRed, LeafwardNode c1, LeafwardNode c2, bool c1IsLeft, Func<TAggregate, TKey, TVal, TAggregate, TAggregate> aggregator) {
            return new LeafwardNode(data, isRed, c1IsLeft ? c1 : c2, c1IsLeft ? c2 : c1, aggregator);
        }
        public LeafwardNode Child(bool toLeft) {
            return toLeft ? Left : Right;
        }
        public bool IsNilLeaf {
            get {
                return this.Data == null && this.Left.Data == null && this.Right.Data == null;
            }
        }
        public override string ToString() {
            if (this.IsNilLeaf) return "nil leaf";
            return Data.ToString();
        }
        public void CheckNodeProperties() {
            CheckMaxBlackToLeaf();
            if (this.IsRed && (this.Left.IsRed || this.Right.IsRed))
                throw new ArgumentException("Red node with red child");
        }
        private int CheckMaxBlackToLeaf() {
            if (this.IsNilLeaf) return 1;
            var c1 = this.Left.CheckMaxBlackToLeaf();
            var c2 = this.Right.CheckMaxBlackToLeaf();
            if (c1 != c2) throw new ArgumentException("Different numbers of black nodes to leaf.");
            var d = this.IsRed ? 0 : 1;
            return c1 + d;
        }
    }
    [DebuggerDisplay("{ToString()}")]
    private sealed class RootwardNode {
        public static readonly RootwardNode Nil = new RootwardNode();

        public readonly KeyVal Data;
        public readonly bool IsRed;
        public bool IsBlack { get { return !IsRed; } }
        public readonly RootwardNode Parent;
        public readonly LeafwardNode SiblingOfChild;
        public readonly bool IsChildOnLeft;
        private RootwardNode() {
            this.Parent = this;
            this.SiblingOfChild = LeafwardNode.Nil;
            this.Data = null;
            this.IsRed = false;
        }
        public RootwardNode(KeyVal data, bool isRed, RootwardNode parent, LeafwardNode siblingOfChild, bool isLeft) {
            if (data == null) throw new ArgumentNullException("data");
            if (parent == null) throw new ArgumentNullException("parent");
            if (siblingOfChild == null) throw new ArgumentNullException("siblingOfChild");
            this.Data = data;
            this.Parent = parent;
            this.SiblingOfChild = siblingOfChild;
            this.IsChildOnLeft = isLeft;
            this.IsRed = isRed;
        }
        public bool IsNilRoot {
            get {
                return this.Data == null && this.Parent.Data == null;
            }
        }
        public override string ToString() {
            if (this.IsNilRoot) return "nil root";
            if (this.Parent.IsNilRoot) return "(root) " + Data;
            return Data.ToString();
        }
    }
    [DebuggerDisplay("{ToString()}")]
    private sealed class ZipperNode {
        public readonly KeyVal Data;
        public readonly bool IsRed;
        public readonly LeafwardNode Left;
        public readonly LeafwardNode Right;
        public readonly RootwardNode Parent;
        public readonly Func<TAggregate, TKey, TVal, TAggregate, TAggregate> Aggregator;
        public ZipperNode(Func<TAggregate, TKey, TVal, TAggregate, TAggregate> aggregator) {
            this.Left = LeafwardNode.Nil;
            this.Right = LeafwardNode.Nil;
            this.Parent = RootwardNode.Nil;
            this.Data = null;
            this.IsRed = false;
            this.Aggregator = aggregator;
        }
        public ZipperNode(KeyVal content, bool isRed, LeafwardNode left, LeafwardNode right, RootwardNode parent, Func<TAggregate, TKey, TVal, TAggregate, TAggregate> aggregator) {
            this.Data = content;
            this.IsRed = isRed;
            this.Left = left;
            this.Right = right;
            this.Parent = parent;
            this.Aggregator = aggregator;
        }
        public static ZipperNode From(RootwardNode parent, KeyVal content, bool isRed, LeafwardNode c1, LeafwardNode c2, bool c1IsLeft, Func<TAggregate, TKey, TVal, TAggregate, TAggregate> aggregator) {
            return new ZipperNode(content, isRed, c1IsLeft ? c1 : c2, c1IsLeft ? c2 : c1, parent, aggregator);
        }

        public LeafwardNode AsDownward() {
            if (IsNilLeaf) return LeafwardNode.Nil;
            return new LeafwardNode(this.Data, this.IsRed, this.Left, this.Right, Aggregator);
        }
        public RootwardNode AsUpwardFor(bool toLeft) {
            if (IsNilRoot) return RootwardNode.Nil;
            return new RootwardNode(this.Data, this.IsRed, this.Parent, toLeft ? Right : Left, toLeft);
        }

        public ZipperNode ZipToRoot() {
            var p = this;
            while (!p.Parent.IsNilRoot)
                p = p.ZipToParent();
            return p;
        }
        public ZipperNode ZipToParent() {
            if (IsNilRoot) throw new InvalidOperationException("nil root has no parent");
            var c = this.AsDownward();
            return new ZipperNode(
                Parent.Data,
                Parent.IsRed,
                Parent.IsChildOnLeft ? c : Parent.SiblingOfChild,
                Parent.IsChildOnLeft ? Parent.SiblingOfChild : c,
                Parent.Parent,
                Aggregator);
        }
        public ZipperNode ZipToChild(bool toLeft) {
            if (IsNilLeaf) throw new InvalidOperationException("nil leaf has no children");
            var d = toLeft ? Left : Right;
            return new ZipperNode(d.Data, d.IsRed, d.Left, d.Right, this.AsUpwardFor(toLeft), Aggregator);
        }
        public ZipperNode ZipToSibling() {
            if (IsNilRoot) throw new InvalidOperationException();
            var u = Parent.SiblingOfChild;
            return new ZipperNode(u.Data, u.IsRed, u.Left, u.Right, new RootwardNode(this.Parent.Data, this.Parent.IsRed, this.Parent.Parent, this.AsDownward(), !this.Parent.IsChildOnLeft), Aggregator);
        }
        public LeafwardNode Child(bool toLeft) {
            return toLeft ? Left : Right;
        }
        public ZipperNode WithParent(RootwardNode newParent) {
            return new ZipperNode(Data, IsRed, Left, Right, newParent, Aggregator);
        }
        public ZipperNode WithContents(KeyVal contents) {
            return new ZipperNode(contents, this.IsRed, this.Left, this.Right, this.Parent, Aggregator);
        }
        public ZipperNode ColoredRed() {
            return new ZipperNode(this.Data, true, this.Left, this.Right, this.Parent, Aggregator);
        }
        public ZipperNode ColoredLike(RootwardNode other) {
            return new ZipperNode(this.Data, other.IsRed, this.Left, this.Right, this.Parent, Aggregator);
        }
        public ZipperNode ColoredBlack() {
            return new ZipperNode(this.Data, false, this.Left, this.Right, this.Parent, Aggregator);
        }

        public bool IsLeftChild() {
            return Parent.IsChildOnLeft;
        }
        public bool IsRightChild() {
            return !Parent.IsChildOnLeft;
        }
        public LeafwardNode Sibling() {
            return this.Parent.SiblingOfChild;
        }
        public LeafwardNode Uncle() {
            return this.Parent.Parent.SiblingOfChild;
        }

        /// <summary>
        /// Rotates the tree, maintaining sorted order, such that the node p becomes the child of its child in the specified direction.
        /// The node p's direction from its new parent (former child) will be the opposite of the specified direction.
        /// The return value is the former child.
        /// </summary>
        public ZipperNode Rotate(bool toLeft) {
            // before
            //  -> v1
            //p=v2
            //     -> v3
            //  -> v4
            //     -> v5
            var p = this;
            var n1 = p.Child(toLeft);
            var n2 = p;
            var n3 = p.Child(!toLeft).Child(toLeft);
            var n4 = p.Child(!toLeft);
            var n5 = p.Child(!toLeft).Child(!toLeft);

            // after
            //     -> v1
            //  -> v2 
            //     -> v3
            //r=v4
            //  -> v5
            return From(p.Parent, n4.Data, n4.IsRed, LeafwardNode.From(n2.Data, n2.IsRed, n1, n3, toLeft, Aggregator), n5, toLeft, Aggregator);
        }

        public ZipperNode DeleteAndZipToRoot() {
            if (!Left.IsNilLeaf && !Right.IsNilLeaf) {
                return WithContents(ZipToSuccessorNode().Data).ZipToSuccessorNode().DeleteAndZipToRoot();
            }

            var c = ZipToChild(!Left.IsNilLeaf).WithParent(Parent);
            if (IsRed) return c.ZipToRoot();
            if (c.IsRed) return c.ColoredBlack().ZipToRoot();
            return c.DeleteRebalanceAndZipToRoot();
        }
        public ZipperNode DeleteRebalanceAndZipToRoot() {
            if (this.Parent.IsNilRoot) return this;

            var p = this.Parent;
            var s = this.Sibling();
            var left = this.IsLeftChild();
            var right = !left;
            var sL = s.Child(left);
            var sR = s.Child(right);

            if (s.IsRed) return this.ColoredBlack()
                                    .ZipToSibling().ColoredBlack()
                                    .ZipToParent().ColoredRed()
                                    .Rotate(left)
                                    .ZipToChild(left)
                                    .ZipToChild(left)
                                    .DeleteRebalanceAndZipToRoot();
            if (sL.IsBlack && sR.IsBlack) {
                if (p.IsBlack) return this.ZipToSibling().ColoredRed()
                                            .ZipToParent()
                                            .DeleteRebalanceAndZipToRoot();
                return this.ZipToSibling().ColoredRed()
                            .ZipToParent().ColoredBlack()
                            .ZipToRoot();
            }
            if (sR.IsBlack) return this.ZipToSibling().ColoredRed()
                                        .Rotate(right).ColoredBlack()
                                        .ZipToSibling()
                                        .DeleteRebalanceAndZipToRoot();
            return this.ZipToSibling().ColoredLike(p)
                        .ZipToParent().ColoredBlack()
                        .Rotate(left)
                        .ZipToChild(right).ColoredBlack()
                        .ZipToRoot();
        }

        ///<summary>Gets the node containing the next higher key, or the nil node if no such node exists.</summary>
        public ZipperNode ZipToSuccessorNode() {
            if (!Right.IsNilLeaf) {
                var c = ZipToChild(toLeft: false);
                while (!c.Left.IsNilLeaf) {
                    c = c.ZipToChild(toLeft: true);
                }
                return c;
            }

            var p = this;
            while (p.IsRightChild()) {
                p = p.ZipToParent();
            }
            p = p.ZipToParent();
            if (p.Parent.IsNilRoot) return new ZipperNode(Aggregator);
            return p;
        }

        public bool IsNilLeaf {
            get {
                return this.Data == null && this.Left.Data == null && this.Right.Data == null;
            }
        }
        public bool IsNilRoot {
            get {
                return this.Data == null && this.Parent.Data == null;
            }
        }
        public override string ToString() {
            if (IsNilLeaf && IsNilRoot) return "nil";
            if (IsNilLeaf && this.Parent.IsNilRoot) return "nil leaf below nil root";
            if (IsNilRoot) return "nil root above " + this.Left + " and " + this.Right;
            if (IsNilLeaf) return "nil leaf below " + this.Parent;
            if (this.Parent.IsNilRoot) return "(root) " + Data;
            return Data.ToString();
        }
    }

    private readonly LeafwardNode _root;
    private readonly IComparer<TKey> _comparer;
    private readonly Func<TAggregate, TKey, TVal, TAggregate, TAggregate> _aggregator;
    public PersistentAggregatingRedBlackTree(Func<TAggregate, TKey, TVal, TAggregate, TAggregate> aggregator, IComparer<TKey> comparer = null) {
        if (aggregator == null) throw new ArgumentNullException("aggregator");
        this._root = LeafwardNode.Nil;
        this._aggregator = aggregator;
        this._comparer = comparer ?? Comparer<TKey>.Default;
    }
    private PersistentAggregatingRedBlackTree(LeafwardNode root, Func<TAggregate, TKey, TVal, TAggregate, TAggregate> aggregator, IComparer<TKey> comparer) {
        this._root = root;
        this._aggregator = aggregator;
        this._comparer = comparer;
    }
    public TVal this[TKey key] {
        get {
            var n = TryGetContentByKey(key);
            if (n == null) throw new InvalidOperationException("Missing key");
            return n.Val;
        }
    }
    public PersistentAggregatingRedBlackTree<TKey, TVal, TAggregate> WithoutKey(TKey key) {
        var n = TryZipToNodeByKey(key);
        if (n == null) return this;
        return WithRoot(n.DeleteAndZipToRoot());
    }
    public PersistentAggregatingRedBlackTree<TKey, TVal, TAggregate> WithEmpty() {
        return WithRoot(new ZipperNode(_aggregator));
    }

    private PersistentAggregatingRedBlackTree<TKey, TVal, TAggregate> WithRoot(ZipperNode node) {
        var root = node.IsNilRoot ? LeafwardNode.Nil : new LeafwardNode(node.Data, node.IsRed, node.Left, node.Right, _aggregator);
        return new PersistentAggregatingRedBlackTree<TKey, TVal, TAggregate>(root, _aggregator, _comparer);
    }
    private ZipperNode ZipStartAtRoot() {
        if (this._root.IsNilLeaf) return new ZipperNode(_aggregator);
        return new ZipperNode(this._root.Data, this._root.IsRed, this._root.Left, this._root.Right, RootwardNode.Nil, _aggregator);
    }

    public void CheckProperties() {
        if (this._root.IsRed) throw new ArgumentException("Red root");
        this._root.CheckNodeProperties();
    }

    public PersistentAggregatingRedBlackTree<TKey, TVal, TAggregate> With(TKey key, TVal value, bool? overwrite = null) {
        //find insertion point, or existing node with same key
        var n = ZipStartAtRoot();
        while (!n.IsNilLeaf) {
            var d = _comparer.Compare(key, n.Data.Key);
            if (d == 0) {
                if (overwrite.HasValue && !overwrite.Value) throw new InvalidOperationException("Duplicate Key");
                if (ReferenceEquals(n.Data.Val, value)) return this;
                n = n.WithContents(new KeyVal(key, value));
                return this.WithRoot(n.ZipToRoot());
            }

            n = n.ZipToChild(toLeft: d < 0);
        }
        if (overwrite.HasValue && overwrite.Value) throw new InvalidOperationException("Missing Key");

        //insert over nil leaf
        n = n.WithContents(new KeyVal(key, value)).ColoredRed();

        //rebalance
        while (n.Parent.IsRed) {
            if (n.Uncle().IsRed) {
                n = n.ZipToParent().ColoredBlack()
                        .ZipToSibling().ColoredBlack()
                        .ZipToParent().ColoredRed();
            } else {
                var bc = n.IsLeftChild();
                n = n.ZipToParent();
                var bp = n.IsLeftChild();
                if (bc != bp) n = n.Rotate(toLeft: bp);
                n = n.ColoredBlack();
                n = n.ZipToParent().ColoredRed();
                n = n.Rotate(toLeft: !bp);
                break;
            }
        }
        return this.WithRoot(n.ZipToRoot().ColoredBlack());
    }

    private KeyVal TryGetContentByKey(TKey key) {
        var n = this._root;
        while (true) {
            if (n.IsNilLeaf) return null;
            var d = _comparer.Compare(key, n.Data.Key);
            if (d == 0) return n.Data;
            n = n.Child(d < 0);
        }
    }
    private ZipperNode TryZipToNodeByKey(TKey key) {
        var n = this.ZipStartAtRoot();
        while (true) {
            if (n.IsNilLeaf) return null;
            var d = _comparer.Compare(key, n.Data.Key);
            if (d == 0) return n;
            n = n.ZipToChild(d < 0);
        }
    }

	///<summary>Enumerates the tree nodes whose aggregate from left to right is within the given range.</summary>
    public IEnumerable<Tuple<TKey, TVal, TAggregate>> Range(RangeComparer<Tuple<TKey, TVal, TAggregate>> range = null) {
        if (_root.IsNilLeaf) yield break;
        range = range ?? (e => 0);

        // recursive enumeration rewritten to use an explicit stack
        // (to avoid iterator methods using yield must do work for each recursive level for each item, due to how they are implemented)
        var callStack = new Stack<Tuple<LeafwardNode, bool, TAggregate>>();
        callStack.Push(Tuple.Create(_root, true, default(TAggregate)));
        while (callStack.Count > 0) {
            var item = callStack.Pop();
            var node = item.Item1;
            var exploreVsYield = item.Item2;
            var leftOverhead = item.Item3;
            if (node.IsNilLeaf) continue;

            var leftOverheadForRightChild = _aggregator(leftOverhead, node.Data.Key, node.Data.Val, node.Left.Aggregate);
            var cur = Tuple.Create(node.Data.Key, node.Data.Val, leftOverheadForRightChild);
            if (exploreVsYield) {
                var c = range(cur);
                // schedule recurse right when current node is below range maximum
                if (c <= 0) callStack.Push(Tuple.Create(node.Right, true, leftOverheadForRightChild));
                // schedule yield self when current node is in range
                if (c == 0) callStack.Push(Tuple.Create(node, false, leftOverhead));
                // schedule recurse left when current node is below range minimum
                if (c >= 0) callStack.Push(Tuple.Create(node.Left, true, leftOverhead));
            } else {
                yield return cur;
            }
        }
    }

    public int Count { get { return _root.Count; } }
    public TAggregate Total { get { return _root.Aggregate; } }
    public bool ContainsKey(TKey key) {
        return TryGetContentByKey(key) != null;
    }
    public bool TryGetValue(TKey key, out TVal value) {
        var content = TryGetContentByKey(key);
        value = content != null ? content.Val : default(TVal);
        return content != null;
    }
}
