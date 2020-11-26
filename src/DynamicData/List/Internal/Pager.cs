// Copyright (c) 2011-2020 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;

using DynamicData.Kernel;

namespace DynamicData.List.Internal
{
    internal class Pager<T>
    {
        private readonly IObservable<IPageRequest> _requests;

        private readonly IObservable<IChangeSet<T>> _source;

        public Pager(IObservable<IChangeSet<T>> source, IObservable<IPageRequest> requests)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
        }

        public IObservable<IPageChangeSet<T>> Run()
        {
            return Observable.Create<IPageChangeSet<T>>(
                observer =>
                    {
                        var locker = new object();
                        var all = new List<T>();
                        var paged = new ChangeAwareList<T>();

                        IPageRequest parameters = new PageRequest(0, 25);

                        var requestStream = _requests.Synchronize(locker).Select(
                            request =>
                                {
                                    parameters = request;
                                    return CheckParametersAndPage(all, paged, request);
                                });

                        var dataChanged = _source
                            .Synchronize(locker)
                            .Select(changes => Page(all, paged, parameters, changes));

                        return requestStream
                            .Merge(dataChanged)
                            .Where(changes => changes is not null && changes.Count != 0)
                            .Select(x => x!)
                            .SubscribeSafe(observer);
                    });
        }

        private static int CalculatePages(ICollection all, IPageRequest? request)
        {
            if (request is null || request.Size >= all.Count || request.Size == 0)
            {
                return 1;
            }

            int pages = all.Count / request.Size;
            int overlap = all.Count % request.Size;

            if (overlap == 0)
            {
                return pages;
            }

            return pages + 1;
        }

        private static PageChangeSet<T>? CheckParametersAndPage(List<T> all, ChangeAwareList<T> paged, IPageRequest? request)
        {
            if (request is null || request.Page < 0 || request.Size < 1)
            {
                return null;
            }

            return Page(all, paged, request);
        }

        private static PageChangeSet<T> Page(List<T> all, ChangeAwareList<T> paged, IPageRequest request, IChangeSet<T>? changeset = null)
        {
            if (changeset is not null)
            {
                all.Clone(changeset);
            }

            var previous = paged;

            int pages = CalculatePages(all, request);
            int page = request.Page > pages ? pages : request.Page;
            int skip = request.Size * (page - 1);

            var current = all.Skip(skip)
                              .Take(request.Size)
                              .ToList();

            var adds = current.Except(previous);
            var removes = previous.Except(current);

            paged.RemoveMany(removes);

            adds.ForEach(t =>
            {
                var index = current.IndexOf(t);
                paged.Insert(index, t);
            });

            var startIndex = skip;

            var moves = changeset.EmptyIfNull()
                                 .Where(change => change.Reason == ListChangeReason.Moved
                                                  && change.MovedWithinRange(startIndex, startIndex + request.Size));

            foreach (var change in moves)
            {
                // check whether an item has moved within the same page
                var currentIndex = change.Item.CurrentIndex - startIndex;
                var previousIndex = change.Item.PreviousIndex - startIndex;
                paged.Move(previousIndex, currentIndex);
            }

            // find replaces [Is this ever the case that it can be reached]
            for (int i = 0; i < current.Count; i++)
            {
                var currentItem = current[i];
                var previousItem = previous[i];

                if (ReferenceEquals(currentItem, previousItem))
                {
                    continue;
                }

                var index = paged.IndexOf(currentItem);
                paged.Move(i, index);
            }

            var changed = paged.CaptureChanges();

            return new PageChangeSet<T>(changed, new PageResponse(paged.Count, page, all.Count, pages));
        }
    }
}