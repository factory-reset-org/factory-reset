using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ToyFactory.Player
{
    /// <summary>
    /// The player's own body, seen from inside: Unit 047's legs and feet when the player looks down.
    /// </summary>
    /// <remarks>
    /// <para>The legs are Unit 047's model, made once at start under the player. Every part is set to
    /// cast shadows only, so the camera never sees the torso, head or arms from inside them, and the
    /// body still has a shadow. The legs and feet are drawn only while the player looks well down,
    /// the way a shooter shows its legs, and swing along the way the player is moving, with the foot
    /// kept flat and lifted. The colliders are removed so it cannot touch physics.</para>
    /// </remarks>
    public sealed class PlayerBody : MonoBehaviour
    {
        [Tooltip("The model whose legs are used: Unit 047.")]
        [SerializeField] GameObject modelPrefab;

        [Header("Legs")]
        [Tooltip("How far each leg swings from the hip at a run, in degrees.")]
        [SerializeField, Min(0f)] float legSwingDegrees = 28f;

        [Tooltip("Leg swing cycles per metre walked.")]
        [SerializeField, Min(0.1f)] float cyclesPerMetre = 0.45f;

        [Tooltip("How far behind the camera the body stands, so the knees are not in the view.")]
        [SerializeField, Min(0f)] float bodyBackOffset = 0.12f;

        [Tooltip("The legs are drawn once the player looks down further than this, in degrees. They go again a little above it.")]
        [SerializeField, Range(10f, 80f)] float showLegsBelowDegrees = 38f;

        [Tooltip("How high a foot lifts at the top of a step, in metres.")]
        [SerializeField, Min(0f)] float footLift = 0.06f;

        CharacterController _controller;
        Transform _legL;
        Transform _legR;
        Vector3 _legLBase;
        Vector3 _legRBase;
        Vector3 _stepAxis = Vector3.right;
        float _stride;
        float _phase;
        Renderer[] _lowerBody;
        bool _legsShown;
        Transform _pivot;

        void Start()
        {
            _controller = GetComponent<CharacterController>();
            if (modelPrefab == null)
                return;

            BuildBody();
        }

        void BuildBody()
        {
            GameObject body = Instantiate(modelPrefab, transform);
            body.name = "Body (own legs)";
            body.transform.localPosition = new Vector3(0f, 0f, -bodyBackOffset);
            body.transform.localRotation = Quaternion.identity;

            foreach (Collider c in body.GetComponentsInChildren<Collider>(true))
                Destroy(c);

            var lower = new System.Collections.Generic.List<Renderer>();
            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                if (IsLowerBody(r.name))
                    lower.Add(r);
            }

            _lowerBody = lower.ToArray();
            _pivot = transform.Find("CameraPivot");
            _legL = FindDeep(body.transform, "Leg_L_Pivot");
            _legR = FindDeep(body.transform, "Leg_R_Pivot");
            if (_legL != null)
                _legLBase = _legL.localPosition;
            if (_legR != null)
                _legRBase = _legR.localPosition;
        }

        // The pelvis stays out of the view: from above it is a big dark block in the middle of the screen.
        static bool IsLowerBody(string partName) =>
            partName.StartsWith("Leg_", StringComparison.Ordinal)
            || partName.StartsWith("Foot_", StringComparison.Ordinal);

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null)
                    return found;
            }
            return null;
        }

        void Update()
        {
            SwingLegs();
        }

        void SwingLegs()
        {
            if (_legL == null || _legR == null || _controller == null)
                return;

            ShowLegsWhenLookingDown();

            Vector3 v = _controller.velocity;
            v.y = 0f;
            float speed = v.magnitude;
            float dt = Time.deltaTime;

            // Legs swing along the way the player is going, so a sidestep steps sideways.
            if (speed > 0.3f)
            {
                Vector3 local = transform.InverseTransformDirection(v / speed);
                Vector3 axis = new Vector3(local.z, 0f, -local.x);
                _stepAxis = Vector3.Dot(_stepAxis, axis) < 0.2f ? axis : Vector3.Slerp(_stepAxis, axis, 1f - Mathf.Exp(-12f * dt));
                _stepAxis.Normalize();
            }

            _stride = Mathf.MoveTowards(_stride, Mathf.Clamp01(speed / 4f), 5f * dt);
            _phase += speed * cyclesPerMetre * 2f * Mathf.PI * dt;
            float swing = legSwingDegrees * _stride * Mathf.Sin(_phase);
            float lift = footLift * _stride;

            ApplyLeg(_legL, _legLBase, swing, lift * Mathf.Max(0f, Mathf.Cos(_phase)));
            ApplyLeg(_legR, _legRBase, -swing, lift * Mathf.Max(0f, -Mathf.Cos(_phase)));
        }

        // The leg swings from the hip, the foot turns the other way so it stays flat on the floor,
        // and the one that is moving forward is raised a little.
        void ApplyLeg(Transform leg, Vector3 basePosition, float swing, float lift)
        {
            leg.localPosition = basePosition + new Vector3(0f, lift, 0f);
            leg.localRotation = Quaternion.AngleAxis(swing, _stepAxis);
            Transform foot = leg.childCount > 0 ? leg.GetChild(0) : null;
            if (foot != null && foot.name.StartsWith("Foot_", StringComparison.Ordinal))
                foot.localRotation = Quaternion.AngleAxis(-swing * 0.6f, _stepAxis);
        }

        // Drawn only when the player looks down past the threshold, with a small gap so it does not
        // flicker at the edge.
        void ShowLegsWhenLookingDown()
        {
            if (_lowerBody == null || _pivot == null)
                return;

            float pitch = Mathf.DeltaAngle(0f, _pivot.localEulerAngles.x);
            bool show = _legsShown ? pitch > showLegsBelowDegrees - 6f : pitch > showLegsBelowDegrees;
            if (show == _legsShown)
                return;

            _legsShown = show;
            ShadowCastingMode mode = show ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            for (int i = 0; i < _lowerBody.Length; i++)
                _lowerBody[i].shadowCastingMode = mode;
        }
    }
}
