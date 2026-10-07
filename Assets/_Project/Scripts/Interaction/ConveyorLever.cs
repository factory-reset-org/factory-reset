using UnityEngine;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// The jammed conveyor lever. The belts start off; the first pull starts them and
    /// completes the task, and every later pull switches them off or on again.
    /// </summary>
    public sealed class ConveyorLever : TaskProp, IInteractable
    {
        [Tooltip("Must equal the task id in the chapter data.")]
        [SerializeField] string taskId = "ch1.lever";

        [SerializeField] ConveyorBelt[] belts;

        [Header("Arm")]
        [Tooltip("The handle that tilts when pulled. Optional.")]
        [SerializeField] Transform arm;
        [SerializeField] float offAngle = -30f;
        [SerializeField] float onAngle = 30f;
        [SerializeField, Min(1f)] float armDegreesPerSecond = 240f;

        public override string Id => taskId;

        public bool IsOn { get; private set; }

        void Start() => ApplyToBelts();

        public void Interact()
        {
            IsOn = !IsOn;
            ApplyToBelts();
            Complete();
        }

        void ApplyToBelts()
        {
            for (int i = 0; i < belts.Length; i++)
            {
                if (belts[i] != null)
                    belts[i].SetRunning(IsOn);
            }
        }

        void Update()
        {
            if (arm == null)
                return;

            Quaternion target = Quaternion.Euler(IsOn ? onAngle : offAngle, 0f, 0f);
            arm.localRotation = Quaternion.RotateTowards(arm.localRotation, target, armDegreesPerSecond * Time.deltaTime);
        }
    }
}
