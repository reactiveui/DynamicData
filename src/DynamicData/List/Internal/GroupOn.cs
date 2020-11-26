// Copyright (c) 2011-2020 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using DynamicData.Kernel;

namespace DynamicData.List.Internal
{
    internal sealed class GroupOn<TObject, TGroupKey>
        where TGroupKey : notnull
    {
        private readonly Func<TObject, TGroupKey> _groupSelector;

        private readonly IObservable<Unit>? _regrouper;

        private readonly IObservable<IChangeSet<TObject>> _source;

        public GroupOn(IObservable<IChangeSet<TObject>> source, Func<TObject, TGroupKey> groupSelector, IObservable<Unit>? regrouper)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _groupSelector = groupSelector ?? throw new ArgumentNullException(nameof(groupSelector));
            _regrouper = regrouper;
        }

        public IObservable<IChangeSet<IGroup<TObject, TGroupKey>>> Run()
        {
            return Observable.Create<IChangeSet<IGroup<TObject, TGroupKey>>>(
                observer =>
                    {
                        var groupings = new ChangeAwareList<IGroup<TObject, TGroupKey>>();
                        var groupCache = new Dictionary<TGroupKey, Group<TObject, TGroupKey>>();

                        // capture the grouping up front which has the benefit that the group key is only selected once
                        var itemsWithGroup = _source.Transform<TObject, ItemWithGroupKey>((t, previous) => new ItemWithGroupKey(t, _groupSelector(t), previous.Convert(p => p.Group)), true);

                        var locker = new object();
                        var shared = itemsWithGroup.Synchronize(locker).Publish();

                        var grouper = shared.Select(changes => Process(groupings, groupCache, changes));

                        IObservable<IChangeSet<IGroup<TObject, TGroupKey>>> regrouper;
                        if (_regrouper is null)
                        {
                            regrouper = Observable.Never<IChangeSet<IGroup<TObject, TGroupKey>>>();
                        }
                        else
                        {
                            regrouper = _regrouper.Synchronize(locker).CombineLatest(shared.ToCollection(), (_, collection) => Regroup(groupings, groupCache, collection));
                        }

                        var publisher = grouper.Merge(regrouper).DisposeMany() // dispose removes as the grouping is disposable
                            .NotEmpty().SubscribeSafe(observer);

                        return new CompositeDisposable(publisher, shared.Connect());
                    });
        }

        private static GroupWithAddIndicator GetCache(IDictionary<TGroupKey, Group<TObject, TGroupKey>> groupCaches, TGroupKey key)
        {
            var cache = groupCaches.Lookup(key);
            if (cache.HasValue)
            {
                return new GroupWithAddIndicator(cache.Value, false);
            }

            var newcache = new Group<TObject, TGroupKey>(key);
            groupCaches[key] = newcache;
            return new GroupWithAddIndicator(newcache, true);
        }

        private static IChangeSet<IGroup<TObject, TGroupKey>> Process(ChangeAwareList<IGroup<TObject, TGroupKey>> result, IDictionary<TGroupKey, Group<TObject, TGroupKey>> groupCollection, IChangeSet<ItemWithGroupKey> changes)
        {
            foreach (var grouping in changes.Unified().GroupBy(change => change.Current.Group))
            {
                // lookup group and if created, add to result set
                var currentGroup = grouping.Key;
                var lookup = GetCache(groupCollection, currentGroup);
                var groupCache = lookup.Group;

                if (lookup.WasCreated)
                {
                    result.Add(groupCache);
                }

                // start a group edit session, so all changes are batched
                groupCache.Edit(
                    list =>
                        {
                            // iterate through the group's items and process
                            foreach (var change in grouping)
                            {
                                switch (change.Reason)
                                {
                                    case ListChangeReason.Add:
                                        {
                                            list.Add(change.Current.Item);
                                            break;
                                        }

                                    case ListChangeReason.Replace:
                                        {
                                            var previousItem = change.Previous.Value.Item;
                                            var previousGroup = change.Previous.Value.Group;

                                            // check whether an item changing has resulted in a different group
                                            if (previousGroup.Equals(currentGroup))
                                            {
                                                // find and replace
                                                var index = list.IndexOf(previousItem);
                                                list[index] = change.Current.Item;
                                            }
                                            else
                                            {
                                                // add to new group
                                                list.Add(change.Current.Item);

                                                // remove from old group
                                                groupCollection.Lookup(previousGroup).IfHasValue(
                                                    g =>
                                                        {
                                                            g.Edit(oldList => oldList.Remove(previousItem));
                                                            if (g.List.Count != 0)
                                                            {
                                                                return;
                                                            }

                                                            groupCollection.Remove(g.GroupKey);
                                                            result.Remove(g);
                                                        });
                                            }

                                            break;
                                        }

                                    case ListChangeReason.Refresh:
                                        {
                                            // 1. Check whether item was in the group and should not be now (or vice versa)
                                            var currentItem = change.Current.Item;
                                            var previousGroup = change.Current.PreviousGroup.Value;

                                            // check whether an item changing has resulted in a different group
                                            if (previousGroup.Equals(currentGroup))
                                            {
                                                // Propagate refresh event
                                                var cal = (ChangeAwareList<TObject>)list;
                                                cal.Refresh(currentItem);
                                            }
                                            else
                                            {
                                                // add to new group
                                                list.Add(currentItem);

                                                // remove from old group if empty
                                                groupCollection.Lookup(previousGroup).IfHasValue(
                                                    g =>
                                                        {
                                                            g.Edit(oldList => oldList.Remove(currentItem));
                                                            if (g.List.Count != 0)
                                                            {
                                                                return;
                                                            }

                                                            groupCollection.Remove(g.GroupKey);
                                                            result.Remove(g);
                                                        });
                                            }

                                            break;
                                        }

                                    case ListChangeReason.Remove:
                                        {
                                            list.Remove(change.Current.Item);
                                            break;
                                        }

                                    case ListChangeReason.Clear:
                                        {
                                            list.Clear();
                                            break;
                                        }
                                }
                            }
                        });

                if (groupCache.List.Count == 0)
                {
                    groupCollection.Remove(groupCache.GroupKey);
                    result.Remove(groupCache);
                }
            }

            return result.CaptureChanges();
        }

        private IChangeSet<IGroup<TObject, TGroupKey>> Regroup(ChangeAwareList<IGroup<TObject, TGroupKey>> result, IDictionary<TGroupKey, Group<TObject, TGroupKey>> groupCollection, IReadOnlyCollection<ItemWithGroupKey> currentItems)
        {
            // TODO: We need to update ItemWithValue>
            foreach (var itemWithValue in currentItems)
            {
                var currentGroupKey = itemWithValue.Group;
                var newGroupKey = _groupSelector(itemWithValue.Item);
                if (newGroupKey.Equals(currentGroupKey))
                {
                    continue;
                }

                // remove from the old group
                var currentGroupLookup = GetCache(groupCollection, currentGroupKey);
                var currentGroupCache = currentGroupLookup.Group;
                currentGroupCache.Edit(innerList => innerList.Remove(itemWithValue.Item));

                if (currentGroupCache.List.Count == 0)
                {
                    groupCollection.Remove(currentGroupKey);
                    result.Remove(currentGroupCache);
                }

                // Mark the old item with the new cache group
                itemWithValue.Group = newGroupKey;

                // add to the new group
                var newGroupLookup = GetCache(groupCollection, newGroupKey);
                var newGroupCache = newGroupLookup.Group;
                newGroupCache.Edit(innerList => innerList.Add(itemWithValue.Item));

                if (newGroupLookup.WasCreated)
                {
                    result.Add(newGroupCache);
                }
            }

            return result.CaptureChanges();
        }

        private readonly struct GroupWithAddIndicator
        {
            public GroupWithAddIndicator(Group<TObject, TGroupKey> group, bool wasCreated)
                : this()
            {
                Group = group;
                WasCreated = wasCreated;
            }

            public Group<TObject, TGroupKey> Group { get; }

            public bool WasCreated { get; }
        }

        private sealed class ItemWithGroupKey : IEquatable<ItemWithGroupKey>
        {
            public ItemWithGroupKey(TObject item, TGroupKey group, Optional<TGroupKey> previousGroup)
            {
                Item = item;
                Group = group;
                PreviousGroup = previousGroup;
            }

            public TGroupKey Group { get; set; }

            public TObject Item { get; }

            public Optional<TGroupKey> PreviousGroup { get; }

            /// <summary>Returns a value that indicates whether the values of two <see cref="GroupOn{TObject, TGroupKey}.ItemWithGroupKey" /> objects are equal.</summary>
            /// <param name="left">The first value to compare.</param>
            /// <param name="right">The second value to compare.</param>
            /// <returns>true if the <paramref name="left" /> and <paramref name="right" /> parameters have the same value; otherwise, false.</returns>
            public static bool operator ==(ItemWithGroupKey left, ItemWithGroupKey right)
            {
                return Equals(left, right);
            }

            /// <summary>Returns a value that indicates whether two <see cref="GroupOn{TObject, TGroupKey}.ItemWithGroupKey" /> objects have different values.</summary>
            /// <param name="left">The first value to compare.</param>
            /// <param name="right">The second value to compare.</param>
            /// <returns>true if <paramref name="left" /> and <paramref name="right" /> are not equal; otherwise, false.</returns>
            public static bool operator !=(ItemWithGroupKey left, ItemWithGroupKey right)
            {
                return !Equals(left, right);
            }

            public bool Equals(ItemWithGroupKey? other)
            {
                if (ReferenceEquals(null, other))
                {
                    return false;
                }

                if (ReferenceEquals(this, other))
                {
                    return true;
                }

                return EqualityComparer<TObject>.Default.Equals(Item, other.Item);
            }

            public override bool Equals(object? obj)
            {
                if (ReferenceEquals(null, obj))
                {
                    return false;
                }

                if (ReferenceEquals(this, obj))
                {
                    return true;
                }

                return obj is ItemWithGroupKey value && Equals(value);
            }

            public override int GetHashCode()
            {
                return Item is null ? 0 : EqualityComparer<TObject>.Default.GetHashCode(Item);
            }

            public override string ToString() => $"{Item} ({Group})";
        }
    }
}