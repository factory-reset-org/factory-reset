using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ToyFactory.Player
{
    /// <summary>
    /// The player's own body, seen from inside: Unit 047's legs and feet when the player looks
    /// down, and a hand that reaches out when the player slides a box.
    /// </summary>
    /// <remarks>
    /// <para>The legs are Unit 047's model, made once at start under the player. Every part except
    /// the legs, feet and pelvis is set to cast shadows only, so the camera never sees the torso,
    /// head or arms from inside them, and the body still has a shadow. Its colliders are removed so
    /// it cannot touch physics, and the legs swing with the player's speed.</para>
    /// <para>The hand is the model's left arm and hand, drawn by the overlay camera on the
    /// ViewModel layer like the gun, so it can never be inside a wall. It is hidden until a slide.
    /// <see cref="Thrust"/> reaches out, calls back at the moment the hand arrives (the box is
    /// shoved then), and pulls back.</para>
    /// </remarks>
    public sealed class PlayerBody : MonoBehaviour
    {
        const string ViewLayerName = "ViewModel";

        [Tooltip("The model whose legs and hand are used: Unit 047.")]
        [SerializeField] GameObject modelPrefab;

        [Tooltip("The overlay camera that draws the hand. Left empty, found under the player as ViewModelCamera.")]
        [SerializeField] Transform viewCamera;

        [Header("Legs")]
        [Tooltip("How far each leg swings from the hip at a run, in degrees.")]
        [SerializeField, Min(0f)] float legSwingDegrees = 28f;

        [Tooltip("Leg swing cycles per metre walked.")]
        [SerializeField, Min(0.1f)] float cyclesPerMetre = 0.45f;

        [Tooltip("How far behind the camera the body stands, so the knees are not in the view.")]
        [SerializeField, Min(0f)] float bodyBackOffset = 0.12f;

        [Header("Hand")]
        [SerializeField, Min(0.05f)] float reachSeconds = 0.12f;
        [SerializeField, Min(0f)] float holdSeconds = 0.06f;
        [SerializeField, Min(0.05f)] float returnSeconds = 0.22f;

        [Tooltip("Where the hand waits and where it ends up, in the overlay camera's space.")]
        [SerializeField] Vector3 restPosition = new Vector3(-0.30f, -0.42f, 0.10f);
        [SerializeField] Vector3 reachPosition = new Vector3(-0.16f, -0.22f, 0.52f);
        [SerializeField, Min(0.1f)] float handScale = 1.2f;

        [Tooltip("How the arm is held, in degrees about X, Y, Z. -90 on X points the model's hanging arm straight ahead; a little more tilts it up and in.")]
        [SerializeField] Vector3 handAngles = new Vector3(-78f, 10f, 0f);

        CharacterController _controller;
        Transform _legL;
        Transform _legR;
        float _phase;

        Transform _hand;
        float _handTime = -1f;
        Action _onArrive;
        bool _arrived;

        /// <summary>True while the hand is reaching out or coming back.</summary>
        public bool IsReaching => _handTime >= 0f;

        void Start()
        {
            _controller = GetComponent<CharacterController>();
            if (modelPrefab == null)
                return;

            GameObject body = BuildBody();
            BuildHand(body.transform);
        }

        GameObject BuildBody()
        {
            GameObject body = Instantiate(modelPrefab, transform);
            body.name = "Body (own legs)";
            body.transform.localPosition = new Vector3(0f, 0f, -bodyBackOffset);
            body.transform.localRotation = Quaternion.identity;

            foreach (Collider c in body.GetComponentsInChildren<Collider>(true))
                Destroy(c);

            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
            {
                bool lower = IsLowerBody(r.name);
                r.shadowCastingMode = lower ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            }

            _legL = FindDeep(body.transform, "Leg_L_Pivot");
            _legR = FindDeep(body.transform, "Leg_R_Pivot");
            return body;
        }

        static bool IsLowerBody(string partName) =>
            partName.StartsWith("Leg_", StringComparison.Ordinal)
            || partName.StartsWith("Foot_", StringComparison.Ordinal)
            || partName == "Pelvis";

        void BuildHand(Transform body)
        {
            Transform view = viewCamera != null ? viewCamera : transform.Find("CameraPivot/Camera/ViewModelCamera");
            Transform arm = FindDeep(body, "Arm_L");
            Transform hand = FindDeep(body, "Hand_L");
            if (view == null || arm == null || hand == null)
                return;

            int layer = LayerMask.NameToLayer(ViewLayerName);
            var root = new GameObject("Hand (slide)");
            root.layer = layer < 0 ? 0 : layer;
            _hand = root.transform;
            _hand.SetParent(view, false);

            // The arm hangs down from the shoulder in the model; turned -90 degrees about X it points
            // straight ahead of the camera.
            _hand.localRotation = Quaternion.Euler(handAngles);
            _hand.localScale = Vector3.one * handScale;
            _hand.localPosition = restPosition;

            CopyPart(arm, new Vector3(-0.02f, -0.28f, 0f), root.transform, layer);
            CopyPart(hand, new Vector3(-0.02f, -0.60f, 0f), root.transform, layer);
            root.SetActive(false);
        }

        static void CopyPart(Transform source, Vector3 localPosition, Transform parent, int layer)
        {
            var filter = source.GetComponent<MeshFilter>();
            var renderer = source.GetComponent<MeshRenderer>();
            if (filter == null || renderer == null)
                return;

            var part = new GameObject(source.name + " (view)");
            part.layer = layer < 0 ? 0 : layer;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = source.localRotation;
            part.transform.localScale = source.localScale;
            part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            MeshRenderer copy = part.AddComponent<MeshRenderer>();
            copy.sharedMaterials = renderer.sharedMaterials;
            copy.shadowCastingMode = ShadowCastingMode.Off;
            copy.receiveShadows = false;
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

        /// <summary>
        /// Reaches the hand out. <paramref name="onArrive"/> is called once, when the hand is at the
        /// far point. False, and nothing happens, if the hand is already out.
        /// </summary>
        public bool Thrust(Action onArrive)
        {
            if (_hand == null)
            {
                onArrive?.Invoke();
                return true;
            }

            if (IsReaching)
                return false;

            _onArrive = onArrive;
            _arrived = false;
            _handTime = 0f;
            _hand.gameObject.SetActive(true);
            return true;
        }

        void Update()
        {
            SwingLegs();
            AnimateHand();
        }

        void SwingLegs()
        {
            if (_legL == null || _legR == null || _controller == null)
                return;

            Vector3 v = _controller.velocity;
            float speed = new Vector2(v.x, v.z).magnitude;
            _phase += speed * cyclesPerMetre * 2f * Mathf.PI * Time.deltaTime;
            float amount = legSwingDegrees * Mathf.Clamp01(speed / 4f) * Mathf.Sin(_phase);
            _legL.localRotation = Quaternion.Euler(amount, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(-amount, 0f, 0f);
        }

        void AnimateHand()
        {
            if (_handTime < 0f || _hand == null)
                return;

            _handTime += Time.deltaTime;
            float t = _handTime;

            if (t < reachSeconds)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / reachSeconds);
                _hand.localPosition = Vector3.Lerp(restPosition, reachPosition, k);
            }
            else if (t < reachSeconds + holdSeconds)
            {
                _hand.localPosition = reachPosition;
                if (!_arrived)
                    Arrive();
            }
            else if (t < reachSeconds + holdSeconds + returnSeconds)
            {
                if (!_arrived)
                    Arrive();
                float k = Mathf.SmoothStep(0f, 1f, (t - reachSeconds - holdSeconds) / returnSeconds);
                _hand.localPosition = Vector3.Lerp(reachPosition, restPosition, k);
            }
            else
            {
                if (!_arrived)
                    Arrive();
                _hand.localPosition = restPosition;
                _hand.gameObject.SetActive(false);
                _handTime = -1f;
            }
        }

        void Arrive()
        {
            _arrived = true;
            Action callback = _onArrive;
            _onArrive = null;
            callback?.Invoke();
        }
    }
}
