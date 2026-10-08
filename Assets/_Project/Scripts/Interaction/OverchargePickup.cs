using ToyFactory.Player;

namespace ToyFactory.Interaction
{
    /// <summary>An overcharge cell: free shots for a short time from the moment it is taken.</summary>
    public sealed class OverchargePickup : PlayerPickup
    {
        protected override bool Collect(PlayerBattery battery)
        {
            battery.StartOvercharge();
            return true;
        }
    }
}
