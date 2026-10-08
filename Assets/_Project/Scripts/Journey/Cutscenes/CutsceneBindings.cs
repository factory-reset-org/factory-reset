using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using ToyFactory.Interfaces;
using Object = UnityEngine.Object;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>
    /// Binds a Timeline's tracks to objects in other scenes when a cutscene starts. Timelines
    /// live in the Agents scene and cannot keep a reference to a door in Interactables or a
    /// lamp in Env, so a track that should drive one is left unbound and named after the
    /// object's <see cref="CutsceneBindingId"/> (a track named "AlarmDoor3" drives the object
    /// with that id). Cinemachine tracks are bound to the brain on the gameplay camera.
    /// </summary>
    /// <remarks>
    /// Tracks already bound in the Agents scene (the Unit 047 actor, shot cameras) are left as
    /// they are. The lookup runs once per cutscene, never per frame. Only enabled objects are
    /// registered, so an object a cutscene switches on should be bound through an active parent.
    /// </remarks>
    public static class CutsceneBindings
    {
        /// <summary>
        /// Binds every unbound track of the director's Timeline by its name, and every
        /// Cinemachine track to <paramref name="brain"/>. Returns the names that found no object.
        /// </summary>
        public static List<string> Resolve(PlayableDirector director, CinemachineBrain brain)
        {
            var missing = new List<string>();
            if (director == null || director.playableAsset == null)
                return missing;

            foreach (PlayableBinding output in director.playableAsset.outputs)
            {
                if (!(output.sourceObject is TrackAsset track) || output.outputTargetType == null)
                    continue;

                if (track is CinemachineTrack)
                {
                    if (brain != null)
                        director.SetGenericBinding(track, brain);
                    continue;
                }

                if (director.GetGenericBinding(track) != null)
                    continue;

                Object bound = CutsceneBindingId.TryFind(track.name, out CutsceneBindingId target)
                    ? BindingFor(target.gameObject, output.outputTargetType)
                    : null;
                if (bound != null)
                    director.SetGenericBinding(track, bound);
                else
                    missing.Add(track.name);
            }
            return missing;
        }

        /// <summary>The object a track of <paramref name="type"/> drives on <paramref name="target"/>.</summary>
        public static Object BindingFor(GameObject target, Type type)
        {
            if (target == null || type == null)
                return null;
            if (type == typeof(GameObject))
                return target;
            if (!typeof(Component).IsAssignableFrom(type))
                return null;
            // Explicit null checks: a missing component is Unity's fake null, which ?? does not see.
            Component own = target.GetComponent(type);
            if (own != null)
                return own;
            Component child = target.GetComponentInChildren(type, true);
            return child != null ? child : null;
        }
    }
}
