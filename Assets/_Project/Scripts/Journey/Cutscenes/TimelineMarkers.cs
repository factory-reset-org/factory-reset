using System.Collections.Generic;
using UnityEngine.Timeline;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>
    /// Finds markers on a cutscene Timeline by time, for moving the playhead ahead without
    /// losing what lies in between.
    /// </summary>
    public static class TimelineMarkers
    {
        /// <summary>
        /// Every marker of <paramref name="timeline"/> (the marker track and every other track)
        /// later than <paramref name="after"/> and no later than <paramref name="upTo"/>,
        /// sorted by time.
        /// </summary>
        public static List<IMarker> Between(TimelineAsset timeline, double after, double upTo)
        {
            var found = new List<IMarker>();
            if (timeline == null)
                return found;

            if (timeline.markerTrack != null)
                Collect(timeline.markerTrack, after, upTo, found);
            foreach (TrackAsset track in timeline.GetOutputTracks())
                if (track != timeline.markerTrack)
                    Collect(track, after, upTo, found);

            found.Sort((a, b) => a.time.CompareTo(b.time));
            return found;
        }

        /// <summary>The time of the first <see cref="ShotEndMarker"/> later than <paramref name="after"/>.</summary>
        public static bool NextShotEnd(TimelineAsset timeline, double after, out double time)
        {
            time = 0;
            bool found = false;
            foreach (IMarker marker in Between(timeline, after, double.MaxValue))
            {
                if (marker is ShotEndMarker)
                {
                    time = marker.time;
                    found = true;
                    break;
                }
            }
            return found;
        }

        static void Collect(TrackAsset track, double after, double upTo, List<IMarker> found)
        {
            foreach (IMarker marker in track.GetMarkers())
                if (marker.time > after && marker.time <= upTo)
                    found.Add(marker);
        }
    }
}
