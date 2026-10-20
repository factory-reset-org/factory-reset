namespace ToyFactory.Interfaces
{
    /// <summary>Anything the player's blaster can hit: agents, spinning targets, power cores.</summary>
    public interface IDamageable
    {
        /// <summary>Registers one hit. What that does (stun, HP loss, destruction) is up to the implementer.</summary>
        void TakeHit();
    }
}
