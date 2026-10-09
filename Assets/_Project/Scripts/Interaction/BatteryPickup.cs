using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Player;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// A battery cell lying in the level: one more spare cell for the player. A Saboteur
    /// can take it first, through <see cref="ISabotageable.Execute"/>.
    /// </summary>
    public sealed class BatteryPickup : PlayerPickup, ISabotageable
    {
        [Tooltip("The id the Saboteur names this battery by. Must be different for every battery.")]
        [SerializeField, Min(0)] int sabotageId;

        // A battery that has been taken, by the player or a Saboteur, switches itself off,
        // which also takes it out of the list of things a Saboteur can go for.
        void OnEnable() => SabotageTargets.Register(SabotageKind.Battery, sabotageId, this, transform);

        void OnDisable() => SabotageTargets.Unregister(SabotageKind.Battery, sabotageId, this);

        protected override bool Collect(PlayerBattery battery) => battery.AddSpareCell();

        /// <summary>Saboteur action: steal the battery, so it is gone for the player.</summary>
        void ISabotageable.Execute() => gameObject.SetActive(false);
    }
}
