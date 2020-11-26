// Copyright (c) 2011-2020 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;

using DynamicData.Kernel;

namespace DynamicData.Cache.Internal
{
    internal class CacheUpdater<TObject, TKey> : ISourceUpdater<TObject, TKey>
        where TKey : notnull
    {
        private readonly ICache<TObject, TKey> _cache;

        private readonly Func<TObject, TKey>? _keySelector;

        public CacheUpdater(ICache<TObject, TKey> cache, Func<TObject, TKey>? keySelector = null)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _keySelector = keySelector;
        }

        public CacheUpdater(Dictionary<TKey, TObject> data, Func<TObject, TKey>? keySelector = null)
        {
            if (data is null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            _cache = new Cache<TObject, TKey>(data);
            _keySelector = keySelector;
        }

        public int Count => _cache.Count;

        public IEnumerable<TObject> Items => _cache.Items;

        public IEnumerable<TKey> Keys => _cache.Keys;

        public IEnumerable<KeyValuePair<TKey, TObject>> KeyValues => _cache.KeyValues;

        public void AddOrUpdate(IEnumerable<TObject> items)
        {
            if (items is null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (_keySelector is null)
            {
                throw new KeySelectorException("A key selector must be specified");
            }

            if (items is IList<TObject> list)
            {
                // zero allocation enumerator
                foreach (var item in EnumerableIList.Create(list))
                {
                    _cache.AddOrUpdate(item, _keySelector(item));
                }
            }
            else
            {
                foreach (var item in items)
                {
                    _cache.AddOrUpdate(item, _keySelector(item));
                }
            }
        }

        public void AddOrUpdate(TObject item)
        {
            if (_keySelector is null)
            {
                throw new KeySelectorException("A key selector must be specified");
            }

            var key = _keySelector(item);
            _cache.AddOrUpdate(item, key);
        }

        public void AddOrUpdate(TObject item, IEqualityComparer<TObject> comparer)
        {
            if (_keySelector is null)
            {
                throw new KeySelectorException("A key selector must be specified");
            }

            var key = _keySelector(item);
            var oldItem = _cache.Lookup(key);
            if (oldItem.HasValue)
            {
                if (comparer.Equals(oldItem.Value, item))
                {
                    return;
                }

                _cache.AddOrUpdate(item, key);
                return;
            }

            _cache.AddOrUpdate(item, key);
        }

        public void AddOrUpdate(IEnumerable<KeyValuePair<TKey, TObject>> itemsPairs)
        {
            if (itemsPairs is IList<KeyValuePair<TKey, TObject>> list)
            {
                // zero allocation enumerator
                foreach (var item in (EnumerableIList<KeyValuePair<TKey, TObject>>)EnumerableIList.Create(list))
                {
                    _cache.AddOrUpdate(item.Value, item.Key);
                }
            }
            else
            {
                foreach (var item in itemsPairs)
                {
                    _cache.AddOrUpdate(item.Value, item.Key);
                }
            }
        }

        public void AddOrUpdate(KeyValuePair<TKey, TObject> item)
        {
            _cache.AddOrUpdate(item.Value, item.Key);
        }

        public void AddOrUpdate(TObject item, TKey key)
        {
            _cache.AddOrUpdate(item, key);
        }

        public void Clear()
        {
            _cache.Clear();
        }

        public void Clone(IChangeSet<TObject, TKey> changes)
        {
            _cache.Clone(changes);
        }

        [Obsolete(Constants.EvaluateIsDead)]
        public void Evaluate(IEnumerable<TKey> keys) => Refresh(keys);

        [Obsolete(Constants.EvaluateIsDead)]
        public void Evaluate(IEnumerable<TObject> items) => Refresh(items);

        [Obsolete(Constants.EvaluateIsDead)]
        public void Evaluate(TObject item) => Refresh(item);

        [Obsolete(Constants.EvaluateIsDead)]
        public void Evaluate()
        {
            Refresh();
        }

        [Obsolete(Constants.EvaluateIsDead)]
        public void Evaluate(TKey key)
        {
            Refresh(key);
        }

        public TKey GetKey(TObject item)
        {
            if (_keySelector is null)
            {
                throw new KeySelectorException("A key selector must be specified");
            }

            return _keySelector(item);
        }

        public IEnumerable<KeyValuePair<TKey, TObject>> GetKeyValues(IEnumerable<TObject> items)
        {
            if (_keySelector is null)
            {
                throw new KeySelectorException("A key selector must be specified");
            }

            return items.Select(t => new KeyValuePair<TKey, TObject>(_keySelector(t), t));
        }

        public void Load(IEnumerable<TObject> items)
        {
            if (items is null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            Clear();
            AddOrUpdate(items);
        }

        public Optional<TObject> Lookup(TKey key)
        {
            var item = _cache.Lookup(key);
            return item.HasValue ? item.Value : Optional.None<TObject>();
        }

        public Optional<TObject> Lookup(TObject item)
        {
            if (_keySelector is null)
            {
                throw new KeySelectorException("A key selector must be specified");
            }

            TKey key = _keySelector(item);
            return Lookup(key);
        }

        public void Refresh()
        {
            _cache.Refresh();
        }

        public void Refresh(IEnumerable<TObject> items)
        {
            if (items is null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (items is IList<TObject> list)
            {
                // zero allocation enumerator
                foreach (var item in EnumerableIList.Create(list))
                {
                    Refresh(item);
                }
            }
            else
            {
                foreach (var item in items)
                {
                    Refresh(item);
                }
            }
        }

        public void Refresh(IEnumerable<TKey> keys)
        {
            if (keys is null)
            {
                throw new ArgumentNullException(nameof(keys));
            }

            if (keys is IList<TKey> list)
            {
                // zero allocation enumerator
                foreach (var item in EnumerableIList.Create(list))
                {
                    Refresh(item);
                }
            }
            else
            {
                foreach (var key in keys)
                {
                    Refresh(key);
                }
            }
        }

        public void Refresh(TObject item)
        {
            if (_keySelector is null)
            {
                throw new KeySelectorException("A key selector must be specified");
            }

            var key = _keySelector(item);
            _cache.Refresh(key);
        }

        public void Refresh(TKey key)
        {
            _cache.Refresh(key);
        }

        public void Remove(IEnumerable<TObject> items)
        {
            if (items is null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (items is IList<TObject> list)
            {
                // zero allocation enumerator
                foreach (var item in EnumerableIList.Create(list))
                {
                    Remove(item);
                }
            }
            else
            {
                foreach (var item in items)
                {
                    Remove(item);
                }
            }
        }

        public void Remove(IEnumerable<TKey> keys)
        {
            if (keys is null)
            {
                throw new ArgumentNullException(nameof(keys));
            }

            if (keys is IList<TKey> list)
            {
                // zero allocation enumerator
                foreach (var key in EnumerableIList.Create(list))
                {
                    Remove(key);
                }
            }
            else
            {
                foreach (var key in keys)
                {
                    Remove(key);
                }
            }
        }

        public void Remove(TObject item)
        {
            if (_keySelector is null)
            {
                throw new KeySelectorException("A key selector must be specified");
            }

            var key = _keySelector(item);
            _cache.Remove(key);
        }

        public void Remove(TKey key)
        {
            _cache.Remove(key);
        }

        public void Remove(IEnumerable<KeyValuePair<TKey, TObject>> items)
        {
            if (items is null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (items is IList<TObject> list)
            {
                // zero allocation enumerator
                foreach (var key in EnumerableIList.Create(list))
                {
                    Remove(key);
                }
            }
            else
            {
                foreach (var key in items)
                {
                    Remove(key);
                }
            }
        }

        public void Remove(KeyValuePair<TKey, TObject> item)
        {
            Remove(item.Key);
        }

        public void RemoveKey(TKey key)
        {
            Remove(key);
        }

        public void RemoveKeys(IEnumerable<TKey> keys)
        {
            if (keys is null)
            {
                throw new ArgumentNullException(nameof(keys));
            }

            _cache.Remove(keys);
        }

        public void Update(IChangeSet<TObject, TKey> changes)
        {
            _cache.Clone(changes);
        }
    }
}