using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ToyFactory.Player
{
    /// <summary>
    /// The player's own legs, the way a first-person shooter shows them: looking down, the player sees
    /// their legs and feet, and they step as the player walks.
    /// </summary>
    /// <remarks>
    /// Unit 047's model stands a little in front of the camera. Its legs and feet are drawn for the camera
    /// and are only in view when the player looks down past about 50 degrees. The hips, chest, head,
    /// arms and blaster are not drawn (the gun is the first-person model), but every part still casts
    /// a shadow. Colliders are removed so it cannot
    /// touch physics, and the legs swing along the way the player moves.
    /// </remarks>
    public sealed class PlayerBody : MonoBehaviour
    {
        [Tooltip("The model used for the body: Unit 047.")]
        [SerializeField] GameObject modelPrefab;

        [Header("Legs")]
        [Tooltip("How far each leg swings from the hip at a run, in degrees.")]
        [SerializeField, Min(0f)] float legSwingDegrees = 28f;

        [Tooltip("Leg swing cycles per metre walked.")]
        [SerializeField, Min(0.1f)] float cyclesPerMetre = 0.45f;

        [Tooltip("How far behind the camera the body stands; negative is in front of it. In front, the legs and feet come into view at about 50 degrees of look down.")]
        [SerializeField] float bodyBackOffset = -0.30f;

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
            body.name = "Body";
            body.transform.localPosition = new Vector3(0f, 0f, -bodyBackOffset);
            body.transform.localRotation = Quaternion.identity;

            foreach (Collider c in body.GetComponentsInChildren<Collider>(true))
                Destroy(c);

            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = IsSeen(r.name) ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;

            _legL = FindDeep(body.transform, "Leg_L_Pivot");
            _legR = FindDeep(body.transform, "Leg_R_Pivot");
            if (_legL != null)
                _legLBase = _legL.localPosition;
            if (_legR != null)
                _legRBase = _legR.localPosition;
        }

        // What the player sees of themselves: the legs and the feet. Seen from above, the hips and chest are
        // big flat slabs, so they are not drawn.
        static bool IsSeen(string partName) =>
            partName.StartsWith("Leg_", StringComparison.Ordinal) || partName.StartsWith("Foot_", StringComparison.Ordinal);

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
    }
}
