using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using ToyFactory.Interfaces;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>
    /// A marker on a cutscene Timeline for a change the game must not miss (the Captain
    /// waking, the Control Room doors unlocking). When the playhead passes it, the
    /// <see cref="CutsceneDirector"/> on the same object raises
    /// <see cref="CutsceneEvents.OnCriticalSignal"/>. Put it on the Timeline's marker track.
    /// </summary>
    /// <remarks>
    /// The id should be one of the <see cref="CutsceneSignals"/> constants and also be
    /// listed in the cutscene's Critical signals, so a skip still fires it.
    /// </remarks>
    [DisplayName("Factory Reset/Critical Signal")]
    public sealed class CriticalSignalMarker : Marker, INotification, INotificationOptionProvider
    {
        [Tooltip("A CutsceneSignals id: CaptainWake, ControlRoomUnlock or CoreShieldsDown.")]
        [SerializeField] string signalId = CutsceneSignals.CaptainWake;

        /// <summary>The <see cref="CutsceneSignals"/> id this marker fires.</summary>
        public string SignalId => signalId;

        public PropertyName id => new PropertyName(signalId);

        // Once per play, and still sent if playback starts past the marker.
        NotificationFlags INotificationOptionProvider.flags =>
            NotificationFlags.TriggerOnce | NotificationFlags.Retroactive;
    }
}
