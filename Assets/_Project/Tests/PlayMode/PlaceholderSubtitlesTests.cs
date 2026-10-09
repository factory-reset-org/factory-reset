using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Cutscenes;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The placeholder subtitles show each line with its coloured speaker tag on a canvas that
    /// scales with the screen, with a dynamic font so the text stays sharp, and hide on clear.
    /// </summary>
    public sealed class PlaceholderSubtitlesTests
    {
        GameObject _holder;

        [TearDown]
        public void TearDown()
        {
            if (_holder != null)
                Object.Destroy(_holder);
        }

        [UnityTest]
        public IEnumerator ALineShowsItsTagAndTypedTextThenClears()
        {
            _holder = new GameObject("Subtitles");
            var subtitles = _holder.AddComponent<PlaceholderSubtitles>();
            yield return null;
            Assert.IsFalse(subtitles.IsShowing);

            DialogueEvents.RaiseLineShown(new DialogueLineView("CAPTAIN", Color.red, "I do not chase. I predict.", 8));
            Assert.IsTrue(subtitles.IsShowing);
            StringAssert.Contains("<color=#FF0000>CAPTAIN</color>", subtitles.Shown);
            StringAssert.EndsWith("\nI do not", subtitles.Shown, "Only the characters typed so far.");

            DialogueEvents.RaiseLineCleared();
            Assert.IsFalse(subtitles.IsShowing);
        }

        [UnityTest]
        public IEnumerator TheBarScalesWithTheScreenAndSortsAboveTheLetterbox()
        {
            _holder = new GameObject("Subtitles");
            _holder.AddComponent<PlaceholderSubtitles>();
            yield return null;

            var canvas = _holder.GetComponentInChildren<Canvas>(true);
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
            Assert.Greater(canvas.sortingOrder, 40, "Above the letterbox bars.");
            var scaler = canvas.GetComponent<CanvasScaler>();
            Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
            Text text = _holder.GetComponentInChildren<Text>(true);
            Assert.IsTrue(text.font.dynamic, "Glyphs drawn at their size on screen, not a scaled bitmap.");
        }
    }
}
