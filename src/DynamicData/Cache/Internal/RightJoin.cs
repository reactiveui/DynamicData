// Copyright (c) 2011-2020 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using DynamicData.Kernel;

namespace DynamicData.Cache.Internal
{
    internal class RightJoin<TLeft, TLeftKey, TRight, TRightKey, TDestination>
        where TLeftKey : notnull
        where TRightKey : notnull
    {
        private readonly IObservable<IChangeSet<TLeft, TLeftKey>> _left;

        private readonly Func<TRightKey, Optional<TLeft>, TRight, TDestination> _resultSelector;

        private readonly IObservable<IChangeSet<TRight, TRightKey>> _right;

        private readonly Func<TRight, TLeftKey> _rightKeySelector;

        public RightJoin(IObservable<IChangeSet<TLeft, TLeftKey>> left, IObservable<IChangeSet<TRight, TRightKey>> right, Func<TRight, TLeftKey> rightKeySelector, Func<TRightKey, Optional<TLeft>, TRight, TDestination> resultSelector)
        {
            _left = left ?? throw new ArgumentNullException(nameof(left));
            _right = right ?? throw new ArgumentNullException(nameof(right));
            _rightKeySelector = rightKeySelector ?? throw new ArgumentNullException(nameof(rightKeySelector));
            _resultSelector = resultSelector ?? throw new ArgumentNullException(nameof(resultSelector));
        }

        public IObservable<IChangeSet<TDestination, TRightKey>> Run()
        {
            return Observable.Create<IChangeSet<TDestination, TRightKey>>(
                observer =>
                {
                    var locker = new object();

                    // create local backing stores
                    var leftCache = _left.Synchronize(locker).AsObservableCache(false);
                    var rightCache = _right.Synchronize(locker).AsObservableCache(false);
                    var rightGrouped = _right.Synchronize(locker).GroupWithImmutableState(_rightKeySelector).AsObservableCache(false);

                    // joined is the final cache
                    var joinedCache = new LockFreeObservableCache<TDestination, TRightKey>();

                    var rightLoader = rightCache.Connect().Subscribe(
                        changes =>
                        {
                            joinedCache.Edit(
                                innerCache =>
                                {
                                    foreach (var change in changes.ToConcreteType())
                                    {
                                        var leftKey = _rightKeySelector(change.Current);
                                        switch (change.Reason)
                                        {
                                            case ChangeReason.Add:
                                            case ChangeReason.Update:
                                                // Update with right (and right if it is presents)
                                                var right = change.Current;
                                                var left = leftCache.Lookup(leftKey);
                                                innerCache.AddOrUpdate(_resultSelector(change.Key, left, right), change.Key);
                                                break;

                                            case ChangeReason.Remove:
                                                // remove from result because a right value is expected
                                                innerCache.Remove(change.Key);
                                                break;

                                            case ChangeReason.Refresh:
                                                // propagate upstream
                                                innerCache.Refresh(change.Key);
                                                break;
                                        }
                                    }
                                });
                        });

                    var leftLoader = leftCache.Connect().Subscribe(
                        changes =>
                        {
                            joinedCache.Edit(
                                innerCache =>
                                {
                                    foreach (var change in changes.ToConcreteType())
                                    {
                                        TLeft left = change.Current;
                                        var right = rightGrouped.Lookup(change.Key);

                                        if (right.HasValue)
                                        {
                                            switch (change.Reason)
                                            {
                                                case ChangeReason.Add:
                                                case ChangeReason.Update:
                                                    foreach (var keyvalue in right.Value.KeyValues)
                                                    {
                                                        innerCache.AddOrUpdate(_resultSelector(keyvalue.Key, left, keyvalue.Value), keyvalue.Key);
                                                    }

                                                    break;

                                                case ChangeReason.Remove:
                                                    foreach (var keyvalue in right.Value.KeyValues)
                                                    {
                                                        innerCache.AddOrUpdate(_resultSelector(keyvalue.Key, Optional<TLeft>.None, keyvalue.Value), keyvalue.Key);
                                                    }

                                                    break;

                                                case ChangeReason.Refresh:
                                                    foreach (var key in right.Value.Keys)
                                                    {
                                                        innerCache.Refresh(key);
                                                    }

                                                    break;
                                            }
                                        }
                                    }
                                });
                        });

                    return new CompositeDisposable(joinedCache.Connect().NotEmpty().SubscribeSafe(observer), leftCache, rightCache, rightLoader, joinedCache, leftLoader);
                });
        }
    }
}