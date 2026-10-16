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
    /// <para>The legs are Unit 047's model, made once at start under the player. Every part is set to
    /// cast shadows only, so the camera never sees the torso, head or arms from inside them, and the
    /// body still has a shadow. The legs and feet are drawn only while the player looks well down,
    /// the way a shooter shows its legs, and swing along the way the player is moving, with the foot
    /// kept flat and lifted. The colliders are removed so it cannot touch physics.</para>
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

        [Tooltip("The legs are drawn once the player looks down further than this, in degrees. They go again a little above it.")]
        [SerializeField, Range(10f, 80f)] float showLegsBelowDegrees = 38f;

        [Tooltip("How high a foot lifts at the top of a step, in metres.")]
        [SerializeField, Min(0f)] float footLift = 0.06f;

        [Header("Hand")]
        [Tooltip("The hand draws back and down before it shoves, so the shove reads as a swing.")]
        [SerializeField, Min(0.02f)] float windupSeconds = 0.08f;
        [SerializeField, Min(0.05f)] float reachSeconds = 0.10f;
        [SerializeField, Min(0f)] float holdSeconds = 0.05f;
        [SerializeField, Min(0.05f)] float returnSeconds = 0.26f;

        [Tooltip("How far the hand draws back from where it rests, in the overlay camera's space.")]
        [SerializeField] Vector3 windupOffset = new Vector3(-0.05f, -0.08f, -0.12f);

        [Tooltip("How much the hand turns during the wind up, in degrees about X, Y, Z, on top of the rest angles.")]
        [SerializeField] Vector3 windupTurn = new Vector3(14f, -10f, -14f);

        [Tooltip("How the arm is held at the end of the shove, with the palm facing the box.")]
        [SerializeField] Vector3 reachAngles = new Vector3(12f, 0f, 0f);

        [Tooltip("How hard the view kicks when the hand lands.")]
        [SerializeField, Min(0f)] float impactShake = 0.06f;

        [Tooltip("Where the hand waits and where it ends up, in the overlay camera's space.")]
        [SerializeField] Vector3 restPosition = new Vector3(-0.30f, -0.44f, 0.36f);
        [SerializeField] Vector3 reachPosition = new Vector3(-0.06f, -0.30f, 0.72f);
        [SerializeField, Min(0.1f)] float handScale = 1.0f;

        [Tooltip("How the hand waits, in degrees about X, Y, Z. The hand is built fingers up with the palm to the front; more on X leans the fingers away from the player.")]
        [SerializeField] Vector3 handAngles = new Vector3(38f, 12f, -10f);

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
            return body;
        }

        // The pelvis stays out of the view: from above it is a big dark block in the middle of the screen.
        static bool IsLowerBody(string partName) =>
            partName.StartsWith("Leg_", StringComparison.Ordinal)
            || partName.StartsWith("Foot_", StringComparison.Ordinal);

        // The hand is a toy hand built here from simple shapes in the model's own materials: a forearm
        // with a cuff, a mitten palm, four fingers and a thumb. It is made upright, fingers up and palm
        // to the front, and the rest and reach poses tilt it. The model's own arm is posed round a
        // blaster and cannot be used for a shove.
        void BuildHand(Transform body)
        {
            Transform view = viewCamera != null ? viewCamera : transform.Find("CameraPivot/Camera/ViewModelCamera");
            Material skin = MaterialOf(body, "Arm_L");
            Material glove = MaterialOf(body, "Hand_L");
            Material cuff = MaterialOf(body, "Neck");
            if (view == null || skin == null || glove == null)
                return;

            int layer = LayerMask.NameToLayer(ViewLayerName);
            if (layer < 0)
                layer = 0;

            var root = new GameObject("Hand (slide)");
            root.layer = layer;
            _hand = root.transform;
            _hand.SetParent(view, false);
            _hand.localRotation = Quaternion.Euler(handAngles);
            _hand.localScale = Vector3.one * handScale;
            _hand.localPosition = restPosition;

            Part(root.transform, PrimitiveType.Capsule, "Forearm", new Vector3(0f, -0.32f, 0f), Vector3.zero, new Vector3(0.10f, 0.30f, 0.10f), skin, layer);
            Part(root.transform, PrimitiveType.Cylinder, "Cuff", new Vector3(0f, -0.035f, 0f), Vector3.zero, new Vector3(0.125f, 0.03f, 0.125f), cuff != null ? cuff : skin, layer);
            Part(root.transform, PrimitiveType.Sphere, "Palm", new Vector3(0f, 0.045f, 0f), Vector3.zero, new Vector3(0.125f, 0.105f, 0.065f), glove, layer);
            float[] fingerX = { -0.045f, -0.015f, 0.015f, 0.045f };
            float[] fingerLength = { 0.085f, 0.105f, 0.10f, 0.08f };
            for (int i = 0; i < fingerX.Length; i++)
            {
                Part(root.transform, PrimitiveType.Capsule, "Finger " + (i + 1),
                    new Vector3(fingerX[i], 0.095f + fingerLength[i] * 0.5f, 0.004f), new Vector3(0f, 0f, -fingerX[i] * 120f),
                    new Vector3(0.03f, fingerLength[i] * 0.5f, 0.032f), glove, layer);
            }

            Part(root.transform, PrimitiveType.Capsule, "Thumb", new Vector3(0.078f, 0.075f, 0f), new Vector3(0f, 0f, -52f),
                new Vector3(0.033f, 0.04f, 0.034f), glove, layer);
            root.SetActive(false);
        }

        static Material MaterialOf(Transform body, string partName)
        {
            Transform part = FindDeep(body, partName);
            Renderer renderer = part != null ? part.GetComponent<Renderer>() : null;
            return renderer != null ? renderer.sharedMaterial : null;
        }

        // One shape of the hand: a primitive's mesh with no collider, drawn only by the overlay camera.
        static void Part(Transform parent, PrimitiveType shape, string name, Vector3 position, Vector3 euler, Vector3 scale, Material material, int layer)
        {
            GameObject part = GameObject.CreatePrimitive(shape);
            part.name = name;
            part.layer = layer;
            Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = Quaternion.Euler(euler);
            part.transform.localScale = scale;
            MeshRenderer renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
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

        void AnimateHand()
        {
            if (_handTime < 0f || _hand == null)
                return;

            _handTime += Time.deltaTime;
            float t = _handTime;
            Vector3 windPosition = restPosition + windupOffset;
            Quaternion restRotation = Quaternion.Euler(handAngles);
            Quaternion windRotation = Quaternion.Euler(handAngles + windupTurn);
            Quaternion reachRotation = Quaternion.Euler(reachAngles);

            if (t < windupSeconds)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / windupSeconds);
                Pose(Vector3.Lerp(restPosition, windPosition, k), Quaternion.Slerp(restRotation, windRotation, k), 1f);
            }
            else if (t < windupSeconds + reachSeconds)
            {
                // Fast out with a little overshoot, so it lands with a snap.
                float k = EaseOutBack((t - windupSeconds) / reachSeconds);
                Pose(Vector3.LerpUnclamped(windPosition, reachPosition, k),
                    Quaternion.SlerpUnclamped(windRotation, reachRotation, k), 1f + 0.06f * Mathf.Clamp01(k));
            }
            else if (t < windupSeconds + reachSeconds + holdSeconds)
            {
                Pose(reachPosition, reachRotation, 1.06f);
                if (!_arrived)
                    Arrive();
            }
            else if (t < windupSeconds + reachSeconds + holdSeconds + returnSeconds)
            {
                if (!_arrived)
                    Arrive();
                float k = Mathf.SmoothStep(0f, 1f, (t - windupSeconds - reachSeconds - holdSeconds) / returnSeconds);
                Pose(Vector3.Lerp(reachPosition, restPosition, k), Quaternion.Slerp(reachRotation, restRotation, k),
                    Mathf.Lerp(1.06f, 1f, k));
            }
            else
            {
                if (!_arrived)
                    Arrive();
                Pose(restPosition, restRotation, 1f);
                _hand.gameObject.SetActive(false);
                _handTime = -1f;
            }
        }

        void Pose(Vector3 position, Quaternion rotation, float scale)
        {
            _hand.localPosition = position;
            _hand.localRotation = rotation;
            _hand.localScale = Vector3.one * (handScale * scale);
        }

        static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t) - 1f;
            const float c1 = 1.4f;
            return 1f + (c1 + 1f) * t * t * t + c1 * t * t;
        }

        void Arrive()
        {
            _arrived = true;
            PlayerCameraEffects.Shake(impactShake);
            Action callback = _onArrive;
            _onArrive = null;
            callback?.Invoke();
        }
    }
}
