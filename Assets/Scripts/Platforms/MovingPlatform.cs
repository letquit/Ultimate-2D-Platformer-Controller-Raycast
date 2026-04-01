using System;
using UnityEngine;

[DefaultExecutionOrder(-100), SelectionBase]
public class MovingPlatform : MonoBehaviour, IVelocityInheritable
{
    [Header("Configuration")]
    [SerializeField] private Vector3 _moveOffset = new Vector3(0, 3, 0);
    [SerializeField] private float _duration = 2f;
    [SerializeField] private AnimationCurve _easeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private bool _loop = true;
    [SerializeField] private bool _checkForWalls = false;

    [Header("Collision Logic")]
    [SerializeField] private bool _pushPassenger = true;
    [SerializeField] private LayerMask _passengerMask;
    [SerializeField] private float _pushCollisionPadding = 0.015f;
    
    private Vector3 _startPosition;
    private Vector3 _endPosition;
    private float _timer;
    private Rigidbody2D _rb;
    private BoxCollider2D _collider;

    private VisualInterpolator _visuals;
    
    public bool ProbesShouldLead { get; set; } = true;
    public bool ImpartMomentumOnExit { get; set; } = true;
    public bool LaunchVerticallyOnExit { get; } = false;
    
    public Vector3 PositionDelta { get; private set; }

    public bool NeedsFuturePositionBoxcastCheck => _checkForWalls;

    public Vector2 GetVelocity() => PositionDelta / Time.fixedDeltaTime;

    private void Awake()
    {
        _visuals = GetComponentInChildren<VisualInterpolator>();
        _rb = GetComponent<Rigidbody2D>();
        _rb.bodyType = RigidbodyType2D.Kinematic;
        _collider = GetComponent<BoxCollider2D>();

        _startPosition = _rb.position;
        _endPosition = _startPosition + _moveOffset;
    }

    private void FixedUpdate()
    {
        Vector2 oldPos = _rb.position;
        
        _timer += Time.fixedDeltaTime;
        float t = _loop ? Mathf.PingPong(_timer / _duration, 1f) : Mathf.Clamp01(_timer / _duration);
        float easedT = _easeCurve.Evaluate(t);
        Vector2 targetPos = Vector2.Lerp(_startPosition, _endPosition, easedT);
        
        Vector2 moveVector = targetPos - oldPos;

        if (moveVector != Vector2.zero && _pushPassenger)
        {
            PushPassengers(moveVector);
        }

        _rb.position = targetPos;
        PositionDelta = _rb.position - oldPos;
        
        if (_visuals != null) _visuals.UpdatePhysicsState();
    }

    private void PushPassengers(Vector2 moveAmount)
    {
        Bounds bounds = _collider.bounds;
        Vector2 center = bounds.center;
        Vector2 size = bounds.size;

        float distance = moveAmount.magnitude + _pushCollisionPadding;
        Vector2 direction = moveAmount.normalized;

        RaycastHit2D hit = Physics2D.BoxCast(center, size, 0f, direction, distance, _passengerMask);

        if (hit)
        {
            if (hit.transform == transform) return;

            float dot = Vector2.Dot(direction, hit.normal);

            if (dot >= -0.01f)
            {
                return;
            }

            MovementController controller = hit.transform.GetComponent<MovementController>();
            if (controller != null)
            {
                controller.ApplyExternalPush(moveAmount, transform);
            }
        }
    }
    
    private void OnDrawGizmos()
    {
        Vector3 start = Application.isPlaying ? _startPosition : transform.position;
        Vector3 end = start + _moveOffset;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(start, end);
    }
}
