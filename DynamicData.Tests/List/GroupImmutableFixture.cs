﻿using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Subjects;
using DynamicData.Tests.Domain;
using NUnit.Framework;
using DynamicData.Kernel;
using FluentAssertions;

namespace DynamicData.Tests.List
{
    
    public class GroupImmutableFixture: IDisposable
    {
        private ISourceList<Person> _source;
        private ChangeSetAggregator<DynamicData.List.IGrouping<Person, int>> _results;
        private ISubject<Unit> _regrouper;
        [SetUp]
        public void Initialise()
        {
            _source = new SourceList<Person>();
            _regrouper = new Subject<Unit>();
            _results = _source.Connect().GroupWithImmutableState(p => p.Age, _regrouper).AsAggregator();
        }

        public void Dispose()
        {
            _source.Dispose();
            _results.Dispose();
        }
        
        [Test]
        public void Add()
        {

            _source.Add(new Person("Person1", 20));
            _results.Data.Count.Should().Be(1, "Should be 1 add");
            _results.Messages.First().Adds.Should().Be(1);
        }

        [Test]
        public void UpdatesArePermissible()
        {
            _source.Add(new Person("Person1", 20));
            _source.Add(new Person("Person2", 20));

            _results.Data.Count.Should().Be(1);//1 group
            _results.Messages.First().Adds.Should().Be(1);
            _results.Messages.Skip(1).First().Replaced.Should().Be(1);

            var group = _results.Data.Items.First();
            group.Count.Should().Be(2);
        }

        [Test]
        public void UpdateAnItemWillChangedThegroup()
        {
            var person1 = new Person("Person1", 20);
            _source.Add(person1);
            _source.Replace(person1, new Person("Person1", 21));

            _results.Data.Count.Should().Be(1);
            _results.Messages.First().Adds.Should().Be(1);
            _results.Messages.Skip(1).First().Adds.Should().Be(1);
            _results.Messages.Skip(1).First().Removes.Should().Be(1);
            var group = _results.Data.Items.First();
            group.Count.Should().Be(1);

            group.Key.Should().Be(21);
        }

        [Test]
        public void Remove()
        {
            var person = new Person("Person1", 20);
            _source.Add(person);
            _source.Remove(person);

            _results.Messages.Count.Should().Be(2);
            _results.Data.Count.Should().Be(0);
        }

        [Test]
        public void FiresManyValueForBatchOfDifferentAdds()
        {
            _source.Edit(updater =>
            {
                updater.Add(new Person("Person1", 20));
                updater.Add(new Person("Person2", 21));
                updater.Add(new Person("Person3", 22));
                updater.Add(new Person("Person4", 23));
            });

            _results.Data.Count.Should().Be(4);
            _results.Messages.Count.Should().Be(1);
            _results.Messages.First().Count.Should().Be(1);
            foreach (var update in _results.Messages.First())
            {
                update.Reason.Should().Be(ListChangeReason.AddRange);
            }
        }

        [Test]
        public void FiresOnlyOnceForABatchOfUniqueValues()
        {
            _source.Edit(updater =>
            {
                updater.Add(new Person("Person1", 20));
                updater.Add(new Person("Person2", 20));
                updater.Add(new Person("Person3", 20));
                updater.Add(new Person("Person4", 20));
            });

            _results.Messages.Count.Should().Be(1);
            _results.Messages.First().Adds.Should().Be(1);
            _results.Data.Items.First().Count.Should().Be(4);
        }

        [Test]
        public void ChanegMultipleGroups()
        {
            var initialPeople = Enumerable.Range(1, 10000)
                .Select(i => new Person("Person" + i, i % 10))
                .ToArray();

            _source.AddRange(initialPeople);

            initialPeople.GroupBy(p => p.Age)
                .ForEach(group =>
                {
                    var grp = _results.Data.Items.First(g=> g.Key.Equals(group.Key));
                    grp.Items.ShouldAllBeEquivalentTo(group.ToArray());
                });

            _source.RemoveMany(initialPeople.Take(15));

            initialPeople.Skip(15)
                .GroupBy(p => p.Age)
                .ForEach(group =>
                {
                    var list = _results.Data.Items.First(p => p.Key == group.Key);
                    list.Items.ShouldAllBeEquivalentTo(group);
                });

            _results.Messages.Count.Should().Be(2);
            _results.Messages.First().Adds.Should().Be(10);
            _results.Messages.Skip(1).First().Replaced.Should().Be(10);
        }

        [Test]
        public void Reevaluate()
        {
            var initialPeople = Enumerable.Range(1, 10)
                .Select(i => new Person("Person" + i, i % 2))
                .ToArray();

            _source.AddRange(initialPeople);
            _results.Messages.Count.Should().Be(1);

            //do an inline update
            foreach (var person in initialPeople)
                person.Age = person.Age + 1;

            //signal operators to evaluate again
            _regrouper.OnNext();

            initialPeople.GroupBy(p => p.Age)
                .ForEach(groupContainer =>
                {
                    var grouping = _results.Data.Items.First(g => g.Key == groupContainer.Key);
                    grouping.Items.ShouldAllBeEquivalentTo(groupContainer);

                });

            _results.Data.Count.Should().Be(2);
            _results.Messages.Count.Should().Be(2);

            var secondMessage = _results.Messages.Skip(1).First();
            secondMessage.Removes.Should().Be(1);
            secondMessage.Replaced.Should().Be(1);
            secondMessage.Adds.Should().Be(1);
        }
    }
}