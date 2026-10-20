using System;
using NUnit.Framework;
using ToyFactory.AI.Core.Blackboard;

namespace ToyFactory.Tests
{
    public sealed class WorldBlackboardChapterTests
    {
        [Test]
        public void NewBlackboardStartsBeforeTheJourney()
        {
            var blackboard = new WorldBlackboard();

            Assert.That(blackboard.ChapterIndex, Is.Zero);
            Assert.That(blackboard.IsFinalChapter, Is.False);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void SetChapterIndexStoresTheValue(int chapter)
        {
            var blackboard = new WorldBlackboard();

            blackboard.SetChapterIndex(chapter);

            Assert.That(blackboard.ChapterIndex, Is.EqualTo(chapter));
        }

        [TestCase(1, false)]
        [TestCase(3, false)]
        [TestCase(4, true)]
        public void OnlyTheLastChapterIsFinal(int chapter, bool expected)
        {
            var blackboard = new WorldBlackboard();

            blackboard.SetChapterIndex(chapter);

            Assert.That(blackboard.IsFinalChapter, Is.EqualTo(expected));
        }

        [TestCase(-1)]
        [TestCase(5)]
        public void OutOfRangeChapterThrowsAndKeepsTheOldValue(int chapter)
        {
            var blackboard = new WorldBlackboard();
            blackboard.SetChapterIndex(2);

            Assert.Throws<ArgumentOutOfRangeException>(() => blackboard.SetChapterIndex(chapter));
            Assert.That(blackboard.ChapterIndex, Is.EqualTo(2));
        }

        [Test]
        public void ChangingTheChapterDoesNotTouchObjectives()
        {
            var blackboard = new WorldBlackboard();

            blackboard.SetChapterIndex(3);

            Assert.That(blackboard.ObjectivesVersion, Is.Zero);
            Assert.That(blackboard.ObjectiveTargets, Is.Empty);
        }

        [Test]
        public void FinalChapterMatchesTheJourneyLength()
        {
            Assert.That(WorldBlackboard.FinalChapter, Is.EqualTo(4));
        }
    }
}
