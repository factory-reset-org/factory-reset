using System;
using System.Reflection;
using NUnit.Framework;
using ToyFactory.Interfaces;
using UnityEngine;

namespace ToyFactory.Tests
{
    public sealed class ChapterEventsTests
    {
        static readonly MethodInfo ClearListenersMethod = typeof(ChapterEvents).GetMethod(
            "ClearListeners", BindingFlags.Static | BindingFlags.NonPublic);

        [SetUp]
        public void SetUp()
        {
            ClearListeners();
        }

        [TearDown]
        public void TearDown()
        {
            ClearListeners();
        }

        [Test]
        public void JourneyConstantsMatchThePlan()
        {
            Assert.That(ChapterEvents.ChapterCount, Is.EqualTo(4));
            Assert.That(ChapterEvents.SwitchCount, Is.EqualTo(3));
            Assert.That(ChapterEvents.ConsoleTaskId, Is.EqualTo("console"));
        }

        [TestCase(1)]
        [TestCase(4)]
        public void RaiseChapterStartedForwardsTheChapter(int chapter)
        {
            int received = 0;
            ChapterEvents.OnChapterStarted += value => received = value;

            ChapterEvents.RaiseChapterStarted(chapter);

            Assert.That(received, Is.EqualTo(chapter));
        }

        [TestCase("lever")]
        [TestCase(ChapterEvents.ConsoleTaskId)]
        public void RaiseTaskCompletedForwardsTheTaskId(string taskId)
        {
            string received = null;
            ChapterEvents.OnTaskCompleted += value => received = value;

            ChapterEvents.RaiseTaskCompleted(taskId);

            Assert.That(received, Is.EqualTo(taskId));
        }

        [TestCase(1)]
        [TestCase(3)]
        public void RaiseSwitchRestoredForwardsTheSwitchNumber(int switchNumber)
        {
            int received = 0;
            ChapterEvents.OnSwitchRestored += value => received = value;

            ChapterEvents.RaiseSwitchRestored(switchNumber);

            Assert.That(received, Is.EqualTo(switchNumber));
        }

        [Test]
        public void EachRaiseReachesOnlyItsOwnEvent()
        {
            int chapterCalls = 0;
            int taskCalls = 0;
            int switchCalls = 0;
            ChapterEvents.OnChapterStarted += _ => chapterCalls++;
            ChapterEvents.OnTaskCompleted += _ => taskCalls++;
            ChapterEvents.OnSwitchRestored += _ => switchCalls++;

            ChapterEvents.RaiseSwitchRestored(2);

            Assert.That(chapterCalls, Is.Zero);
            Assert.That(taskCalls, Is.Zero);
            Assert.That(switchCalls, Is.EqualTo(1));
        }

        [Test]
        public void RaiseNotifiesEverySubscriberSynchronously()
        {
            int firstCalls = 0;
            int secondCalls = 0;
            ChapterEvents.OnChapterStarted += _ => firstCalls++;
            ChapterEvents.OnChapterStarted += _ => secondCalls++;

            ChapterEvents.RaiseChapterStarted(2);

            Assert.That(firstCalls, Is.EqualTo(1));
            Assert.That(secondCalls, Is.EqualTo(1));
        }

        [Test]
        public void RaisingWithoutSubscribersDoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                ChapterEvents.RaiseChapterStarted(1);
                ChapterEvents.RaiseTaskCompleted("lever");
                ChapterEvents.RaiseSwitchRestored(1);
            });
        }

        [Test]
        public void RemovedSubscriberIsNotNotified()
        {
            int calls = 0;
            Action<int> listener = _ => calls++;
            ChapterEvents.OnSwitchRestored += listener;
            ChapterEvents.OnSwitchRestored -= listener;

            ChapterEvents.RaiseSwitchRestored(1);

            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void SubsystemRegistrationCleanupRemovesAllListeners()
        {
            int calls = 0;
            ChapterEvents.OnChapterStarted += _ => calls++;
            ChapterEvents.OnTaskCompleted += _ => calls++;
            ChapterEvents.OnSwitchRestored += _ => calls++;

            ClearListeners();
            ChapterEvents.RaiseChapterStarted(1);
            ChapterEvents.RaiseTaskCompleted("lever");
            ChapterEvents.RaiseSwitchRestored(1);

            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void CleanupUsesSubsystemRegistration()
        {
            var attribute = ClearListenersMethod.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();

            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.loadType,
                Is.EqualTo(RuntimeInitializeLoadType.SubsystemRegistration));
        }

        static void ClearListeners()
        {
            Assert.That(ClearListenersMethod, Is.Not.Null);
            ClearListenersMethod.Invoke(null, null);
        }
    }
}
