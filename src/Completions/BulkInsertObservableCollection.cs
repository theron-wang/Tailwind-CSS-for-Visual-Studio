using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Threading;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;

namespace TailwindCSSIntellisense.Completions;

internal class BulkInsertObservableCollection<T> : BulkObservableCollection<T>
{
    private readonly Dispatcher _dispatcher;
    private const string _collectionChangedDuringRangeOperation =
        "_collectionChangedDuringRangeOperation";
    private delegate void AddRangeToBeginningCallback(IList<T> items);

    public BulkInsertObservableCollection()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    /// <summary>
    /// Functionally equivalent to <see cref="List{T}.InsertRange(int, IEnumerable{T})"/> where the first parameter is 0
    /// </summary>
    /// <param name="items">The list of items to prepend</param>
    ///
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "VSTHRD102:Implement internal logic asynchronously",
        Justification = "Not expensive"
    )]
    public void AddRangeToBeginning(IEnumerable<T> items)
    {
        if (items == null || !items.Any())
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            try
            {
                BeginBulkOperation();
                SetField(_collectionChangedDuringRangeOperation, true);
                foreach (T item in items.Reverse())
                {
                    Items.Insert(0, item);
                }
            }
            finally
            {
                EndBulkOperation();
            }
        }
        else
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
#pragma warning disable VSTHRD001 // Avoid legacy thread switching APIs
                await _dispatcher.BeginInvoke(
                    DispatcherPriority.Send,
                    new AddRangeToBeginningCallback(AddRangeToBeginning),
                    items
                );
#pragma warning restore VSTHRD001 // Avoid legacy thread switching APIs
            });
        }
    }

    // https://stackoverflow.com/questions/6961781/reflecting-a-private-field-from-a-base-class
    private void SetField(string fieldName, object value)
    {
        Type t = GetType();
        FieldInfo? fi = null;

        while (t != null)
        {
            fi = t.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            if (fi != null)
            {
                break;
            }

            t = t.BaseType;
        }

        fi?.SetValue(this, value);
    }
}
