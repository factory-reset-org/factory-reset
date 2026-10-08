using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>
    /// Marks where a shot's lines should be over, so the camera move and the dialogue stay
    /// together. Paired with a <see cref="DialogueMarker"/> that does not wait: the shot's
    /// lines start at that marker and the camera keeps moving while they are said.
    /// </summary>
    /// <remarks>
    /// When the playhead reaches this marker with a line still on screen, the
    /// <see cref="CutsceneDirector"/> holds the Timeline until the line is finished. When the
    /// lines finish first (the player clicked through them), the director moves the playhead
    /// straight here, so the camera never lingers on an empty subtitle. Put it just before the
    /// next shot's dialogue marker, on the marker track.
    /// </remarks>
    [DisplayName("Factory Reset/Shot End")]
    public sealed class ShotEndMarker : Marker, INotification, INotificationOptionProvider
    {
        public PropertyName id => new PropertyName("ShotEnd");

        NotificationFlags INotificationOptionProvider.flags => NotificationFlags.TriggerOnce;
    }
}
