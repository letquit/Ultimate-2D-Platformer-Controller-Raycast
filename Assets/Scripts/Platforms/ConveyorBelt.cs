using System;
using UnityEngine;

[DefaultExecutionOrder(-101)]
public class ConveyorBelt : MonoBehaviour, IVelocityInheritable
{
    [Header("Configuration")] 
    [SerializeField] private float _moveSpeed = 1f;
    [SerializeField] private bool _launchOnExit = true;
    [SerializeField] private bool _checkForWalls = false;

    [Header("Debug")]
    [SerializeField] private bool _showGizmos = true;

    public Vector2 GetVelocity() => transform.right * _moveSpeed;
    public bool ImpartMomentumOnExit { get; set; } = true;
    public bool ProbesShouldLead { get; set; } = false;
    public bool NeedsFuturePositionBoxcastCheck => _checkForWalls;
    public bool LaunchVerticallyOnExit => _launchOnExit;

    private void OnDrawGizmos()
    {
        if (_showGizmos)
        {
            Gizmos.color = Color.yellow;
            Vector3 center = GetComponent<Collider2D>() ? GetComponent<Collider2D>().bounds.center : transform.position;
            Vector3 direction = transform.right * _moveSpeed;
            
            Gizmos.DrawRay(center, direction);

            if (_moveSpeed != 0)
            {
                Vector3 arrowHead = center + direction;
                Vector3 rightWing = Quaternion.Euler(0, 0, 160) * transform.right;
                Vector3 leftWing = Quaternion.Euler(0, 0, -160) * transform.right;
                Gizmos.DrawRay(arrowHead, rightWing * 0.5f);
                Gizmos.DrawRay(arrowHead, leftWing * 0.5f);
            }
        }
    }
}
