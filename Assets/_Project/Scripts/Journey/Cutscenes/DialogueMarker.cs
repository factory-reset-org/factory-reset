using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>
    /// A marker on a cutscene Timeline that starts one shot's lines from the cutscene's
    /// <see cref="DialogueScript"/> when the playhead passes it. With "Wait For Lines" on, the
    /// Timeline holds there until those lines are finished, so the camera never cuts away in
    /// the middle of a line, however fast or slow the player reads. Put it on the marker track.
    /// </summary>
    [DisplayName("Factory Reset/Dialogue Shot")]
    public sealed class DialogueMarker : Marker, INotification, INotificationOptionProvider
    {
        [Tooltip("Which shot of the cutscene's dialogue script to say (0 = the first).")]
        [SerializeField, Min(0)] int shot;

        [Tooltip("Hold the Timeline here until the shot's lines are finished.")]
        [SerializeField] bool waitForLines = true;

        /// <summary>The shot of the dialogue script this marker starts.</summary>
        public int Shot => shot;

        /// <summary>True if the Timeline should wait for the lines to finish.</summary>
        public bool WaitForLines => waitForLines;

        public PropertyName id => new PropertyName("DialogueShot" + shot);

        NotificationFlags INotificationOptionProvider.flags => NotificationFlags.TriggerOnce;

        /// <summary>Sets the marker up from code, for tests and the Timeline builder.</summary>
        public void Configure(int shotIndex, bool wait)
        {
            shot = shotIndex;
            waitForLines = wait;
        }
    }
}
