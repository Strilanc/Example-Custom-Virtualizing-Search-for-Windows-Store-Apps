using System;
using System.Collections.Generic;
using System.Linq;
using Windows.System.Threading;
using Windows.UI.Xaml.Controls;

namespace VirtualizingSearchExample {
    sealed partial class MainPage {
        // This constant defines a type of control that can be shown in the search results
        // (each item is represented in text and visually by a text block and slider, with a fixed layout height of 40)
        // (the code to populate the controls is specified when adding items)
        private static readonly FastVerticallyScrollingItemViewer.IVirtualControlType SliderTextControlType
            = new FastVerticallyScrollingItemViewer.AnonymousVirtualControlType(
                height: 40,
                maker: () => {
                    var g = new StackPanel();
                    g.Children.Add(new TextBlock());
                    g.Children.Add(new Slider());
                    return g;
                });

        // the range of numbers to search over
        private static readonly int SearchRangeMax = (int)Math.Pow(10, 5);
        private static readonly IEnumerable<int> SearchRange =
            Enumerable.Range(0, SearchRangeMax) // possible results
                      .Select(e => ((e + 5)*11)%SearchRangeMax); // make results arrive out of order

        public MainPage() {
            this.InitializeComponent();
            // an object to synchronize changes to the displayed search results
            var searchSyncRoot = new object();

            // initialize the fast viewer to get a controller/driver with a type matching the values we want to represent
            var controller = fastView.Init<int>();

            // This method changes the search results to match some new filter, discarding old results
            var curSearchId = 0;
            Action<string> updateSearch = filter => {
                // clear and cancel previous search
                int thisSearchId;
                lock (searchSyncRoot) {
                    // end previous search
                    thisSearchId = ++curSearchId;
                    // clear previous results
                    controller.Clear();
                }


                // do the search NOT on the ui thread, so we don't lock it up
                ThreadPool.RunAsync(x => {
                    // enumerate matching results, showing them as we go
                    foreach (var result in SearchRange.Where(i => i.ToString().Contains(filter))) {
                        lock (searchSyncRoot) {
                            // if a new search has started, we'd better stop adding results
                            if (curSearchId != thisSearchId) break;
                            // add a newly found result
                            controller.Add(
                                // a key to identify the search result, used for more efficient caching
                                key: result,
                                // the virtual control used to show the search result
                                value: new FastVerticallyScrollingItemViewer.AnonymousVirtualControlValue(
                                    // the control type our value is represented by
                                    virtualControlType: SliderTextControlType,
                                    // how to fill in the given control type with our value
                                    filler: control => {
                                        var stackPanel = (StackPanel)control;
                                        var textBlock = (TextBlock)stackPanel.Children.First();
                                        var slider = (Slider)stackPanel.Children.Last();
                                        textBlock.Text = "" + result;
                                        slider.Value = result/(double)SearchRangeMax*100;
                                    },
                                    // how to adjust the given given control type as our value changes
                                    updateContentsIn: xx => { }));
                        }
                    }
                });
            };

            // wire-up searching to the text entered by the user
            txtSearch.Text = "123";
            updateSearch("123");
            txtSearch.TextChanged += (sender, arg) => updateSearch(txtSearch.Text);
        }
    }
}
