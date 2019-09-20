﻿// Copyright (c) 2011-2019 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using DynamicData.Kernel;

namespace DynamicData.Cache.Internal
{
    internal sealed class DistinctCalculator<TObject, TKey, TValue>
    {
        private readonly IObservable<IChangeSet<TObject, TKey>> _source;
        private readonly Func<TObject, TValue> _valueSelector;
        private readonly IDictionary<TValue, int> _valueCounters = new Dictionary<TValue, int>();
        private readonly IDictionary<TKey, int> _keyCounters = new Dictionary<TKey, int>();
        private readonly IDictionary<TKey, TValue> _itemCache = new Dictionary<TKey, TValue>();

        public DistinctCalculator(IObservable<IChangeSet<TObject, TKey>> source, Func<TObject, TValue> valueSelector)
        {
            _source = source;
            _valueSelector = valueSelector ?? throw new ArgumentNullException(nameof(valueSelector));
        }

        public IObservable<DistinctChangeSet<TValue>> Run()
        {
            return _source.Select(Calculate).Where(updates => updates.Count != 0);
        }

        private DistinctChangeSet<TValue> Calculate(IChangeSet<TObject, TKey> changes)
        {
            var result = new DistinctChangeSet<TValue>();

            void AddKeyAction( TKey key, TValue value) => _keyCounters.Lookup(key)
                .IfHasValue(count => _keyCounters[key] = count + 1)
                .Else(() =>
                {
                    _keyCounters[key] = 1;
                    _itemCache[key] = value; // add to cache
                });

            void AddValueAction( TValue value) => _valueCounters.Lookup(value)
                .IfHasValue(count => _valueCounters[value] = count + 1)
                .Else(() =>
                {
                    _valueCounters[value] = 1;
                    result.Add(new Change<TValue, TValue>(ChangeReason.Add, value, value));
                });

            void RemoveKeyAction(TKey key)
            {
                var counter = _keyCounters.Lookup(key);
                if (!counter.HasValue) 
                {
                    return;
                }

                //decrement counter
                var newCount = counter.Value - 1;
                _keyCounters[key] = newCount;
                if (newCount != 0) 
                {
                    return;
                }

                //if there are none, then remove from cache
                _keyCounters.Remove(key);
                _itemCache.Remove(key);
            }

            void RemoveValueAction(TValue value)
            {
                var counter = _valueCounters.Lookup(value);
                if (!counter.HasValue)
                {
                    return;
                }

                //decrement counter
                var newCount = counter.Value - 1;
                _valueCounters[value] = newCount;
                if (newCount != 0)
                {
                    return;
                }

                //if there are none, then remove and notify
                _valueCounters.Remove(value);
                result.Add(new Change<TValue, TValue>(ChangeReason.Remove, value, value));
            }

            var enumerable = changes.ToConcreteType();
            foreach (var change in enumerable)
            {
                var key = change.Key;
                switch (change.Reason)
                {
                    case ChangeReason.Add:
                        {
                            var value = _valueSelector(change.Current);
                            AddKeyAction(key, value);
                            AddValueAction(value);
                            break;
                        }
                    case ChangeReason.Refresh:
                    case ChangeReason.Update:
                        {
                            var value = _valueSelector(change.Current);
                            var previous = _itemCache[key];
                            if (value.Equals(previous))
                            {
                                continue;
                            }

                            RemoveValueAction(previous);
                            AddValueAction(value);
                            _itemCache[key] = value;
                            break;
                        }
                    case ChangeReason.Remove:
                        {
                            var previous = _itemCache[key];
                            RemoveKeyAction(key);
                            RemoveValueAction(previous);
                            break;
                        }
                }
            }
            return result;
        }
    }
}
