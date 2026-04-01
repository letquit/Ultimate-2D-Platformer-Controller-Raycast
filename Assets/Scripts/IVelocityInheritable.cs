using UnityEngine;

public interface IVelocityInheritable
{
    Vector2 GetVelocity();
    
    bool ProbesShouldLead { get; set; }
    bool ImpartMomentumOnExit { get; set; }
    bool LaunchVerticallyOnExit { get; }
    bool NeedsFuturePositionBoxcastCheck { get; }
}
