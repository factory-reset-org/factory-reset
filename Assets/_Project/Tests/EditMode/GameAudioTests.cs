using NUnit.Framework;
using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// The pause menu's volume settings: kept between 0 and 1, saved for the next run, and
    /// announced when they change so the music can follow at once.
    /// </summary>
    public class GameAudioTests
    {
        float _sound;
        float _music;

        [SetUp]
        public void Keep()
        {
            _sound = GameAudio.SoundVolume;
            _music = GameAudio.MusicVolume;
        }

        [TearDown]
        public void Restore()
        {
            GameAudio.SoundVolume = _sound;
            GameAudio.MusicVolume = _music;
        }

        [Test]
        public void TheVolumesStayBetweenNothingAndFull()
        {
            GameAudio.SoundVolume = 1.7f;
            Assert.AreEqual(1f, GameAudio.SoundVolume);
            GameAudio.MusicVolume = -0.3f;
            Assert.AreEqual(0f, GameAudio.MusicVolume);
        }

        [Test]
        public void TheVolumesAreSavedForTheNextRun()
        {
            GameAudio.SoundVolume = 0.4f;
            GameAudio.MusicVolume = 0.7f;
            Assert.AreEqual(0.4f, PlayerPrefs.GetFloat("FactoryReset.SoundVolume"), 1e-5f);
            Assert.AreEqual(0.7f, PlayerPrefs.GetFloat("FactoryReset.MusicVolume"), 1e-5f);
        }

        [Test]
        public void AChangeIsAnnouncedOnce()
        {
            GameAudio.MusicVolume = 0.5f;
            int heard = 0;
            void Count() => heard++;
            GameAudio.Changed += Count;
            try
            {
                GameAudio.MusicVolume = 0.25f;
                GameAudio.MusicVolume = 0.25f;   // the same value again is no change
            }
            finally
            {
                GameAudio.Changed -= Count;
            }
            Assert.AreEqual(1, heard);
        }
    }
}
