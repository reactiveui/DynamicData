﻿using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;

namespace DynamicData.Tests.List
{
    
    public class CastFixture: IDisposable
    {
        private ISourceList<int> _source;
        private ChangeSetAggregator<decimal> _results;

        [SetUp]
        public void Initialise()
        {
            _source = new SourceList<int>();
            _results = _source.Cast(i=>(decimal)i).AsAggregator();
        }

        public void Dispose()
        {
            _source.Dispose();
            _results.Dispose();
        }

        [Test]
        public void CanCast()
        {
            _source.AddRange(Enumerable.Range(1,10));
            _results.Data.Count.Should().Be(10);

            _source.Clear();
            _results.Data.Count.Should().Be(0);
        }
    }
}