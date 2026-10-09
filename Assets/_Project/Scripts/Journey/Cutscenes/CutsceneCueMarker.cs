using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>
    /// A marker on a cutscene Timeline that sounds the alarm or pops a comic word up when the
    /// playhead passes it; the <see cref="CutsceneDirector"/> hands it to its
    /// <see cref="CutsceneCuePlayer"/>. Purely for show: a skipped cutscene simply does not
    /// play it. Put it on the marker track; the Timeline builder places one per
    /// <see cref="CutsceneCuePlan"/> entry.
    /// </summary>
    [DisplayName("Factory Reset/Cue (alarm or comic word)")]
    public sealed class CutsceneCueMarker : Marker, INotification, INotificationOptionProvider
    {
        [SerializeField] CutsceneCueKind kind;
        [SerializeField] ComicWord word;

        [Tooltip("The word pops up above the Unit 047 stand-in (Point is an offset) instead of at a world point.")]
        [SerializeField] bool onActor;

        [SerializeField] Vector3 point;

        public CutsceneCueKind Kind => kind;
        public ComicWord Word => word;
        public bool OnActor => onActor;
        public Vector3 Point => point;

        public PropertyName id => new PropertyName("Cue" + kind + word);

        // Once per play; a cue the playhead jumped past is not worth playing late.
        NotificationFlags INotificationOptionProvider.flags => NotificationFlags.TriggerOnce;

        /// <summary>Sets the marker up from a plan entry, for the Timeline builder and tests.</summary>
        public void Configure(CutsceneCue cue)
        {
            kind = cue.Kind;
            word = cue.Word;
            onActor = cue.OnActor;
            point = cue.Point;
        }
    }
}
