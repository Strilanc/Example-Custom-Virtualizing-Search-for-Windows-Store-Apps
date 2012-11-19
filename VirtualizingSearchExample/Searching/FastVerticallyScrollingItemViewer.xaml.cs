using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using System.Linq;

namespace VirtualizingSearchExample {
    public sealed partial class FastVerticallyScrollingItemViewer {
        public FastVerticallyScrollingItemViewer() {
            this.InitializeComponent();
        }

        public Driver<T> Init<T>(IComparer<T> comparer = null) {
            return new Driver<T>(this, comparer ?? Comparer<T>.Default);
        }

        public sealed class Driver<T> {
            private sealed class ControlData {
                public T Key;
                public IVirtualControlValue Value;
                public UserControl Container;
            }

            private HashSet<ControlData> _currentlyShown = new HashSet<ControlData>();
            private PersistentAggregatingRedBlackTree<Unique<T>, ControlData, double> _layoutTree;
            private readonly Dictionary<IVirtualControlType, ControlCache> _availableByType = new Dictionary<IVirtualControlType, ControlCache>();
            private readonly NextActionThrottle _updateShownThrottle = new NextActionThrottle();
            private readonly FastVerticallyScrollingItemViewer _parent;

            public Driver(FastVerticallyScrollingItemViewer parent, IComparer<T> comparer) {
                this._parent = parent;
                this._layoutTree = new PersistentAggregatingRedBlackTree<Unique<T>, ControlData, double>(
                    (a1, k, v, a2) => a1 + v.Value.Type.Height + a2,
                    Unique<T>.MakeComparerUnique(comparer));
                parent.scrollView.ViewChanged += (sender, arg) => UpdateShown();
                parent.scrollView.SizeChanged += (sender, arg) => {
                    parent.layoutArea.Width = arg.NewSize.Width; // Warning: removing this may result in search results that drift right/left as they are manipulated by touch
                    UpdateShown();
                };
            }

            public Unique<T> Add(T key, IVirtualControlValue value) {
                if (value == null) throw new ArgumentNullException("value");
                var uniqueKey = new Unique<T>(key);

                _layoutTree = _layoutTree.With(uniqueKey, new ControlData { Value = value, Key = key }, overwrite: false);
                ScheduleUpdateShown();

                return uniqueKey;
            }
            public void Remove(Unique<T> key) {
                _layoutTree = _layoutTree.WithoutKey(key);
                ScheduleUpdateShown();
            }

            public void ScheduleUpdateShown() {
                _updateShownThrottle.SetNextTask(() => {
                    if (CoreApplication.MainView.CoreWindow.Dispatcher.HasThreadAccess) {
                        UpdateShown();
                    } else {
                        CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, UpdateShown);
                    }
                    return Task.Delay(TimeSpan.FromMilliseconds(100)); // delay next update
                });
            }
            public void Clear() {
                _layoutTree = _layoutTree.WithEmpty();
                ScheduleUpdateShown();
            }
            private void UpdateShown() {
                if (_parent.Visibility == Visibility.Collapsed) return;
                if (_parent.ActualHeight == 0) return;
                if (_layoutTree == null) throw new InvalidOperationException("Not initialized");

                var usedTree = _layoutTree;
                _parent.layoutArea.Height = usedTree.Total;

                var safetyMargin = _parent.scrollView.ActualHeight * 0.25;
                var minHeight = _parent.scrollView.VerticalOffset - safetyMargin;
                var maxHeight = _parent.scrollView.VerticalOffset + _parent.scrollView.ActualHeight + safetyMargin;

                var shown = usedTree.Range(t => t.Item3 < minHeight ? -1 : 0)
                    .Select(e => new {
                        controlData = e.Item2,
                        aggregateHeight = e.Item3,
                        type = e.Item2.Value.Type,
                        y = e.Item3 - e.Item2.Value.Type.Height
                    })
                    .TakeWhile(e => e.y <= maxHeight)
                    .ToArray();

                // cache controls that will not be shown this time, but were shown last time
                var unshown = _currentlyShown;
                _currentlyShown = new HashSet<ControlData>(shown.Select(e => e.controlData));
                var toCollapse = new HashSet<UserControl>(unshown.Select(e => e.Container));
                unshown.ExceptWith(_currentlyShown);
                foreach (var staleControlData in unshown) {
                    _availableByType[staleControlData.Value.Type].Cache(staleControlData);
                }

                foreach (var e in shown) {
                    // show content using a container, if not already showing
                    if (e.controlData.Container == null) {
                        // place content into a cached container
                        if (!_availableByType.ContainsKey(e.type)) _availableByType.Add(e.type, new ControlCache(_parent.layoutArea));
                        var container = _availableByType[e.type].Get(e.controlData);
                        e.controlData.Value.PlaceContentsIn((FrameworkElement)container.Content);
                        e.controlData.Container = container;
                        container.Visibility = Visibility.Visible;
                        this._currentlyShown.Add(e.controlData);
                    }

                    // position container appropriately
                    toCollapse.Remove(e.controlData.Container);
                    e.controlData.Container.Margin = new Thickness(0, e.y, 0, 0);
                    e.controlData.Value.UpdateContentsIn((FrameworkElement)e.controlData.Container.Content);
                }
                foreach (var e in toCollapse)
                    e.Visibility = Visibility.Collapsed;
            }

            private sealed class ControlCache {
                private readonly Dictionary<T, UserControl> _staleByKey = new Dictionary<T, UserControl>();
                private readonly Stack<UserControl> _stales = new Stack<UserControl>();
                private readonly Grid _layoutArea;

                public ControlCache(Grid layoutArea) {
                    this._layoutArea = layoutArea;
                }
                public void Cache(ControlData data) {
                    if (!_staleByKey.ContainsKey(data.Key))
                        _staleByKey.Add(data.Key, data.Container);
                    else
                        _stales.Push(data.Container);
                    data.Container = null;
                }
                public UserControl Get(ControlData data) {
                    // check for control that likely already has the right contents
                    if (_staleByKey.ContainsKey(data.Key)) {
                        var r = _staleByKey[data.Key];
                        _staleByKey.Remove(data.Key);
                        return r;
                    }

                    // check for an extra unused control
                    if (_stales.Count > 0)
                        return _stales.Pop();

                    // grab a control containing different content that is nevertheless ready
                    if (_staleByKey.Count > 0) {
                        var p = _staleByKey.First();
                        _staleByKey.Remove(p.Key);
                        return p.Value;
                    }

                    // create a new control in the layout area
                    var newContainer = new UserControl {
                        VerticalAlignment = VerticalAlignment.Top,
                        Content = data.Value.Type.CreateInstance(),
                        Height = data.Value.Type.ContainerHeight
                    };
                    _layoutArea.Children.Add(newContainer);
                    return newContainer;
                }
            }
        }

        ///<summary>A type of virtual control, used to instantiate framework elements that may be shared by virtual controls.</summary>
        public interface IVirtualControlType {
            ///<summary>The height following controls are displaced downwards by.</summary>
            double Height { get; }
            ///<summary>The height assigned to the control's container (may be greater than standard height to, for example, allow overlap).</summary>
            double ContainerHeight { get; }
            ///<summary>Creates an instance of the control.</summary>
            FrameworkElement CreateInstance();
        }
        ///<summary>A virtual control, with contents that may be assigned to instances of its virtual control type.</summary>
        public interface IVirtualControlValue {
            ///<summary>The type of virtual control. Virtual controls of the same type may use the same framework elements at different times.</summary>
            IVirtualControlType Type { get; }
            ///<summary>Updates the contents of the control (created based on this virtual control's type) to match this virtual control.</summary>
            void PlaceContentsIn(FrameworkElement control);
            ///<summary>Updates the control's layout and other dynamic properties, just before rendering.</summary>
            void UpdateContentsIn(FrameworkElement control);
        }

        ///<summary>Implements an IVirtualControl with delegates.</summary>
        [DebuggerStepThrough]
        public sealed class AnonymousVirtualControlValue : IVirtualControlValue {
            private readonly Action<FrameworkElement> _filler;
            private readonly Action<FrameworkElement> _updateContentsIn;
            public IVirtualControlType Type { get; private set; }
            public AnonymousVirtualControlValue(IVirtualControlType virtualControlType, Action<FrameworkElement> filler, Action<FrameworkElement> updateContentsIn) {
                if (virtualControlType == null) throw new ArgumentNullException("virtualControlType");
                if (filler == null) throw new ArgumentNullException("filler");
                if (updateContentsIn == null) throw new ArgumentNullException("updateContentsIn");
                this.Type = virtualControlType;
                this._filler = filler;
                this._updateContentsIn = updateContentsIn;
            }
            public void PlaceContentsIn(FrameworkElement control) {
                _filler(control);
            }
            public void UpdateContentsIn(FrameworkElement control) {
                _updateContentsIn(control);
            }
        }
        ///<summary>Implements an IVirtualControlType with delegates.</summary>
        [DebuggerStepThrough]
        public sealed class AnonymousVirtualControlType : IVirtualControlType {
            public double Height { get; private set; }
            public double ContainerHeight { get; private set; }
            private readonly Func<FrameworkElement> _maker;
            public AnonymousVirtualControlType(double height, Func<FrameworkElement> maker, double? containerHeight = null) {
                if (maker == null) throw new ArgumentNullException("maker");
                this.Height = height;
                this.ContainerHeight = containerHeight ?? height;
                this._maker = maker;
            }
            public FrameworkElement CreateInstance() {
                return _maker();
            }
        }
    }
}
