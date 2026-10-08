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
        protected override bool Collect(PlayerBattery battery) => battery.AddSpareCell();

        /// <summary>Saboteur action: steal the battery, so it is gone for the player.</summary>
        void ISabotageable.Execute() => gameObject.SetActive(false);
    }
}
