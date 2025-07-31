using Microsoft.UI.Xaml.Media;

namespace GitMC.Helpers
{
    /// <summary>
    /// Helper class for common UI operations and safe element finding
    /// </summary>
    internal static class UiHelper
    {
        /// <summary>
        /// Safely finds a named element in the visual tree
        /// </summary>
        /// <typeparam name="T">Type of element to find</typeparam>
        /// <param name="parent">Parent element to search from</param>
        /// <param name="name">Name of the element to find</param>
        /// <returns>Found element or null if not found</returns>
        public static T? FindElementByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (string.IsNullOrEmpty(name))
                return null;

            try
            {
                // Check if parent itself matches
                if (parent is T parentAsT && parentAsT.Name == name)
                    return parentAsT;

                // Search children
                int childCount = VisualTreeHelper.GetChildrenCount(parent);
                for (int i = 0; i < childCount; i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    var result = FindElementByName<T>(child, name);
                    if (result != null)
                        return result;
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Safely finds elements of a specific type in the visual tree
        /// </summary>
        /// <typeparam name="T">Type of elements to find</typeparam>
        /// <param name="parent">Parent element to search from</param>
        /// <returns>List of found elements</returns>
        public static List<T> FindElementsOfType<T>(DependencyObject parent) where T : DependencyObject
        {
            var results = new List<T>();

            try
            {
                FindElementsOfTypeRecursive(parent, results);
                return results;
            }
            catch (Exception)
            {
                return results;
            }
        }

        /// <summary>
        /// Recursive helper for finding elements of a specific type
        /// </summary>
        private static void FindElementsOfTypeRecursive<T>(DependencyObject parent, List<T> results) where T : DependencyObject
        {
            if (parent is T parentAsT)
                results.Add(parentAsT);

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                FindElementsOfTypeRecursive(child, results);
            }
        }

        /// <summary>
        /// Safely gets the parent of a specific type from an element
        /// </summary>
        /// <typeparam name="T">Type of parent to find</typeparam>
        /// <param name="element">Element to start searching from</param>
        /// <returns>Parent of specified type or null if not found</returns>
        public static T? GetParentOfType<T>(DependencyObject element) where T : DependencyObject
        {
            try
            {
                var parent = VisualTreeHelper.GetParent(element);
                while (parent != null)
                {
                    if (parent is T parentAsT)
                        return parentAsT;

                    parent = VisualTreeHelper.GetParent(parent);
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Safely executes an action on the UI thread
        /// </summary>
        /// <param name="action">Action to execute</param>
        /// <param name="dispatcher">Dispatcher to use (optional, uses current if null)</param>
        public static void RunOnUIThread(Action? action, Microsoft.UI.Dispatching.DispatcherQueue? dispatcher = null)
        {
            if (action == null) return;

            try
            {
                var currentDispatcher = dispatcher ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

                if (currentDispatcher?.HasThreadAccess == true)
                {
                    action();
                }
                else if (currentDispatcher != null)
                {
                    currentDispatcher.TryEnqueue(() => action());
                }
            }
            catch (Exception)
            {
                // Silently fail to prevent crashes
            }
        }

        /// <summary>
        /// Creates a throttled action that prevents rapid successive calls
        /// </summary>
        /// <param name="action">Action to throttle</param>
        /// <param name="delay">Delay between allowed executions</param>
        /// <returns>Throttled action</returns>
        public static Action CreateThrottledAction(Action action, TimeSpan delay)
        {
            DateTime lastExecution = DateTime.MinValue;
            object lockObj = new object();

            return () =>
            {
                lock (lockObj)
                {
                    var now = DateTime.Now;
                    if (now - lastExecution >= delay)
                    {
                        lastExecution = now;
                        action();
                    }
                }
            };
        }

        /// <summary>
        /// Creates a debounced action that only executes after a delay without new calls
        /// </summary>
        /// <param name="action">Action to debounce</param>
        /// <param name="delay">Delay before execution</param>
        /// <returns>Debounced action</returns>
        public static Action CreateDebouncedAction(Action action, TimeSpan delay)
        {
            Timer? timer = null;
            object lockObj = new object();

            return () =>
            {
                lock (lockObj)
                {
                    timer?.Dispose();
                    timer = new Timer(_ => action(), null, delay, TimeSpan.FromMilliseconds(-1));
                }
            };
        }
    }
}
