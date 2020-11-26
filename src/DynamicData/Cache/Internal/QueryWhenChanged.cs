// Copyright (c) 2011-2020 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Reactive.Linq;

namespace DynamicData.Cache.Internal
{
    internal class QueryWhenChanged<TObject, TKey, TValue>
        where TKey : notnull
    {
        private readonly Func<TObject, IObservable<TValue>>? _itemChangedTrigger;

        private readonly IObservable<IChangeSet<TObject, TKey>> _source;

        public QueryWhenChanged(IObservable<IChangeSet<TObject, TKey>> source, Func<TObject, IObservable<TValue>>? itemChangedTrigger = null)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _itemChangedTrigger = itemChangedTrigger;
        }

        public IObservable<IQuery<TObject, TKey>> Run()
        {
            if (_itemChangedTrigger is null)
            {
                return _source.Scan(
                    (Cache<TObject, TKey>?)null,
                    (cache, changes) =>
                        {
                            if (cache is null)
                            {
                                cache = new Cache<TObject, TKey>(changes.Count);
                            }

                            cache.Clone(changes);
                            return cache;
                        })
                    .Where(x => x is not null)
                    .Select(x => x!)
                    .Select(list => new AnonymousQuery<TObject, TKey>(list));
            }

            return _source.Publish(
                shared =>
                    {
                        var locker = new object();
                        var state = new Cache<TObject, TKey>();

                        var inlineChange = shared.MergeMany(_itemChangedTrigger).Synchronize(locker).Select(_ => new AnonymousQuery<TObject, TKey>(state));

                        var sourceChanged = shared.Synchronize(locker).Scan(
                            state,
                            (list, changes) =>
                                {
                                    list.Clone(changes);
                                    return list;
                                }).Select(list => new AnonymousQuery<TObject, TKey>(list));

                        return sourceChanged.Merge(inlineChange);
                    });
        }
    }
}