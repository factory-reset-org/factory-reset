using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Player;
using ToyFactory.UI;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// A battery cell lying in the level: one more spare cell for the player. A Saboteur
    /// can take it first, through <see cref="ISabotageable.Execute"/>. Like the prototype's
    /// batteries, it comes back after 40 seconds.
    /// </summary>
    public sealed class BatteryPickup : PlayerPickup, ISabotageable, ISabotageFact
    {
        [Tooltip("The id the Saboteur names this battery by. Unique across every door, trap and battery: the squad claims a target by id alone.")]
        [SerializeField, Min(0)] int sabotageId;

        SabotageKind ISabotageFact.FactKind => SabotageKind.Battery;
        int ISabotageFact.SabotageId => sabotageId;

        /// <summary>Only listed while it is lying there; a taken battery unregisters.</summary>
        bool ISabotageFact.Available => !IsTaken;

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

        [Tooltip("Integrity the battery repairs when the player takes it.")]
        [SerializeField, Min(0f)] float repair = 25f;

        // A battery fills the blaster's cell, adds a spare cell and repairs some integrity: the player sees the charge
        // bar and the integrity bar go up. It is taken if any of that helped. A cell the player takes scores 50 (the
        // Saboteur stealing one does not come through here).
        protected override bool Collect(PlayerBattery battery)
        {
            bool filled = battery.FillCurrentCell();
            bool spare = battery.AddSpareCell();
            var health = battery.GetComponent<PlayerHealth>();
            bool healed = health != null && health.Heal(repair);
            if (!filled && !spare && !healed)
                return false;

            ScoreManager score = ScoreManager.Current;
            if (score != null)
                score.BatteryPickedUp();
            return true;
        }

        /// <summary>Saboteur action: steal the battery, so it is gone for the player.</summary>
        void ISabotageable.Execute() => Take();
    }
}
