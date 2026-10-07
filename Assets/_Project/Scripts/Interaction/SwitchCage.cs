using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// The energy cage round a control switch. While it stands, its collider keeps the
    /// player's interact ray off the switch. When the switch is unsealed the collider goes
    /// at once and the cage shrinks away.
    /// </summary>
    public sealed class SwitchCage : MonoBehaviour
    {
        [SerializeField, Range(1, ChapterEvents.SwitchCount)] int switchNumber = 1;

        [SerializeField, Min(0.05f)] float dropTime = 0.4f;

        Collider _collider;
        Vector3 _fullScale;
        float _progress;
        bool _dropping;

        void Awake()
        {
            _collider = GetComponent<Collider>();
            _fullScale = transform.localScale;
        }

        void OnEnable() => ChapterEvents.OnSwitchUnsealed += HandleUnsealed;

        void OnDisable() => ChapterEvents.OnSwitchUnsealed -= HandleUnsealed;

        void HandleUnsealed(int number)
        {
            if (number != switchNumber || _dropping)
                return;

            _dropping = true;
            if (_collider != null)
                _collider.enabled = false;
        }

        void Update()
        {
            if (!_dropping)
                return;

            _progress += Time.deltaTime / dropTime;
            transform.localScale = Vector3.Lerp(_fullScale, Vector3.zero, _progress);
            if (_progress >= 1f)
                gameObject.SetActive(false);
        }
    }
}
