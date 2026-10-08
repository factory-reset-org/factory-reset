using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using ToyFactory.Journey.Cutscenes;

namespace ToyFactory.Editor.Cutscenes
{
    /// <summary>
    /// Builds the five cutscene Timelines from <see cref="CutsceneShotPlan"/> and the dialogue
    /// scripts, and the cameras they use in the Agents scene. Open <c>Agents.unity</c>, run
    /// the menu item, save the scene. Running it again rebuilds everything in place, so a
    /// changed shot or line is one click away and the Timelines' asset references stay valid.
    /// </summary>
    /// <remarks>
    /// Each shot becomes two Cinemachine cameras (the start and end pose) and two overlapping
    /// clips on the Timeline's Cinemachine track, so the camera blends from one pose to the
    /// other while the shot's lines are said. On the marker track each shot gets a
    /// <see cref="DialogueMarker"/> that does not wait (the camera keeps moving), its Critical
    /// signals, and a <see cref="ShotEndMarker"/> just before the next shot. A shot lasts as
    /// long as its lines take when nobody clicks. The Cinemachine track is bound to the
    /// gameplay camera's brain when the cutscene starts, so it is left unbound here.
    /// </remarks>
    public static class CutsceneTimelineBuilder
    {
        const string Folder = "Assets/_Project/Data/Cutscenes";
        const string ActorPrefab = "Assets/_Project/Prefabs/Characters/Unit047.prefab";
        const string CamerasName = "Shot Cameras";
        const string ActorName = "Unit 047 (cutscene)";

        // Each shot's start camera is alone for this long at the start, its end camera at the end.
        const double SoloSeconds = 0.1;

        // The shot end sits this far before the next shot's dialogue marker, so the two never
        // fire on the same frame in an order that would hold the next shot's lines.
        const double EndLead = 0.05;

        [MenuItem("Factory Reset/Cutscenes/Build Timelines")]
        public static void BuildAll()
        {
            CutsceneDirector director = Object.FindAnyObjectByType<CutsceneDirector>(FindObjectsInactive.Include);
            if (director == null)
            {
                Debug.LogError("Open the Agents scene first: no CutsceneDirector is loaded.");
                return;
            }
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/_Project/Data", "Cutscenes");

            PlayableDirector playable = director.GetComponent<PlayableDirector>();
            ClearExposedReferences(playable);
            Transform actor = BuildActor(director.transform);
            Transform cameras = Recreate(director.transform, CamerasName);

            var serialized = new SerializedObject(director);
            SerializedProperty cutscenes = serialized.FindProperty("cutscenes");
            int built = 0;
            for (int i = 0; i < cutscenes.arraySize; i++)
            {
                SerializedProperty definition = cutscenes.GetArrayElementAtIndex(i);
                string id = definition.FindPropertyRelative("id").stringValue;
                List<CutsceneShot> shots = CutsceneShotPlan.For(id);
                var script = definition.FindPropertyRelative("dialogue").objectReferenceValue as DialogueScript;
                if (shots.Count == 0 || script == null)
                {
                    Debug.LogWarning($"Cutscene \"{id}\": no shot plan or no dialogue script, so no Timeline was built.");
                    continue;
                }
                if (shots.Count != script.ShotCount)
                {
                    Debug.LogError($"Cutscene \"{id}\": the shot plan has {shots.Count} shots but the dialogue script has {script.ShotCount}.");
                    continue;
                }

                definition.FindPropertyRelative("timeline").objectReferenceValue =
                    BuildTimeline(id, shots, script, playable, actor, cameras);
                built++;
            }

            serialized.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"Built {built} cutscene Timelines in {Folder}. Save the Agents scene to keep the cameras.");
        }

        static TimelineAsset BuildTimeline(string id, List<CutsceneShot> shots, DialogueScript script,
            PlayableDirector playable, Transform actor, Transform cameras)
        {
            string path = $"{Folder}/{id}.playable";
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            if (timeline == null)
            {
                timeline = ScriptableObject.CreateInstance<TimelineAsset>();
                AssetDatabase.CreateAsset(timeline, path);
            }

            foreach (TrackAsset old in new List<TrackAsset>(timeline.GetRootTracks()))
                timeline.DeleteTrack(old);
            timeline.CreateMarkerTrack();
            foreach (IMarker old in new List<IMarker>(timeline.markerTrack.GetMarkers()))
                timeline.markerTrack.DeleteMarker(old);

            var cameraTrack = timeline.CreateTrack<CinemachineTrack>(null, "Camera");
            var group = new GameObject(id).transform;
            group.SetParent(cameras, false);

            double start = 0;
            foreach (CutsceneShot shot in shots)
            {
                double end = start + DialogueRunner.ShotSeconds(script.LinesOf(shot.Index)) + CutsceneShotPlan.ShotPadding;

                CinemachineCamera from = MakeCamera(group, shot, true, actor);
                CinemachineCamera to = MakeCamera(group, shot, false, actor);
                TimelineClip first = AddShot(cameraTrack, playable, from, shot.FromCameraName, start, end - SoloSeconds);
                TimelineClip second = AddShot(cameraTrack, playable, to, shot.ToCameraName, start + SoloSeconds, end);
                double blend = end - start - 2 * SoloSeconds;
                first.blendOutDuration = blend;
                second.blendInDuration = blend;

                timeline.markerTrack.CreateMarker<DialogueMarker>(start).Configure(shot.Index, false);
                foreach (string signal in shot.Signals)
                    timeline.markerTrack.CreateMarker<CriticalSignalMarker>(start).Configure(signal);
                timeline.markerTrack.CreateMarker<ShotEndMarker>(end - EndLead);

                start = end;
            }

            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = start;
            EditorUtility.SetDirty(timeline);
            return timeline;
        }

        static TimelineClip AddShot(CinemachineTrack track, PlayableDirector playable, CinemachineCamera camera,
            string exposedName, double start, double end)
        {
            TimelineClip clip = track.CreateClip<CinemachineShot>();
            clip.start = start;
            clip.duration = end - start;
            clip.displayName = camera.name;
            var shot = (CinemachineShot)clip.asset;
            shot.VirtualCamera.exposedName = exposedName;
            playable.SetReferenceValue(exposedName, camera);
            return clip;
        }

        static CinemachineCamera MakeCamera(Transform group, CutsceneShot shot, bool isStart, Transform actor)
        {
            string pose = isStart ? "from" : "to";
            var go = new GameObject($"{shot.Index + 1} {pose}: {shot.Subject}");
            go.transform.SetParent(group, false);

            CinemachineCamera camera = go.AddComponent<CinemachineCamera>();
            LensSettings lens = camera.Lens;
            lens.FieldOfView = shot.FieldOfView;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 500f;
            camera.Lens = lens;

            Vector3 position = isStart ? shot.From : shot.To;
            Vector3 look = isStart ? shot.LookFrom : shot.LookTo;
            if (shot.Framing == ShotFraming.World)
            {
                go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(look - position));
                return camera;
            }

            // Follow shots ride along with the stand-in, with no damping: the stand-in is moved
            // to the player as the cutscene starts, and the camera must not swoop after it.
            CinemachineFollow follow = go.AddComponent<CinemachineFollow>();
            follow.FollowOffset = position;
            follow.TrackerSettings.BindingMode = BindingMode.LockToTargetWithWorldUp;
            follow.TrackerSettings.PositionDamping = Vector3.zero;
            follow.TrackerSettings.RotationDamping = Vector3.zero;
            follow.TrackerSettings.QuaternionDamping = 0f;

            CinemachineRotationComposer aim = go.AddComponent<CinemachineRotationComposer>();
            aim.Damping = Vector2.zero;
            aim.CenterOnActivate = true;

            camera.Follow = actor;
            if (shot.LooksAtActor)
            {
                camera.LookAt = actor;
                aim.TargetOffset = look;
            }
            else
            {
                // A fixed point to look at: a still object beside the cameras, not under one.
                var point = new GameObject($"{shot.Index + 1} {pose}: look point").transform;
                point.SetParent(group, false);
                point.position = look;
                camera.LookAt = point;
            }
            return camera;
        }

        // The Unit 047 stand-in under the director: kept between builds, created once.
        static Transform BuildActor(Transform director)
        {
            Transform actor = director.Find(ActorName);
            if (actor == null)
            {
                actor = new GameObject(ActorName).transform;
                actor.SetParent(director, false);
            }

            CutsceneActor component = actor.GetComponent<CutsceneActor>();
            if (component == null)
                component = actor.gameObject.AddComponent<CutsceneActor>();

            var serialized = new SerializedObject(component);
            SerializedProperty model = serialized.FindProperty("model");
            if (model.objectReferenceValue == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ActorPrefab);
                if (prefab == null)
                {
                    Debug.LogError($"No Unit 047 prefab at {ActorPrefab}; the stand-in has no model.");
                    return actor;
                }
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, actor);
                instance.name = "Model";
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.SetActive(false);
                model.objectReferenceValue = instance;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            return actor;
        }

        static Transform Recreate(Transform parent, string childName)
        {
            Transform old = parent.Find(childName);
            if (old != null)
                Object.DestroyImmediate(old.gameObject);
            var created = new GameObject(childName).transform;
            created.SetParent(parent, false);
            return created;
        }

        // Drops the director's old camera references, so a removed shot leaves nothing behind.
        static void ClearExposedReferences(PlayableDirector playable)
        {
            var serialized = new SerializedObject(playable);
            SerializedProperty references = serialized.FindProperty("m_ExposedReferences.m_References");
            if (references == null)
                return;
            references.ClearArray();
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
