﻿// Copyright (c) 2011-2020 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using DynamicData.Kernel;

namespace DynamicData.Cache.Internal
{
    /// <summary>
    /// An observable cache which exposes an update API. Used at the root
    /// of all observable chains.
    /// </summary>
    /// <typeparam name="TObject">The type of the object.</typeparam>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    public class LockFreeObservableCache<TObject, TKey> : IObservableCache<TObject, TKey>
        where TKey : notnull
    {
        private readonly ISubject<IChangeSet<TObject, TKey>> _changes = new Subject<IChangeSet<TObject, TKey>>();

        private readonly ISubject<IChangeSet<TObject, TKey>> _changesPreview = new Subject<IChangeSet<TObject, TKey>>();

        private readonly IDisposable _cleanUp;

        private readonly ISubject<int> _countChanged = new Subject<int>();

        private readonly ChangeAwareCache<TObject, TKey> _innerCache = new ChangeAwareCache<TObject, TKey>();

        private readonly ICacheUpdater<TObject, TKey> _updater;

        private bool _isDisposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="LockFreeObservableCache{TObject, TKey}"/> class.
        /// </summary>
        /// <param name="source">The source.</param>
        public LockFreeObservableCache(IObservable<IChangeSet<TObject, TKey>> source)
        {
            _updater = new CacheUpdater<TObject, TKey>(_innerCache);

            var loader = source.Select(
                changes =>
                    {
                        _innerCache.Clone(changes);
                        return _innerCache.CaptureChanges();
                    }).SubscribeSafe(_changes);

            _cleanUp = Disposable.Create(
                () =>
                    {
                        loader.Dispose();
                        _changesPreview.OnCompleted();
                        _changes.OnCompleted();
                        _countChanged.OnCompleted();
                    });
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LockFreeObservableCache{TObject, TKey}"/> class.
        /// </summary>
        public LockFreeObservableCache()
        {
            _updater = new CacheUpdater<TObject, TKey>(_innerCache);

            _cleanUp = Disposable.Create(
                () =>
                    {
                        _changes.OnCompleted();
                        _countChanged.OnCompleted();
                    });
        }

        /// <summary>
        /// Gets the total count of cached items.
        /// </summary>
        public int Count => _innerCache.Count;

        /// <summary>
        /// Gets a count changed observable starting with the current count.
        /// </summary>
        public IObservable<int> CountChanged => _countChanged.StartWith(_innerCache.Count).DistinctUntilChanged();

        /// <summary>
        /// Gets the Items.
        /// </summary>
        public IEnumerable<TObject> Items => _innerCache.Items;

        /// <summary>
        /// Gets the keys.
        /// </summary>
        public IEnumerable<TKey> Keys => _innerCache.Keys;

        /// <summary>
        /// Gets the key value pairs.
        /// </summary>
        public IEnumerable<KeyValuePair<TKey, TObject>> KeyValues => _innerCache.KeyValues;

        /// <summary>
        /// Returns a observable of cache changes preceded with the initial cache state.
        /// </summary>
        /// <param name="predicate">The result will be filtered using the specified predicate.</param>
        /// <returns>An observable that emits the change set.</returns>
        public IObservable<IChangeSet<TObject, TKey>> Connect(Func<TObject, bool>? predicate = null)
        {
            return Observable.Defer(
                () =>
                    {
                        var initial = _innerCache.GetInitialUpdates(predicate);
                        var changes = Observable.Return(initial).Concat(_changes);

                        return (predicate is null ? changes : changes.Filter(predicate)).NotEmpty();
                    });
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Edits the specified edit action.
        /// </summary>
        /// <param name="editAction">The edit action.</param>
        public void Edit(Action<ICacheUpdater<TObject, TKey>> editAction)
        {
            if (editAction is null)
            {
                throw new ArgumentNullException(nameof(editAction));
            }

            editAction(_updater);
            _changes.OnNext(_innerCache.CaptureChanges());
        }

        /// <summary>
        /// Lookup a single item using the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>The looked up value.</returns>
        /// <remarks>
        /// Fast indexed lookup.
        /// </remarks>
        public Optional<TObject> Lookup(TKey key)
        {
            return _innerCache.Lookup(key);
        }

        /// <inheritdoc />
        public IObservable<IChangeSet<TObject, TKey>> Preview(Func<TObject, bool>? predicate = null)
        {
            return predicate is null ? _changesPreview : _changesPreview.Filter(predicate);
        }

        /// <summary>
        /// Returns an observable of any changes which match the specified key. The sequence starts with the initial item in the cache (if there is one).
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>An observable that emits the changes.</returns>
        public IObservable<Change<TObject, TKey>> Watch(TKey key)
        {
            return Observable.Create<Change<TObject, TKey>>(
                observer =>
                    {
                        var initial = _innerCache.Lookup(key);
                        if (initial.HasValue)
                        {
                            observer.OnNext(new Change<TObject, TKey>(ChangeReason.Add, key, initial.Value));
                        }

                        return _changes.Subscribe(
                            changes =>
                                {
                                    foreach (var match in changes.Where(update => update.Key.Equals(key)))
                                    {
                                        observer.OnNext(match);
                                    }
                                });
                    });
        }

        /// <summary>
        /// Disposes of managed and unmanaged resources.
        /// </summary>
        /// <param name="isDisposing">If being called by the dispose method.</param>
        protected virtual void Dispose(bool isDisposing)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            if (isDisposing)
            {
                _cleanUp?.Dispose();
            }
        }
    }
}