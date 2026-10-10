using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Player;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// A battery cell lying in the level: one more spare cell for the player. A Saboteur
    /// can take it first, through <see cref="ISabotageable.Execute"/>. Like the prototype's
    /// batteries, it comes back after 40 seconds.
    /// </summary>
    public sealed class BatteryPickup : PlayerPickup, ISabotageable
    {
        [Tooltip("The id the Saboteur names this battery by. Must be different for every battery.")]
        [SerializeField, Min(0)] int sabotageId;

        protected override Color GlowColour => new Color(0.49f, 1f, 0.42f);

        protected override float GlowSize => 1.5f;

        protected override float GlowOpacity => 0.45f;

        protected override float UsualRespawnSeconds => 40f;

        protected override string TakenWord => "cell";

        // A battery that is gone, taken by the player or a Saboteur, is not something a
        // Saboteur can go for; it is listed again when it comes back.
        void OnEnable()
        {
            if (!IsTaken)
                SabotageTargets.Register(SabotageKind.Battery, sabotageId, this, transform);
        }

        void OnDisable() => SabotageTargets.Unregister(SabotageKind.Battery, sabotageId, this);

        protected override void OnTaken() => SabotageTargets.Unregister(SabotageKind.Battery, sabotageId, this);

        protected override void OnRestored() =>
            SabotageTargets.Register(SabotageKind.Battery, sabotageId, this, transform);

        protected override bool Collect(PlayerBattery battery) => battery.AddSpareCell();

        /// <summary>Saboteur action: steal the battery, so it is gone for the player.</summary>
        void ISabotageable.Execute() => Take();
    }
}
