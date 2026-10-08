using System;
using System.Collections.Generic;
using NUnit.Framework;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Cutscenes;

namespace ToyFactory.Tests.EditMode
{
    public sealed class DialogueRunnerTests
    {
        readonly List<DialogueLineView> _shown = new List<DialogueLineView>();
        int _cleared;
        Action<DialogueLineView> _onShown;
        Action _onCleared;

        [SetUp]
        public void SetUp()
        {
            _shown.Clear();
            _cleared = 0;
            DialogueEvents.OnLineShown += _onShown = _shown.Add;
            DialogueEvents.OnLineCleared += _onCleared = () => _cleared++;
        }

        [TearDown]
        public void TearDown()
        {
            DialogueEvents.OnLineShown -= _onShown;
            DialogueEvents.OnLineCleared -= _onCleared;
        }

        static DialogueLine Line(string text, DialogueSpeaker speaker = DialogueSpeaker.Pip,
            DialogueCondition when = DialogueCondition.Always) => new DialogueLine(speaker, text, when);

        DialogueLineView Last => _shown[_shown.Count - 1];

        [Test]
        public void LineTypesOutAtThirtyEightCharactersPerSecond()
        {
            var runner = new DialogueRunner();
            runner.Enqueue(new[] { Line(new string('a', 38)) });
            Assert.AreEqual(0, Last.VisibleCharacters, "Starts empty.");

            runner.Tick(0.5f);
            Assert.AreEqual(19, Last.VisibleCharacters);

            runner.Tick(0.5f);
            Assert.IsTrue(Last.IsComplete);
            Assert.AreEqual("PIP, maintenance radio", Last.SpeakerTag);
        }

        [Test]
        public void TypedLineHoldsForOnePointThreeSecondsPlusAFortiethPerCharacter()
        {
            Assert.AreEqual(1.3f + 0.025f * 40, DialogueRunner.HoldSeconds(40), 1e-5f);

            var runner = new DialogueRunner();
            runner.Enqueue(new[] { Line(new string('a', 38)), Line("Next.") });
            runner.Tick(1f);                                   // typed
            runner.Tick(DialogueRunner.HoldSeconds(38) - 0.05f);
            Assert.AreEqual(38, Last.Text.Length, "Still holding the first line.");

            runner.Tick(0.1f);
            Assert.AreEqual("Next.", Last.Text, "The hold is over: the next line starts.");
        }

        [Test]
        public void LastLineClearsTheSubtitleWhenItsHoldEnds()
        {
            var runner = new DialogueRunner();
            runner.Enqueue(new[] { Line("Hi.") });
            runner.Tick(1f);
            runner.Tick(DialogueRunner.HoldSeconds(3) + 0.01f);

            Assert.IsFalse(runner.IsBusy);
            Assert.AreEqual(1, _cleared);
        }

        [Test]
        public void AdvanceFinishesTheTypingThenMovesToTheNextLine()
        {
            var runner = new DialogueRunner();
            runner.Enqueue(new[] { Line("A long line that is still typing."), Line("Second.") });
            runner.Tick(0.1f);

            runner.Advance();
            Assert.IsTrue(Last.IsComplete, "The first press finishes the typing.");
            Assert.AreEqual("A long line that is still typing.", Last.Text);

            runner.Advance();
            Assert.AreEqual("Second.", Last.Text, "The second press moves on.");
        }

        [Test]
        public void ConditionalLinesAreSaidOnlyWhenTheirConditionHolds()
        {
            bool saboteurAScrapped = true;
            var runner = new DialogueRunner(c =>
                c == DialogueCondition.Always ||
                (c == DialogueCondition.IfSaboteurAScrapped && saboteurAScrapped) ||
                (c == DialogueCondition.IfSaboteurAActive && !saboteurAScrapped));
            runner.Enqueue(new[]
            {
                Line("Scrap it.", when: DialogueCondition.IfSaboteurAActive),
                Line("Go grab it.", when: DialogueCondition.IfSaboteurAScrapped),
            });

            Assert.AreEqual("Go grab it.", Last.Text);
            runner.Advance();
            runner.Advance();
            Assert.IsFalse(runner.IsBusy, "The other version was never said.");
        }

        [Test]
        public void ClearDropsEveryLineAndClearsTheSubtitle()
        {
            var runner = new DialogueRunner();
            runner.Enqueue(new[] { Line("One."), Line("Two.") });

            runner.Clear();

            Assert.IsFalse(runner.IsBusy);
            Assert.AreEqual(1, _cleared);
            runner.Clear();
            Assert.AreEqual(1, _cleared, "Clearing an idle runner says nothing.");
        }

        [Test]
        public void EachTypedLetterIsReportedForTheVoiceBlips()
        {
            var runner = new DialogueRunner();
            var typed = new List<DialogueSpeaker>();
            runner.Typed += typed.Add;
            runner.Enqueue(new[] { Line("Go now", DialogueSpeaker.Captain) });

            runner.Tick(1f);

            Assert.AreEqual(5, typed.Count, "Letters only, not the space.");
            Assert.IsTrue(typed.TrueForAll(s => s == DialogueSpeaker.Captain));
        }

        [Test]
        public void SpeakersMatchTheCastTable()
        {
            Assert.AreEqual(BlipWave.Square, DialogueSpeakers.Wave(DialogueSpeaker.FactoryOS));
            Assert.AreEqual(BlipWave.Triangle, DialogueSpeakers.Wave(DialogueSpeaker.Pip));
            Assert.AreEqual(BlipWave.Sawtooth, DialogueSpeakers.Wave(DialogueSpeaker.Captain));
            Assert.AreEqual(BlipWave.Sine, DialogueSpeakers.Wave(DialogueSpeaker.Unit047));
            Assert.Less(DialogueSpeakers.Pitch(DialogueSpeaker.FactoryOS), DialogueSpeakers.Pitch(DialogueSpeaker.Pip), "Factory OS low, Pip high.");
            Assert.AreEqual("CAPTAIN BOT", DialogueSpeakers.Tag(DialogueSpeaker.Captain));
        }

        [Test]
        public void BlipWaveformsHaveTheirShapes()
        {
            Assert.AreEqual(1f, VoiceBlips.Sample(BlipWave.Square, 0.25f));
            Assert.AreEqual(-1f, VoiceBlips.Sample(BlipWave.Square, 0.75f));
            Assert.AreEqual(1f, VoiceBlips.Sample(BlipWave.Triangle, 0.5f), 1e-5f);
            Assert.AreEqual(-1f, VoiceBlips.Sample(BlipWave.Sawtooth, 0f), 1e-5f);
            Assert.AreEqual(1f, VoiceBlips.Sample(BlipWave.Sine, 0.25f), 1e-5f);
        }
    }
}
