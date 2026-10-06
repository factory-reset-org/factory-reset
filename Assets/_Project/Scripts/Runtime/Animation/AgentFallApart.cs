using System.Collections.Generic;
using UnityEngine;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Animation
{
    /// <summary>
    /// Makes a downed agent fall apart. When it is knocked out, every mesh part of its model
    /// breaks off as a physics body on the Debris layer, with a small burst so the parts scatter.
    /// In the last <c>reassembleSeconds</c> of the knock-out the parts fly back to where they
    /// belong, ease into place and are re-attached, and the Animator takes over again at the
    /// reboot. A scrapped agent (a Saboteur) falls apart the same way, but its parts shrink
    /// away instead and the agent is switched off.
    /// </summary>
    /// <remarks>
    /// <para><b>Rigid parts:</b> S3's models are rigid parts under pivots, with one disabled
    /// collider per part, so falling apart is just detaching the parts and enabling those
    /// colliders. Nothing is spawned or copied.</para>
    /// <para><b>Debris layer:</b> parts collide with the floor, walls and each other, but not
    /// with the player or the agents, so a pile of parts never blocks anyone.</para>
    /// <para><b>Reassembly:</b> each part's target is its original pose under its pivot, which
    /// holds still while the Animator is off. Parts move on a smoothstep from wherever they
    /// landed, so they all arrive together at the reboot.</para>
    /// </remarks>
    [RequireComponent(typeof(AgentController))]
    public sealed class AgentFallApart : MonoBehaviour
    {
        [Tooltip("The model whose mesh parts break off (the nested S3 prefab).")]
        [SerializeField] Transform model;

        [Tooltip("Seconds at the end of a knock-out spent flying the parts back into place.")]
        [SerializeField, Min(0.1f)] float reassembleSeconds = 1f;

        [Tooltip("Speed (m/s) of the burst that scatters the parts.")]
        [SerializeField, Min(0f)] float burstSpeed = 2.5f;

        [Tooltip("Scrapped agents: seconds the parts lie there before they shrink away.")]
        [SerializeField, Min(0f)] float scrapLingerSeconds = 2f;

        [Tooltip("Scrapped agents: seconds to shrink the parts to nothing.")]
        [SerializeField, Min(0.1f)] float scrapShrinkSeconds = 1f;

        sealed class Part
        {
            public Transform Transform;
            public Transform Parent;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
            public Collider Collider;
            public bool ColliderWasEnabled;
            public int Layer;
            public Rigidbody Body;
            public Vector3 StartPosition;
            public Quaternion StartRotation;
        }

        enum Phase { Whole, Apart, Reassembling, Scrapped }

        readonly List<Part> _parts = new List<Part>();
        AgentController _agent;
        Animator _animator;
        Phase _phase;
        float _phaseTime;
        int _debrisLayer;

        /// <summary>True while the parts are off the body (apart, flying back, or scrapped).</summary>
        public bool IsApart => _phase != Phase.Whole;

        void Awake()
        {
            _agent = GetComponent<AgentController>();
            if (model == null)
                model = transform;
            _animator = model.GetComponentInChildren<Animator>();
            _debrisLayer = LayerMask.NameToLayer("Debris");

            foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                Transform part = renderer.transform;
                Collider collider = part.GetComponent<Collider>();
                _parts.Add(new Part
                {
                    Transform = part,
                    Parent = part.parent,
                    LocalPosition = part.localPosition,
                    LocalRotation = part.localRotation,
                    LocalScale = part.localScale,
                    Collider = collider,
                    ColliderWasEnabled = collider != null && collider.enabled,
                    Layer = part.gameObject.layer,
                });
            }
        }

        void LateUpdate()
        {
            switch (_phase)
            {
                case Phase.Whole:
                    if (_agent.IsDead)
                        BreakApart(Phase.Scrapped);
                    else if (_agent.IsDisabled)
                        BreakApart(Phase.Apart);
                    break;

                case Phase.Apart:
                    if (_agent.IsDead)
                        _phase = Phase.Scrapped;
                    else if (!_agent.IsDisabled || _agent.KnockOutTimeLeft <= reassembleSeconds)
                        BeginReassembly();
                    break;

                case Phase.Reassembling:
                    Reassemble();
                    break;

                case Phase.Scrapped:
                    ShrinkAway();
                    break;
            }
        }

        void BreakApart(Phase next)
        {
            _phase = next;
            _phaseTime = 0f;
            if (_animator != null)
                _animator.enabled = false;

            Vector3 centre = transform.position + Vector3.up * 1f;
            Transform debrisParent = transform.parent;
            foreach (Part part in _parts)
            {
                // Out of the hierarchy, so the parts fall freely while the body stands still.
                part.Transform.SetParent(debrisParent, true);
                if (_debrisLayer >= 0)
                    part.Transform.gameObject.layer = _debrisLayer;
                if (part.Collider == null)
                    part.Collider = part.Transform.gameObject.AddComponent<BoxCollider>();
                part.Collider.enabled = true;

                part.Body = part.Transform.gameObject.AddComponent<Rigidbody>();
                part.Body.mass = 0.5f;
                Vector3 outward = part.Transform.position - centre;
                outward.y = Mathf.Abs(outward.y) + 0.5f;
                part.Body.linearVelocity = outward.normalized * burstSpeed;
                part.Body.angularVelocity = Random.insideUnitSphere * 6f;
            }
        }

        void BeginReassembly()
        {
            _phase = Phase.Reassembling;
            _phaseTime = 0f;
            foreach (Part part in _parts)
            {
                if (part.Body != null)
                    Destroy(part.Body);
                part.Body = null;
                part.Collider.enabled = false;
                part.StartPosition = part.Transform.position;
                part.StartRotation = part.Transform.rotation;
            }
        }

        void Reassemble()
        {
            _phaseTime += Time.deltaTime;
            float t = Mathf.Clamp01(_phaseTime / reassembleSeconds);
            float eased = t * t * (3f - 2f * t);   // smoothstep: leaves and arrives gently

            foreach (Part part in _parts)
            {
                Vector3 home = part.Parent.TransformPoint(part.LocalPosition);
                Quaternion homeRotation = part.Parent.rotation * part.LocalRotation;
                part.Transform.SetPositionAndRotation(
                    Vector3.Lerp(part.StartPosition, home, eased),
                    Quaternion.Slerp(part.StartRotation, homeRotation, eased));
            }

            if (t >= 1f && !_agent.IsDisabled)
                Restore();
        }

        // Back under their pivots exactly as they were, with the Animator in charge again.
        void Restore()
        {
            foreach (Part part in _parts)
            {
                part.Transform.SetParent(part.Parent, false);
                part.Transform.localPosition = part.LocalPosition;
                part.Transform.localRotation = part.LocalRotation;
                part.Transform.localScale = part.LocalScale;
                part.Transform.gameObject.layer = part.Layer;
                part.Collider.enabled = part.ColliderWasEnabled;
            }
            if (_animator != null)
                _animator.enabled = true;
            _phase = Phase.Whole;
        }

        void ShrinkAway()
        {
            _phaseTime += Time.deltaTime;
            if (_phaseTime < scrapLingerSeconds)
                return;

            float t = Mathf.Clamp01((_phaseTime - scrapLingerSeconds) / scrapShrinkSeconds);
            foreach (Part part in _parts)
                part.Transform.localScale = part.LocalScale * (1f - t);

            if (t < 1f)
                return;
            foreach (Part part in _parts)
                Destroy(part.Transform.gameObject);
            _parts.Clear();
            gameObject.SetActive(false);   // switched off, not destroyed: the spawner still lists it
        }
    }
}
