using UnityEngine;
using ToyFactory.Player;
using ToyFactory.Runtime.Effects;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// An overcharge cell: free, fast shots for a short time from the moment it is taken. It
    /// comes back after a minute, as in the prototype.
    /// </summary>
    public sealed class OverchargePickup : PlayerPickup
    {
        protected override Color GlowColour => new Color(1f, 0.79f, 0.2f);

        protected override float GlowSize => 2.2f;

        protected override float GlowOpacity => 0.7f;

        protected override float UsualRespawnSeconds => 60f;

        protected override string TakenWord => "overcharge";

        protected override Sfx TakenSound => Sfx.Mission;

        protected override bool Collect(PlayerBattery battery)
        {
            battery.StartOvercharge();
            return true;
        }
    }
}
