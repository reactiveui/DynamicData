// Copyright (c) 2011-2020 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using DynamicData.List.Internal;

// ReSharper disable once CheckNamespace
namespace DynamicData
{
    /// <summary>
    /// An editable observable list.
    /// </summary>
    /// <typeparam name="T">The type of the object.</typeparam>
    public sealed class SourceList<T> : ISourceList<T>
    {
        private readonly ISubject<IChangeSet<T>> _changes = new Subject<IChangeSet<T>>();

        private readonly Subject<IChangeSet<T>> _changesPreview = new Subject<IChangeSet<T>>();

        private readonly IDisposable _cleanUp;

        private readonly Lazy<ISubject<int>> _countChanged = new Lazy<ISubject<int>>(() => new Subject<int>());

        private readonly object _locker = new object();

        private readonly ReaderWriter<T> _readerWriter = new ReaderWriter<T>();

        private int _editLevel;

        /// <summary>
        /// Initializes a new instance of the <see cref="SourceList{T}"/> class.
        /// </summary>
        /// <param name="source">The source.</param>
        public SourceList(IObservable<IChangeSet<T>>? source = null)
        {
            var loader = source is null ? Disposable.Empty : LoadFromSource(source);

            _cleanUp = Disposable.Create(
                () =>
                    {
                        loader.Dispose();
                        OnCompleted();
                        if (_countChanged.IsValueCreated)
                        {
                            _countChanged.Value.OnCompleted();
                        }
                    });
        }

        /// <inheritdoc />
        public int Count => _readerWriter.Count;

        /// <inheritdoc />
        public IObservable<int> CountChanged =>
            Observable.Create<int>(
                observer =>
                    {
                        lock (_locker)
                        {
                            var source = _countChanged.Value.StartWith(_readerWriter.Count).DistinctUntilChanged();
                            return source.SubscribeSafe(observer);
                        }
                    });

        /// <inheritdoc />
        public IEnumerable<T> Items => _readerWriter.Items;

        /// <inheritdoc />
        public IObservable<IChangeSet<T>> Connect(Func<T, bool>? predicate = null)
        {
            var observable = Observable.Create<IChangeSet<T>>(
                observer =>
                    {
                        lock (_locker)
                        {
                            if (_readerWriter.Items.Length > 0)
                            {
                                observer.OnNext(
                                    new ChangeSet<T>()
                                        {
                                            new Change<T>(ListChangeReason.AddRange, _readerWriter.Items)
                                        });
                            }

                            var source = _changes.Finally(observer.OnCompleted);

                            return source.SubscribeSafe(observer);
                        }
                    });

            if (predicate is not null)
            {
                observable = new FilterStatic<T>(observable, predicate).Run();
            }

            return observable;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _cleanUp.Dispose();
            _changesPreview?.Dispose();
        }

        /// <inheritdoc />
        public void Edit(Action<IExtendedList<T>> updateAction)
        {
            if (updateAction is null)
            {
                throw new ArgumentNullException(nameof(updateAction));
            }

            lock (_locker)
            {
                IChangeSet<T>? changes = null;

                _editLevel++;

                if (_editLevel == 1)
                {
                    if (_changesPreview.HasObservers)
                    {
                        changes = _readerWriter.WriteWithPreview(updateAction, InvokeNextPreview);
                    }
                    else
                    {
                        changes = _readerWriter.Write(updateAction);
                    }
                }
                else
                {
                    _readerWriter.WriteNested(updateAction);
                }

                _editLevel--;

                if (changes is not null && _editLevel == 0)
                {
                    InvokeNext(changes);
                }
            }
        }

        /// <inheritdoc />
        public IObservable<IChangeSet<T>> Preview(Func<T, bool>? predicate = null)
        {
            IObservable<IChangeSet<T>> observable = _changesPreview;

            if (predicate is not null)
            {
                observable = new FilterStatic<T>(observable, predicate).Run();
            }

            return observable;
        }

        private void InvokeNext(IChangeSet<T> changes)
        {
            if (changes.Count == 0)
            {
                return;
            }

            lock (_locker)
            {
                _changes.OnNext(changes);

                if (_countChanged.IsValueCreated)
                {
                    _countChanged.Value.OnNext(_readerWriter.Count);
                }
            }
        }

        private void InvokeNextPreview(IChangeSet<T> changes)
        {
            if (changes.Count == 0)
            {
                return;
            }

            lock (_locker)
            {
                _changesPreview.OnNext(changes);
            }
        }

        private IDisposable LoadFromSource(IObservable<IChangeSet<T>> source)
        {
            return source.Synchronize(_locker).Finally(OnCompleted).Select(_readerWriter.Write).Subscribe(InvokeNext, OnError, OnCompleted);
        }

        private void OnCompleted()
        {
            lock (_locker)
            {
                _changesPreview.OnCompleted();
                _changes.OnCompleted();
            }
        }

        private void OnError(Exception exception)
        {
            lock (_locker)
            {
                _changesPreview.OnError(exception);
                _changes.OnError(exception);
            }
        }
    }
}