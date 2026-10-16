using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ToyFactory.Player
{
    /// <summary>
    /// The player's body: a shadow on the floor and Unit 047's own arms holding the blaster in view,
    /// the way a first-person shooter shows its player.
    /// </summary>
    /// <remarks>
    /// <para>Unit 047's model stands under the player with every part set to cast shadows only, so the
    /// player has a shadow and the camera never sees the head or torso from inside them. Its legs swing
    /// along the way the player moves, so the shadow walks.</para>
    /// <para>The arms are a second copy of the model. Its two arms and hands are posed round a blaster that
    /// is the first-person gun scaled up (<see cref="modelBlasterScale"/>), so the copy is scaled to fit and
    /// moved until its blaster sits on the gun's. Only the arms, hands and shoulders are kept and drawn,
    /// on the ViewModel layer by the overlay camera like the gun, under the gun, so they bob and kick
    /// with it and can never be inside a wall.</para>
    /// </remarks>
    public sealed class PlayerBody : MonoBehaviour
    {
        const string ViewLayerName = "ViewModel";

        [Tooltip("The model used for the shadow and the arms: Unit 047.")]
        [SerializeField] GameObject modelPrefab;

        [Header("Arms")]
        [Tooltip("How much bigger the blaster is on the model than in the first-person gun. The arms are scaled down by this to fit the gun.")]
        [SerializeField, Min(0.1f)] float modelBlasterScale = 1.25f;

        [Tooltip("The first-person gun's model, under the player. Left empty, found as CameraPivot/Camera/BlasterViewModel/Model/BlasterModel.")]
        [SerializeField] Transform gunModel;

        [Header("Legs")]
        [Tooltip("How far each leg swings from the hip at a run, in degrees.")]
        [SerializeField, Min(0f)] float legSwingDegrees = 28f;

        [Tooltip("Leg swing cycles per metre walked.")]
        [SerializeField, Min(0.1f)] float cyclesPerMetre = 0.45f;

        [Tooltip("How far behind the camera the shadow body stands.")]
        [SerializeField] float bodyBackOffset = 0.12f;

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
            BuildArms();
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
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;

            _legL = FindDeep(body.transform, "Leg_L_Pivot");
            _legR = FindDeep(body.transform, "Leg_R_Pivot");
            if (_legL != null)
                _legLBase = _legL.localPosition;
            if (_legR != null)
                _legRBase = _legR.localPosition;
        }

        static bool IsArmPart(string partName) =>
            partName == "Arm_L" || partName == "Arm_R" || partName == "Hand_L" || partName == "Hand_R"
            || partName == "ShoulderBall_L" || partName == "ShoulderBall_R";

        // A copy of the model whose blaster is laid over the first-person gun, keeping only the arms.
        void BuildArms()
        {
            Transform gun = gunModel != null ? gunModel : transform.Find("CameraPivot/Camera/BlasterViewModel/Model/BlasterModel");
            Renderer gunRenderer = gun != null ? gun.GetComponent<Renderer>() : null;
            if (gunRenderer == null)
                return;

            int layer = LayerMask.NameToLayer(ViewLayerName);
            if (layer < 0)
                layer = 0;

            GameObject arms = Instantiate(modelPrefab, gun.parent);
            arms.name = "Arms";
            arms.transform.localPosition = Vector3.zero;
            arms.transform.localRotation = Quaternion.identity;
            arms.transform.localScale = Vector3.one / modelBlasterScale;

            foreach (Collider c in arms.GetComponentsInChildren<Collider>(true))
                Destroy(c);
            foreach (Animator a in arms.GetComponentsInChildren<Animator>(true))
                Destroy(a);
            // The model's own motion scripts would swing the arms on their own.
            foreach (MonoBehaviour m in arms.GetComponentsInChildren<MonoBehaviour>(true))
                Destroy(m);

            Bounds modelGun = default;
            bool any = false;
            var kept = new System.Collections.Generic.List<Renderer>();
            foreach (Renderer r in arms.GetComponentsInChildren<Renderer>(true))
            {
                r.gameObject.layer = layer;
                if (r.name == "Blaster_Body" || r.name == "Blaster_Barrel")
                {
                    if (!any)
                        modelGun = r.bounds;
                    else
                        modelGun.Encapsulate(r.bounds);
                    any = true;
                }

                if (IsArmPart(r.name))
                {
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    kept.Add(r);
                }
                else
                {
                    r.enabled = false;
                }
            }

            foreach (Transform t in arms.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;

            // The model's blaster is the gun scaled up and held in the hands: move the copy until it sits on the gun.
            if (any)
                arms.transform.position += gunRenderer.bounds.center - modelGun.center;

            // The gun hides in cutscenes and on the title screen; so do the arms.
            BlasterViewModel viewModel = GetComponentInChildren<BlasterViewModel>(true);
            if (viewModel != null)
                viewModel.Adopt(kept.ToArray());
        }

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
