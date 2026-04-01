using System;
using System.Numerics;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;
#if UNITY_EDITOR
using UnityEditor;
#endif

public struct CollisionState
{
    public bool IsGrounded;
    public bool WasGroundedLastFrame;

    public bool IsAgainstWall;
    public bool WasAgainstWallLastFrame;
    public int WallDirection;
    public float WallAngle;

    public bool IsOnSlope;
    public float SlopeAngle;
    public Vector2 SlopeNormal;

    public bool IsAgainstSteepSlope;

    public bool IsHittingCeiling;
    public float CeilingAngle;
    public Vector2 CeilingNormal;

    public Vector2 AveragedVisualNormal;
    
    public void Reset()
    {
        WasGroundedLastFrame = IsGrounded;
        WasAgainstWallLastFrame = IsAgainstWall;

        IsGrounded = false;
        IsAgainstWall = false;
        WallDirection = 0;
        WallAngle = 0f;

        IsOnSlope = false;
        SlopeAngle = 0f;
        SlopeNormal = Vector2.zero;

        IsAgainstSteepSlope = false;

        IsHittingCeiling = false;
        CeilingAngle = 0f;
        CeilingNormal = Vector2.zero;
        
        AveragedVisualNormal = Vector2.up;
    }
}

[RequireComponent(typeof(PlayerMovement))]
public class MovementController : MonoBehaviour
{
    public bool IsSliding => _internalState.IsOnSlope && _internalState.SlopeAngle > _moveStats.MaxSlopeAngle;
    
    public const float CollisionPadding = 0.015f;
    private const float AIRBORNE_ANGLE_MEMORY = -999f;

    [Range(2, 100)] public int NumOfHorizontalRays = 4;
    [Range(2, 100)] public int NumOfVerticalRays = 4;
    public int NumOfVerticalRaysForVisualNormals;

    [Header("Sensors")]
    [SerializeField] private float _verticalProbeDistance = 0.1f;
    [SerializeField] private float _horizontalProbeDistance = 0.1f;

    [Header("Safety")] 
    [SerializeField] private float _safetyGraceDuration = 0.08f;
    
    private float _horizontalRaySpace;
    private float _verticalRaySpace;

    private BoxCollider2D _coll;
    public RaycastCorners RayCastCorners;
    private PlayerMovementStats _moveStats;

    public Action OnCrush;
    
    public bool IsClimbingSlope { get; private set; }
    public bool WasClimbingSlopeLastFrame { get; private set; }
    public bool IsDescendingSlope { get; private set; }
    public float SlopeAngle { get; private set; }
    public Vector2 SlopeNormal { get; private set; }
    public float LastLandingTime { get; set; }
    public int FaceDirection { get; private set; }

    private bool _isCornerCorrectingThisFrame;
    private bool _isHorizontalCornerCorrectingThisFrame;

    private float _rearCornerSlopeAngle;
    private float _slopeCurveAccumulator;

    private bool _forceAirBorneNextFrame;

    private PlayerMovement _playerMovement;
    private Rigidbody2D _rb;
    
    public CollisionState State { get; private set; }
    private CollisionState _internalState;

    private float _lastSafetyGroundFixedTime = -Mathf.Infinity;
    private RaycastHit2D _lastSafetyGroundHit;

    private bool _wasPushedThisFrame;
    private Vector2 _pushAmountThisFrame;

    private Collider2D[] _overlapBuffer = new Collider2D[1];

    public IVelocityInheritable LastKnownPlatform;
    public IVelocityInheritable PlatformFromLastFrame { get; private set; }
    public bool IsOnPlatform => LastKnownPlatform != null;
    
    public struct RaycastCorners
    {
        public Vector2 topLeft;
        public Vector2 topRight;
        public Vector2 bottomLeft;
        public Vector2 bottomRight;
    }

    private void Awake()
    {
        _coll = GetComponent<BoxCollider2D>();
        _rb = GetComponent<Rigidbody2D>();
        _playerMovement = GetComponent<PlayerMovement>();
        _moveStats = _playerMovement.MoveStats;

        FaceDirection = 1;
    }

    private void Start()
    {
        CalculateRaySpacing();
    }

    public void PollSensors(Vector2 moveDelta)
    {
        _internalState.Reset();

        IVelocityInheritable predictedPlatform = LastKnownPlatform;
        PlatformFromLastFrame = LastKnownPlatform;
        LastKnownPlatform = null;
        
        UpdateRayCastCorners();

        if (moveDelta.x != 0)
        {
            FaceDirection = (int)Mathf.Sign(moveDelta.x);
        }

        IVelocityInheritable foundWallPlatform;
        HorizontalProbes(moveDelta, predictedPlatform, out foundWallPlatform);

        CeilingProbes(moveDelta);

        IVelocityInheritable foundGroundPlatform;
        if (_forceAirBorneNextFrame)
        {
            _internalState.IsGrounded = false;
            _forceAirBorneNextFrame = false;
            foundGroundPlatform = null;
        }
        else
        {
            GroundProbes(moveDelta, predictedPlatform, out foundGroundPlatform);
        }

        if (_internalState.IsGrounded)
        {
            LastKnownPlatform = foundGroundPlatform;
        }
        else if (_internalState.IsAgainstWall)
        {
            LastKnownPlatform = foundWallPlatform;
        }
        else
        {
            LastKnownPlatform = null;
        }

        if (_internalState.IsHittingCeiling && DetectHeadCornerCorrection(moveDelta))
        {
            _internalState.IsHittingCeiling = false;
        }

        if (_moveStats.MatchVisualsToSlope)
        {
            DetectSlopeNormalsForVisuals(moveDelta);
        }
        
        State = _internalState;

        // Sanity check to catch any cases where the player might be considered not grounded while actually being grounded
        // if (moveDelta.x != 0f && !State.IsGrounded)
        // {
        //     Debug.Break();
        // }
    }

    public void Move(Vector2 velocity)
    {
        Vector2 platformMoveAmount = Vector2.zero;

        if (IsOnPlatform)
        {
            Vector2 platformVel = LastKnownPlatform.GetVelocity();
            platformMoveAmount = platformVel * Time.fixedDeltaTime;
            
            // wall guard
            if (platformMoveAmount.x != 0 && LastKnownPlatform.NeedsFuturePositionBoxcastCheck)
            {
                float directionX = Mathf.Sign(platformMoveAmount.x);
                float distance = Mathf.Abs(platformMoveAmount.x) + CollisionPadding;
                Vector2 origin = _coll.bounds.center;
                Vector2 size = _coll.bounds.size - (Vector3.one * CollisionPadding * 2f);

                RaycastHit2D hit = Physics2D.BoxCast(origin, size, 0f, Vector3.right * directionX, distance,
                    _moveStats.GroundLayer);

                if (hit)
                {
                    float adjustedDist = Mathf.Max(0, hit.distance - CollisionPadding);
                    platformVel.x = (adjustedDist * directionX) / Time.fixedDeltaTime;
                }
            }

            if (_internalState.IsAgainstWall && _internalState.IsGrounded)
            {
                float wallNormalX = -_internalState.WallDirection;

                if (Vector2.Dot(platformVel, new Vector2(wallNormalX, 0f)) < -0.01f)
                {
                    platformVel.x = 0f;
                }
            }

            if (_internalState.IsGrounded)
            {
                if (Vector2.Dot(platformVel, _internalState.SlopeNormal) > 0.01f)
                {
                    platformVel.y = 0f;
                }
            }

            if (_wasPushedThisFrame)
            {
                if (Mathf.Abs(_pushAmountThisFrame.x) > 0f) platformVel.x = 0f;
                if (Mathf.Abs(_pushAmountThisFrame.y) > 0f) platformVel.y = 0f;
            }

            platformMoveAmount = platformVel * Time.fixedDeltaTime;
            _rb.position += platformMoveAmount;
        }
        
        UpdateRayCastCorners();
        ResetCollisionStates();

        if (velocity.y <= 0f && !_playerMovement.IsDashing && !WasClimbingSlopeLastFrame)
        {
            DescendSlope(ref velocity);
        }
        
        ApplyHorizontalCornerCorrection(ref velocity);
        ApplyHeadCornerCorrection(ref velocity);

        ResolveHorizontalMovement(ref velocity);
        ResolveVerticalMovement(ref velocity);
        
        _rb.MovePosition(_rb.position + velocity);

        _wasPushedThisFrame = false;
        _pushAmountThisFrame = Vector2.zero;
    }

    private void GroundProbes(Vector2 moveDelta, IVelocityInheritable lastKnownPlatform, out IVelocityInheritable foundPlatform)
    {
        foundPlatform = null;
        
        float rayLength = _verticalProbeDistance + CollisionPadding;

        if (lastKnownPlatform != null && lastKnownPlatform.ProbesShouldLead)
        {
            float platformMoveDist = Mathf.Abs(lastKnownPlatform.GetVelocity().y * Time.fixedDeltaTime);
            rayLength += platformMoveDist;
        }

        float smallestHitDistance = float.MaxValue;
        bool foundGround = false;
        bool foundWalkableGround = false;
        RaycastHit2D groundHit = new RaycastHit2D();

        float horizontalProjection = 0f;

        Vector2 platformOffset = Vector2.zero;
        if (lastKnownPlatform != null && lastKnownPlatform.ProbesShouldLead)
        {
            platformOffset = lastKnownPlatform.GetVelocity() * Time.fixedDeltaTime;
        }
        
        if (moveDelta.y <= 0)
        {
            horizontalProjection = moveDelta.x;
            if (_internalState.IsAgainstWall || _internalState.IsAgainstSteepSlope)
            {
                horizontalProjection = 0f;
            }
        }

        for (int i = 0; i < NumOfVerticalRays; i++)
        {
            Vector2 rayOrigin = RayCastCorners.bottomLeft +
                                Vector2.right * (_verticalRaySpace * i + horizontalProjection);
            rayOrigin += platformOffset;
            
            RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.down, rayLength, _moveStats.GroundLayer);

            #region Debug Visualization

            if (_moveStats.DebugShowIsGrounded)
            {
                bool didHit = hit;
                Color rayColor = didHit ? Color.cyan : Color.red;
                Debug.DrawRay(rayOrigin, Vector2.down * rayLength, rayColor);
            }

            #endregion

            if (hit)
            {
                float hitAngle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.up));
                bool isHitWalkable = hitAngle < _moveStats.MaxSlopeAngle;

                if (!foundGround)
                {
                    smallestHitDistance = hit.distance;
                    groundHit = hit;
                    foundGround = true;
                    foundWalkableGround = isHitWalkable;
                }
                else
                {
                    if (!foundWalkableGround && isHitWalkable)
                    {
                        smallestHitDistance = hit.distance;
                        groundHit = hit;
                        foundWalkableGround = true;
                    }
                    else if (foundWalkableGround == isHitWalkable && hit.distance < smallestHitDistance)
                    {
                        smallestHitDistance = hit.distance;
                        groundHit = hit;
                    }
                }
            }
            else if (IsClimbingSlope || IsDescendingSlope && !IsOnPlatform)
            {
                _internalState.IsGrounded = true;
                _internalState.SlopeAngle = SlopeAngle;
                _internalState.SlopeNormal = SlopeNormal;
                if (_internalState.SlopeAngle > 0.01f)
                {
                    _internalState.IsOnSlope = true;
                }
                
                continue;
            }
        }

        if (foundGround)
        {
            var platform = groundHit.collider.GetComponent<IVelocityInheritable>();
            if (platform != null) foundPlatform = platform;
            
            float slopeAngle = Mathf.Round(Vector2.Angle(groundHit.normal, Vector2.up));
            
            bool isVerticalWall = Mathf.Abs(groundHit.normal.y) < Mathf.Epsilon;
            
            bool isWallSlideable = _playerMovement.IsWallSlideable(slopeAngle) && !isVerticalWall;
            bool isFacingWall = Mathf.Sign(groundHit.normal.x) != FaceDirection;

            bool shouldWallSlide = isFacingWall || _moveStats.CanWallSlideFacingAwayFormWall;

            if (isWallSlideable && shouldWallSlide)
            {
                _internalState.IsAgainstWall = true;
                _internalState.WallAngle = slopeAngle;
                _internalState.WallDirection = (int)-Mathf.Sign(groundHit.normal.x);
            }
            else
            {
                _internalState.IsGrounded = true;
                _internalState.SlopeAngle = slopeAngle;
                _internalState.SlopeNormal = groundHit.normal;
                if (slopeAngle > 0.01f)
                {
                    _internalState.IsOnSlope = true;
                }
            }
        }
        else
        {
            if (WasClimbingSlopeLastFrame)
            {
                float smallestSafetyDistance = float.MaxValue;
                bool foundSafetyGround = false;
                RaycastHit2D safetyGroundHit = new RaycastHit2D();

                for (int i = 0; i < NumOfVerticalRays; i++)
                {
                    Vector2 rayOriginSafety = RayCastCorners.bottomLeft + Vector2.right * (_verticalRaySpace * i);
                    RaycastHit2D hitSafety = Physics2D.Raycast(rayOriginSafety, Vector2.down, rayLength * 5f, _moveStats.GroundLayer);
                    if (hitSafety)
                    {
                        safetyGroundHit = hitSafety;
                        smallestSafetyDistance = safetyGroundHit.distance;
                        foundSafetyGround = true;
                        break;
                    }
                }

                if (foundSafetyGround)
                {
                    var platform = safetyGroundHit.collider.GetComponent<IVelocityInheritable>();
                    if (platform != null) foundPlatform = platform;

                    float slopeAngle = Mathf.Round(Vector2.Angle(safetyGroundHit.normal, Vector2.up));
                    if (!_playerMovement.IsWallSlideable(slopeAngle))
                    {
                        _lastSafetyGroundFixedTime = Time.fixedTime;
                        _lastSafetyGroundHit = safetyGroundHit;
                        _internalState.IsGrounded = true;
                        _internalState.SlopeAngle = slopeAngle;
                        _internalState.SlopeNormal = safetyGroundHit.normal;
                        if (slopeAngle > 0.01f)
                        {
                            _internalState.IsOnSlope = true;
                        }
                    }
                }
            }

            // bool usedRecentSafety = false;
            if (Time.fixedTime - _lastSafetyGroundFixedTime <= _safetyGraceDuration)
            {
                float reuseSlopeAngle = Mathf.Round(Vector2.Angle(_lastSafetyGroundHit.normal, Vector2.up));
                if (!_playerMovement.IsWallSlideable(reuseSlopeAngle))
                {
                    // usedRecentSafety = true;
                    _internalState.IsGrounded = true;
                    _internalState.SlopeAngle = reuseSlopeAngle;
                    _internalState.SlopeNormal = _lastSafetyGroundHit.normal;
                    if (reuseSlopeAngle > 0.01f)
                    {
                        _internalState.IsOnSlope = true;
                    }
                }
            }

            // if (!foundGround && !usedRecentSafety)
            // {
            //     _internalState.IsGrounded = false;
            //     _internalState.IsOnSlope = false;
            // }
        }

        #region Debug SLope Normal

        if (_moveStats.DebugShowSlopeNormal)
        {
            Vector2 drawOrigin = new Vector2(_coll.bounds.center.x, _coll.bounds.min.y);
            float drawLength = _moveStats.ExtraRayDebugDistance * 3f;
            
            Debug.DrawRay(drawOrigin, _internalState.SlopeNormal * drawLength, Color.yellow);
        }

        #endregion
    }

    private void CeilingProbes(Vector2 moveDelta)
    {
        if (moveDelta.y >= 0)
        {
            float rayLength = _verticalProbeDistance + CollisionPadding;

            for (int i = 0; i < NumOfVerticalRays; i++)
            {
                Vector2 rayOrigin = RayCastCorners.topLeft + Vector2.right * (_verticalRaySpace * i);
                RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.up, rayLength, _moveStats.GroundLayer);

                if (hit)
                {
                    float rawHitAngle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.down));

                    if (rawHitAngle > 85f)
                    {
                        continue;
                    }

                    bool isEdgeRay = (i == 0 || i == NumOfVerticalRays - 1);
                    if (isEdgeRay && hit.distance == 0 && rawHitAngle == 0)
                    {
                        continue;
                    }

                    float currentCeilingAngle;
                    if (hit.distance == 0f)
                    {
                        Vector2 safetyRayOrigin = rayOrigin + (Vector2.down * CollisionPadding * 2);
                        RaycastHit2D safetyHit = Physics2D.Raycast(safetyRayOrigin, Vector2.up, CollisionPadding * 3,
                            _moveStats.GroundLayer);

                        if (safetyHit)
                        {
                            currentCeilingAngle = Mathf.Round(Vector2.Angle(safetyHit.normal, Vector2.down));
                            _internalState.CeilingNormal = safetyHit.normal;
                        }
                        else
                        {
                            currentCeilingAngle = 0f;
                            _internalState.CeilingNormal = Vector2.down;
                        }
                    }

                    else
                    {
                        currentCeilingAngle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.down));
                        _internalState.CeilingNormal = hit.normal;
                    }

                    _internalState.IsHittingCeiling = true;

                    if (currentCeilingAngle > _internalState.CeilingAngle)
                    {
                        _internalState.CeilingAngle = currentCeilingAngle;
                        _internalState.CeilingNormal = hit.normal;
                    }
                }

                #region Debug Visualization

                if (_moveStats.DebugShowHeadRays)
                {
                    float drawLength = hit ? hit.distance : (rayLength + _moveStats.ExtraRayDebugDistance);
                    
                    Color rayColor = hit ? Color.cyan : Color.red;

                    if (i == 0 || i == NumOfVerticalRays - 1)
                    {
                        rayColor = hit ? Color.green : Color.magenta;
                    }

                    Debug.DrawRay(rayOrigin, Vector2.up * drawLength, rayColor);
                }

                #endregion
            }
        }
    }

    private void HorizontalProbes(Vector2 moveDelta, IVelocityInheritable lastKnownPlatform, out IVelocityInheritable foundPlatform)
    {
        foundPlatform = null;
        
        float rayLength = Mathf.Abs(moveDelta.x) + CollisionPadding;
        if (rayLength < _horizontalProbeDistance)
        {
            rayLength = _horizontalProbeDistance;
        }

        if (lastKnownPlatform != null && lastKnownPlatform.ProbesShouldLead)
        {
            float platformMoveDist = Mathf.Abs(lastKnownPlatform.GetVelocity().x * Time.fixedDeltaTime);
            rayLength += platformMoveDist;
        }
        
        Vector2 platformOffset = Vector2.zero;
        if (lastKnownPlatform != null && lastKnownPlatform.ProbesShouldLead)
        {
            platformOffset = lastKnownPlatform.GetVelocity() * Time.fixedDeltaTime;
        }

        for (int i = 0; i < NumOfHorizontalRays; i++)
        {
            // check left
            Vector2 rayOriginLeft = RayCastCorners.bottomLeft + Vector2.up * (_horizontalRaySpace * i);
            if (Vector2.Dot(platformOffset, Vector2.left) > 0)
            {
                rayOriginLeft += platformOffset;
            }
            
            RaycastHit2D hitLeft = Physics2D.Raycast(rayOriginLeft, Vector2.left, rayLength, _moveStats.GroundLayer);

            #region Debug Visualization

            if (_moveStats.DebugShowWallHit)
            {
                Debug.DrawRay(rayOriginLeft, Vector2.left * rayLength, hitLeft ? Color.cyan : Color.red);
            }

            #endregion
            
            bool foundWall = false;
            if (hitLeft)
            {
                float wallAngle = Mathf.Round(Vector2.Angle(hitLeft.normal, Vector2.up));

                if (_playerMovement.IsSlideableSlope(wallAngle))
                {
                    _internalState.IsAgainstSteepSlope = true;
                }

                if (_playerMovement.IsWallSlideable(wallAngle))
                {
                    _internalState.IsAgainstWall = true;
                    _internalState.WallDirection = -1;
                    _internalState.WallAngle = wallAngle;
                    foundWall = true;
                }

                var platform = hitLeft.collider.GetComponent<IVelocityInheritable>();
                if (platform != null) foundPlatform = platform;
            }
            
            // check right
            Vector2 rayOriginRight = RayCastCorners.bottomRight + Vector2.up * (_horizontalRaySpace * i);
            if (Vector2.Dot(platformOffset, Vector2.right) > 0)
            {
                rayOriginRight += platformOffset;
            }
            
            RaycastHit2D hitRight = Physics2D.Raycast(rayOriginRight, Vector2.right, rayLength, _moveStats.GroundLayer);

            #region Debug Visualization

            if (_moveStats.DebugShowWallHit)
            {
                Debug.DrawRay(rayOriginRight, Vector2.right * rayLength, hitRight ? Color.cyan : Color.red); 
            }
            
            #endregion
            
            if (hitRight)
            {
                float wallAngle = Mathf.Round(Vector2.Angle(hitRight.normal, Vector2.up));

                if (_playerMovement.IsSlideableSlope(wallAngle))
                {
                    _internalState.IsAgainstSteepSlope = true;
                }

                if (_playerMovement.IsWallSlideable(wallAngle))
                {
                    _internalState.IsAgainstWall = true;
                    _internalState.WallDirection = 1;
                    _internalState.WallAngle = wallAngle;
                    foundWall = true;
                }

                var platform = hitRight.collider.GetComponent<IVelocityInheritable>();
                if (platform != null) foundPlatform = platform;
            }
            
            if (foundWall) break;
        }
    }
    
    private void ResetCollisionStates()
    {
        WasClimbingSlopeLastFrame = IsClimbingSlope;
        IsClimbingSlope = false;
        IsDescendingSlope = false;
        SlopeAngle = 0f;
        SlopeNormal = Vector2.zero;
        _isCornerCorrectingThisFrame = false;
        _isHorizontalCornerCorrectingThisFrame = false;

        if (!_internalState.IsGrounded)
        {
            _slopeCurveAccumulator = 0f;
        }
    }

    private void ResolveVerticalMovement(ref Vector2 velocity)
    {
        #region CeilingCheck

        if (velocity.y > 0f)
        {
            if (_isCornerCorrectingThisFrame) return;
            
            float upwardRayLength = Mathf.Abs(velocity.y) + CollisionPadding;
            for (int i = 0; i < NumOfVerticalRays; i++)
            {
                Vector2 rayOrigin = RayCastCorners.topLeft;
                rayOrigin += Vector2.right * (_verticalRaySpace * i);
                
                RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.up, upwardRayLength, _moveStats.GroundLayer);

                if (hit)
                {
                    float angle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.down));

                    if (hit.distance == 0)
                    {
                        Vector2 shiftDir = Vector2.zero;

                        if (i == 0) shiftDir = Vector2.right * CollisionPadding * 2f;
                        else if (i == NumOfVerticalRays - 1) shiftDir = Vector2.left * CollisionPadding * 2f;

                        if (shiftDir != Vector2.zero)
                        {
                            Vector2 safetyOrigin = rayOrigin + shiftDir;

                            RaycastHit2D safetyHit = Physics2D.Raycast(safetyOrigin, Vector2.up, upwardRayLength,
                                _moveStats.GroundLayer);

                            if (safetyHit)
                            {
                                angle = Mathf.Round(Vector2.Angle(safetyHit.normal, Vector2.down));
                            }
                            else
                            {
                                angle = 90f;
                            }
                        }
                    }

                    if (angle > 85f)
                    {
                        continue;
                    }

                    if (hit.distance == 0)
                    {
                        velocity.y = 0f;
                    }
                    else
                    {
                        velocity.y = (hit.distance - CollisionPadding);
                        upwardRayLength = hit.distance;
                    }
                }
            }
        }

        #endregion

        #region GroundCheck

        float downwardRayLength = Mathf.Abs(velocity.y) + CollisionPadding;

        if (LastKnownPlatform != null && LastKnownPlatform.ProbesShouldLead)
        {
            downwardRayLength += Mathf.Abs(LastKnownPlatform.GetVelocity().y * Time.fixedDeltaTime);
        }

        float smallestHitDistance = float.MaxValue;
        RaycastHit2D groundHit = new RaycastHit2D();
        bool foundGround = false;
        bool foundWalkableGround = false;
        
        for (int i = 0; i < NumOfVerticalRays; i++)
        {
            Vector2 rayOrigin = RayCastCorners.bottomLeft;
            rayOrigin += Vector2.right * (_verticalRaySpace * i + velocity.x);

            RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.down, downwardRayLength, _moveStats.GroundLayer);
            
            if (hit)
            {
                float hitAngle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.up));

                bool isHitWalkable = hitAngle < _moveStats.MaxSlopeAngle;
                bool isSinking = hit.distance < CollisionPadding;

                if (!isSinking && !isHitWalkable)
                {
                    if (IsSliding || _internalState.IsGrounded && _internalState.SlopeAngle < _moveStats.MaxSlopeAngle)
                    {
                        continue;
                    }
                }

                if (!foundGround)
                {
                    smallestHitDistance = hit.distance;
                    groundHit = hit;
                    foundGround = true;
                    foundWalkableGround = isHitWalkable;
                }
                else
                {
                    if (!foundWalkableGround && isHitWalkable)
                    {
                        smallestHitDistance = hit.distance;
                        groundHit = hit;
                        foundWalkableGround = true;
                    }
                    else if (foundWalkableGround == isHitWalkable && hit.distance < smallestHitDistance)
                    {
                        smallestHitDistance = hit.distance;
                        groundHit = hit;
                    }
                }
            }
        }

        if (foundGround)
        {
            float calculation = (groundHit.distance - CollisionPadding) * -1;
            if (calculation > 0 && calculation <= CollisionPadding + 0.001f)
            {
                
            }
            else
            {
                if (Mathf.Abs(calculation) <= 0.001f)
                {
                    calculation = 0f;
                }
            }
            
            if (velocity.y <= 0)
            {
                float distanceToFloor = groundHit.distance - CollisionPadding;
                if (IsSliding && distanceToFloor > 0f && _internalState.SlopeAngle < 89.9f)
                {
                    float slopeAngleRad = _internalState.SlopeAngle * Mathf.Deg2Rad;
                    float pushOutX = distanceToFloor / Mathf.Tan(slopeAngleRad);
                    velocity.x += pushOutX * Mathf.Sign(_internalState.SlopeNormal.x);
                }
                
                velocity.y = calculation;
            }
            else if (IsClimbingSlope)
            {
                velocity.y += calculation;
            }
        }
        
        #endregion
        
        if (IsClimbingSlope)
        {
            float directionX = Mathf.Sign(velocity.x);
            float rayLength = Mathf.Abs(velocity.x) + CollisionPadding;
            Vector2 rayOrigin = ((directionX == -1 ? RayCastCorners.bottomLeft : RayCastCorners.bottomRight) +
                                 Vector2.up * velocity.y);
            RaycastHit2D hit =
                Physics2D.Raycast(rayOrigin, Vector2.right * directionX, rayLength, _moveStats.GroundLayer);

            if (hit)
            {
                float slopeAngle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.up));
                if (slopeAngle != SlopeAngle)
                {
                    velocity.x = (hit.distance - CollisionPadding) * directionX;
                    SlopeAngle = slopeAngle;
                    SlopeNormal = hit.normal;
                }
            }
        }
    }

    private void ResolveHorizontalMovement(ref Vector2 velocity)
    {
        if (_isHorizontalCornerCorrectingThisFrame) return;
        
        float originalVelocityX = velocity.x;
        
        float directionX = Mathf.Sign(velocity.x);
        if (velocity.x == 0f)
        {
            directionX = FaceDirection;
        }
        float rayLength = Mathf.Abs(velocity.x) + CollisionPadding;

        if (Mathf.Abs(velocity.x) < CollisionPadding)
        {
            rayLength = CollisionPadding * 2;
        }

        if (LastKnownPlatform != null && LastKnownPlatform.ProbesShouldLead)
        {
            rayLength += Mathf.Abs(LastKnownPlatform.GetVelocity().x * Time.fixedDeltaTime);
        }

        for (int i = 0; i < NumOfHorizontalRays; i++)
        {
            Vector2 rayOrigin = (directionX == -1) ? RayCastCorners.bottomLeft : RayCastCorners.bottomRight;
            rayOrigin += Vector2.up * (_horizontalRaySpace * i);
            RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.right * directionX, rayLength, _moveStats.GroundLayer);

            if (hit)
            {
                // priority 1 - climb slopes
                float slopeAngle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.up));
                bool isLowerBodyHit = i <= (NumOfHorizontalRays / 2);
                if (isLowerBodyHit && _playerMovement.IsWalkableSlope(slopeAngle) && slopeAngle > 0f)
                {
                    ClimbSlope(ref velocity, slopeAngle, hit.normal, originalVelocityX);
                    continue;
                }

                if (IsClimbingSlope && slopeAngle <= _moveStats.MaxSlopeAngle)
                {
                    continue;
                }
                
                // priority 2 - step up
                if (i == 0 && hit.distance > 0.001f && _internalState.IsGrounded)
                {
                    if (AttemptStepUp(hit, ref velocity, directionX, originalVelocityX))
                    {
                        break;
                    }
                }
                
                // priority 3 - shin guard
                if (i == 0)
                {
                    Vector2 shinOrigin = rayOrigin + (Vector2.up * _horizontalRaySpace);
                    RaycastHit2D shinHit = Physics2D.Raycast(shinOrigin, Vector2.right * directionX, rayLength,
                        _moveStats.GroundLayer);

                    if (shinHit)
                    {
                        float shinAngle = Mathf.Round(Vector2.Angle(shinHit.normal, Vector2.up));
                        if (shinAngle <= _moveStats.MaxSlopeAngle)
                        {
                            ClimbSlope(ref velocity, shinAngle, shinHit.normal, originalVelocityX);
                            continue;
                        }
                    }
                }
                
                // rest
                bool isSlideableSlope = slopeAngle > _moveStats.MaxSlopeAngle && slopeAngle < _moveStats.MinAngleForWallSlide;

                if (isSlideableSlope)
                {
                    velocity.x = (hit.distance - CollisionPadding) * directionX;

                    if (IsClimbingSlope)
                    {
                        velocity.y = Mathf.Tan(SlopeAngle * Mathf.Deg2Rad) * Mathf.Abs(velocity.x);
                    }
                    
                    continue;
                }

                if (IsSliding)
                {
                    bool isWall = _playerMovement.IsWallSlideable(slopeAngle);
                    if (!isWall)
                    {
                        continue;
                    }
                }
                
                velocity.x = (hit.distance - CollisionPadding) * directionX;
                rayLength = hit.distance;

                if (IsClimbingSlope)
                {
                    velocity.y = Mathf.Tan(SlopeAngle * Mathf.Deg2Rad) * Mathf.Abs(velocity.x);
                }
            }
        }
    }
    
    private void UpdateRayCastCorners()
    {
        Bounds bounds = _coll.bounds;
        bounds.Expand(CollisionPadding * -2);

        RayCastCorners.bottomLeft = new Vector2(bounds.min.x, bounds.min.y);
        RayCastCorners.bottomRight = new Vector2(bounds.max.x, bounds.min.y);
        RayCastCorners.topLeft = new Vector2(bounds.min.x, bounds.max.y);
        RayCastCorners.topRight = new Vector2(bounds.max.x, bounds.max.y);
    }

    private void CalculateRaySpacing()
    {
        Bounds bounds = _coll.bounds;
        bounds.Expand(CollisionPadding * -2);

        _horizontalRaySpace = bounds.size.y / (NumOfHorizontalRays - 1);
        _verticalRaySpace = bounds.size.x / (NumOfVerticalRays - 1);
    }

    #region Slopes

    private void ClimbSlope(ref Vector2 velocity, float slopeAngle, Vector2 slopeNormal, float originalInputX)
    {
        float moveDistance = Mathf.Abs(originalInputX);
        float climbVelocityY = Mathf.Sign(slopeAngle * Mathf.Deg2Rad) * moveDistance;

        if (velocity.y <= climbVelocityY)
        {
            velocity.y = climbVelocityY;
            velocity.x = Mathf.Cos(slopeAngle * Mathf.Deg2Rad) * moveDistance * Mathf.Sign(velocity.x);

            IsClimbingSlope = true;
            SlopeAngle = slopeAngle;
            SlopeNormal = slopeNormal;
            _rearCornerSlopeAngle = slopeAngle;
            _slopeCurveAccumulator = 0f;
        }
        else
        {
            float dot = Vector2.Dot(velocity.normalized, slopeNormal);
            if (Mathf.Abs(dot) < 0.05f && _playerMovement.IsDashing)
            {
                IsClimbingSlope = true;
                SlopeAngle = slopeAngle;
                SlopeNormal = slopeNormal;
                _rearCornerSlopeAngle = slopeAngle;
                _slopeCurveAccumulator = 0f;
            }
        }
    }

    private void DescendSlope(ref Vector2 velocity)
    {
        float directionX = FaceDirection;
        
        Vector2 rayOrigin = (directionX == -1) ? RayCastCorners.bottomRight : RayCastCorners.bottomLeft;

        float maxExpectedVerticalDrop = Mathf.Tan(_moveStats.MinAngleForWallSlide * Mathf.Deg2Rad) * Mathf.Abs(velocity.x);
        float dynamicRayLength = Mathf.Abs(velocity.y) + CollisionPadding + maxExpectedVerticalDrop;
        float rayLength = Mathf.Max(dynamicRayLength, Mathf.Abs(velocity.y), CollisionPadding * 2f);

        RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.down, rayLength, _moveStats.GroundLayer);

        #region Debug

        if (_moveStats.DebugShowDescendSlopeRay)
        {
            bool isSticking = IsDescendingSlope;
            Color rayColor = Color.blue;
            
            if (hit && isSticking) rayColor = Color.green;
            else if (hit && IsSliding) rayColor = Color.yellow;
            else if (hit && !isSticking) rayColor = Color.red;

            float debugRayLength =
                hit ? hit.distance + _moveStats.ExtraRayDebugDistance : _moveStats.ExtraRayDebugDistance * 5f;
            Debug.DrawRay(rayOrigin, Vector2.down * debugRayLength, rayColor);
        }

        #endregion

        if (hit)
        {
            float slopeAngle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.up));
            float horizSpeed = Mathf.Abs(velocity.x) / Time.fixedDeltaTime;

            bool inLandingGrace = (Time.time - LastLandingTime) < _moveStats.LandingGraceTime;
            bool wasAirborne = _rearCornerSlopeAngle == AIRBORNE_ANGLE_MEMORY;
            float angleDelta = wasAirborne ? 0f : slopeAngle - _rearCornerSlopeAngle;

            int wallDirection = (int)Mathf.Sign(hit.normal.x);
            bool isFacingWall = (wallDirection == -1 && _playerMovement.IsFacingRight) ||
                                (wallDirection == 1 && !_playerMovement.IsFacingRight);

            bool isNormalSlideableSlope = _playerMovement.IsSlideableSlope(slopeAngle);
            bool isWallSlope = slopeAngle >= _moveStats.MinAngleForWallSlide;
            bool shouldSlide = isNormalSlideableSlope || (isWallSlope && !isFacingWall);

            if (!wasAirborne && angleDelta > 0.1f)
            {
                _slopeCurveAccumulator += angleDelta;
            }
            else
            {
                _slopeCurveAccumulator -= _moveStats.SlopeCurveDecayRate * Time.fixedDeltaTime;
            }

            _slopeCurveAccumulator = Mathf.Clamp(_slopeCurveAccumulator, 0f, 180f);
            
            // landing check
            if (inLandingGrace && wasAirborne)
            {
                if (shouldSlide)
                {
                    SlideDownMaxSlope(hit, ref velocity);
                    _rearCornerSlopeAngle = slopeAngle;
                    return;
                }
                else if (slopeAngle <= _moveStats.MaxSlopeAngle)
                {
                    ApplySlopeStick(ref velocity, slopeAngle, hit);
                    _rearCornerSlopeAngle = slopeAngle;
                }
                else
                {
                    _rearCornerSlopeAngle = slopeAngle;
                }
            }
            
            // dramatic angle change
            else if (!wasAirborne && angleDelta >= _moveStats.MaxAngleDeltaForRunOff)
            {
                _rearCornerSlopeAngle = AIRBORNE_ANGLE_MEMORY;
                _forceAirBorneNextFrame = true;
            }
            
            // speed check
            else if (!wasAirborne && (angleDelta >= _moveStats.MinAngleDeltaForRunOff ||
                                      _slopeCurveAccumulator > _moveStats.MaxSlopeCurveAccumulation))
            {
                if (horizSpeed >= _moveStats.SpeedForRunOff)
                {
                    _rearCornerSlopeAngle = AIRBORNE_ANGLE_MEMORY;
                    _forceAirBorneNextFrame = true;
                    _slopeCurveAccumulator = 0f;
                }
                else
                {
                    if (shouldSlide)
                    {
                        SlideDownMaxSlope(hit, ref velocity);
                        _rearCornerSlopeAngle = slopeAngle;
                        return;
                    }
                    else if (slopeAngle <= _moveStats.MaxSlopeAngle)
                    {
                        ApplySlopeStick(ref velocity, slopeAngle, hit);
                        _rearCornerSlopeAngle = slopeAngle;
                    }
                    else
                    {
                        _rearCornerSlopeAngle = slopeAngle;
                    }
                }
            }
            
            // airborne runOff
            else if (wasAirborne && !inLandingGrace)
            {
                float checkDirection = (Mathf.Abs(velocity.x) < 0.01f) ? FaceDirection : Mathf.Sign(velocity.x);
                bool isMovingDownSlope = Mathf.Sign(hit.normal.x) == checkDirection;

                if (!isMovingDownSlope)
                {
                    _slopeCurveAccumulator = 0f;
                }
                
                if (shouldSlide && isMovingDownSlope)
                {
                    SlideDownMaxSlope(hit, ref velocity);
                    _rearCornerSlopeAngle = slopeAngle;
                    return;
                }
                else if (slopeAngle <= _moveStats.MaxSlopeAngle)
                {
                    _rearCornerSlopeAngle = slopeAngle;
                }
                else
                {
                    _rearCornerSlopeAngle = AIRBORNE_ANGLE_MEMORY;
                }
            }
            
            // steep slope
            else if (slopeAngle > _moveStats.MaxSlopeAngle)
            {
                float checkDirection = (Mathf.Abs(velocity.x) < 0.01f) ? FaceDirection : Mathf.Sign(velocity.x);
                bool isMovingDownSlope = Mathf.Sign(hit.normal.x) == checkDirection;

                if (!isMovingDownSlope)
                {
                    _slopeCurveAccumulator = 0f;
                    _rearCornerSlopeAngle = slopeAngle;
                }
                else if (slopeAngle < _moveStats.MinAngleForWallSlide)
                {
                    SlideDownMaxSlope(hit, ref velocity);
                    _rearCornerSlopeAngle = slopeAngle;
                    return;
                }
                else
                {
                    _rearCornerSlopeAngle = slopeAngle;
                }
            }

            // standard walkable slope
            else
            {
                ApplySlopeStick(ref velocity, slopeAngle, hit);
                _rearCornerSlopeAngle = slopeAngle;
            }
        }
        else
        {
            _slopeCurveAccumulator = 0f;
            _rearCornerSlopeAngle = AIRBORNE_ANGLE_MEMORY;
        }

        bool isStable = false;
        float guardRayLength = Mathf.Max(Mathf.Abs(velocity.y) + CollisionPadding, _verticalProbeDistance);

        for (int i = 0; i < NumOfVerticalRays; i++)
        {
            Vector2 guardOrigin = RayCastCorners.bottomLeft + Vector2.right * (_verticalRaySpace * i);
            RaycastHit2D guardHit =
                Physics2D.Raycast(guardOrigin, Vector2.down, guardRayLength, _moveStats.GroundLayer);

            if (guardHit)
            {
                float guardAngle = (Mathf.Round(Vector2.Angle(guardHit.normal, Vector2.up)));
                if (guardAngle <= _moveStats.MaxSlopeAngle)
                {
                    isStable = true;
                    break;
                }
            }
        }

        if (!isStable)
        {
            RaycastHit2D maxSlopeHitLeft = Physics2D.Raycast(RayCastCorners.bottomLeft, Vector2.down,
                Mathf.Abs(velocity.y) + CollisionPadding, _moveStats.GroundLayer);
            RaycastHit2D maxSlopeHitRight = Physics2D.Raycast(RayCastCorners.bottomRight, Vector2.down,
                Mathf.Abs(velocity.y) + CollisionPadding, _moveStats.GroundLayer);

            if (maxSlopeHitLeft ^ maxSlopeHitRight)
            {
                SlideDownMaxSlope(maxSlopeHitLeft, ref velocity);
                SlideDownMaxSlope(maxSlopeHitRight, ref velocity);
            }
        }
    }

    public void UpdateSlopeMemory()
    {
        _slopeCurveAccumulator = 0f;
        _rearCornerSlopeAngle = AIRBORNE_ANGLE_MEMORY;

        LastLandingTime = Time.time;
    }

    private void ApplySlopeStick(ref Vector2 moveAmount, float slopeAngle, RaycastHit2D hit)
    {
        if (_playerMovement.IsWallSlideable(slopeAngle))
        {
            bool isFacingWall = Mathf.Sign(hit.normal.x) != FaceDirection;
            bool shouldWallSlide = isFacingWall || _moveStats.CanWallSlideFacingAwayFormWall;

            if (shouldWallSlide)
            {
                return;
            }
            else
            {
                SlideDownMaxSlope(hit, ref moveAmount);
            }
        }

        float tanDist = Mathf.Tan(slopeAngle * Mathf.Deg2Rad) * Mathf.Abs(moveAmount.x);
        float hitDistCheck = hit.distance - CollisionPadding;
        
        if (hitDistCheck <= tanDist && slopeAngle > 0.001f)
        {
            float moveDistance = Mathf.Abs(moveAmount.x);
            float descendMoveAmountY = Mathf.Sin(slopeAngle * Mathf.Deg2Rad) * moveDistance;

            moveAmount.x = Mathf.Cos(slopeAngle * Mathf.Deg2Rad) * moveDistance * Mathf.Sign(moveAmount.x);
            moveAmount.y -= descendMoveAmountY;

            IsDescendingSlope = true;
            SlopeNormal = hit.normal;
            SlopeAngle = slopeAngle;
        }
    }

    private void SlideDownMaxSlope(RaycastHit2D hit, ref Vector2 velocity)
    {
        if (hit)
        {
            float slopeAngle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.up));

            int wallDirection = (int)Mathf.Sign(hit.normal.x);
            bool isFacingWall = (wallDirection == -1 && _playerMovement.IsFacingRight) ||
                                (wallDirection == 1 && !_playerMovement.IsFacingRight);
            bool isNormalSlideableSlope =
                slopeAngle > _moveStats.MaxSlopeAngle && slopeAngle < _moveStats.MinAngleForWallSlide;
            bool isWallSlope = slopeAngle >= _moveStats.MinAngleForWallSlide;

            if (isNormalSlideableSlope || (isWallSlope && !isFacingWall))
            {
                Vector2 slopeTangent = new Vector2(hit.normal.y, -hit.normal.x);
                
                if (slopeTangent.y > 0) slopeTangent = -slopeTangent;
                slopeTangent.Normalize();

                float currentSpeed = velocity.magnitude;
                
                Vector2 slideVelocity = slopeTangent * currentSpeed;

                slideVelocity.y -= (hit.distance - CollisionPadding);
                
                velocity.x = slideVelocity.x;
                velocity.y = slideVelocity.y;

                SlopeAngle = slopeAngle;
                SlopeNormal = hit.normal;
                IsDescendingSlope = true;
            }
        }
    }

    #endregion

    #region Step Up

    private bool GetStepInfo(float hitDistance, float directionX, out float stepHeight)
    {
        stepHeight = 0f;

        Vector2 stepProbeUpperOrigin = (directionX == -1) ? RayCastCorners.bottomLeft : RayCastCorners.bottomRight;
        stepProbeUpperOrigin.y += _moveStats.StepMaxHeight;
        float checkDist = hitDistance + _moveStats.StepDetectionRayWidth;

        RaycastHit2D stepHitUpper = Physics2D.Raycast(stepProbeUpperOrigin, Vector2.right * directionX, checkDist,
            _moveStats.GroundLayer);
        if (stepHitUpper) return false;

        Vector2 stepProbeLowerOrigin = stepProbeUpperOrigin;
        stepProbeLowerOrigin.x += directionX * checkDist;

        RaycastHit2D surfaceHit = Physics2D.Raycast(stepProbeLowerOrigin, Vector2.down,
            _moveStats.StepMaxHeight + CollisionPadding, _moveStats.GroundLayer);
        if (!surfaceHit) return false;

        float surfaceAngle = Mathf.Round(Vector2.Angle(surfaceHit.normal, Vector2.up));
        if (!_playerMovement.IsWalkableSlope(surfaceAngle)) return false;

        float characterHeight = _coll.bounds.size.y;
        Vector2 finalHeadroomStart = surfaceHit.point + (Vector2.up * CollisionPadding);
        RaycastHit2D finalHeadroomHit = Physics2D.Raycast(finalHeadroomStart, Vector2.up,
            characterHeight - CollisionPadding, _moveStats.GroundLayer);
        if (finalHeadroomHit) return false;

        Vector2 currentHeadPos = (directionX == -1) ? RayCastCorners.topLeft : RayCastCorners.topRight;
        Vector2 targetHeadPos = finalHeadroomStart + (Vector2.up * (characterHeight - CollisionPadding));

        Vector2 diagonalDir = targetHeadPos - currentHeadPos;
        float diagonalDist = diagonalDir.magnitude;

        RaycastHit2D diagonalHit =
            Physics2D.Raycast(currentHeadPos, diagonalDir.normalized, diagonalDist, _moveStats.GroundLayer);
        if (diagonalHit) return false;

        float currentFeetY = (directionX == -1) ? RayCastCorners.bottomLeft.y : RayCastCorners.bottomRight.y;
        stepHeight = surfaceHit.point.y - currentFeetY;

        return true;
    }

    private bool AttemptStepUp(RaycastHit2D hit, ref Vector2 velocity, float directionX, float originalVelocityX)
    {
        if (GetStepInfo(hit.distance, directionX, out float stepHeight))
        {
            float minStepUpX = (hit.distance + _moveStats.StepDetectionRayWidth) * directionX;
            float targetX = minStepUpX;

            if (Mathf.Abs(originalVelocityX) > Mathf.Abs(minStepUpX))
            {
                targetX = originalVelocityX;
            }

            bool isSmallBump = stepHeight <= _moveStats.VaultMinHeight;
            bool isMovingFast = _playerMovement.IsRunning || _playerMovement.IsDashing;
            bool canVault = !_moveStats.OnlyVaultWhenRunning || isMovingFast;

            if (isSmallBump || canVault)
            {
                velocity.y = stepHeight + 0.001f;
                velocity.x = targetX;
                return true;
            }
        }

        return false;
    }

    public bool IsStep(Vector2 currentVelocity)
    {
        float directionX = Mathf.Sign(currentVelocity.x);
        if (Mathf.Abs(currentVelocity.x) < 0.001f) directionX = FaceDirection;

        float lookAhead = Mathf.Abs(currentVelocity.x) * Time.fixedDeltaTime + CollisionPadding;
        Vector2 rayOrigin = (directionX == -1) ? RayCastCorners.bottomLeft : RayCastCorners.bottomRight;

        RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.right * directionX, lookAhead, _moveStats.GroundLayer);

        if (!hit)
            return false;

        return GetStepInfo(hit.distance, directionX, out _);
    }

    #endregion
    
    #region Corner Correction

    private bool CalculateHeadCornerCorrection(Vector3 velocity, out float correctionAmount)
    {
        correctionAmount = 0f;

        if (velocity.y <= 0 || !_moveStats.EnableCornerCorrection) return false;
        
        float rayLength = Mathf.Abs(velocity.y) + CollisionPadding;
        rayLength = Mathf.Max(rayLength, _verticalProbeDistance);

        Vector2 leftOuter = RayCastCorners.topLeft;
        Vector2 rightOuter = RayCastCorners.topRight;

        RaycastHit2D hitLeftOuter = Physics2D.Raycast(leftOuter, Vector2.up, rayLength, _moveStats.GroundLayer);
        RaycastHit2D hitRightOuter = Physics2D.Raycast(rightOuter, Vector2.up, rayLength, _moveStats.GroundLayer);

        bool leftBlocked = hitLeftOuter;
        bool rightBlocked = hitRightOuter;
        
        if (leftBlocked && rightBlocked) return false;
        if (!leftBlocked && !rightBlocked) return false;

        float nudgeDir = leftBlocked ? 1f : -1f;
        float pushAmount = _moveStats.CornerCorrectionWidth + CollisionPadding;

        RaycastHit2D blockingHit = leftBlocked ? hitLeftOuter : hitRightOuter;
        float currentHitDist = blockingHit.distance;
        Vector2 normal = blockingHit.normal;

        float deltaX = pushAmount * nudgeDir;

        float projectedRise = 0f;
        if (Mathf.Abs(normal.y) > 0.001f)
        {
            projectedRise = -deltaX * (normal.x / normal.y);
        }

        float safeyBuffer = 0.3f;
        float testHeight = currentHitDist + projectedRise + safeyBuffer;
        
        Vector2 potentialNudge = Vector2.right * deltaX;
        Vector2 potentialVerticalMove = Vector2.up * testHeight;
        Vector2 testPos = _rb.position + potentialNudge + potentialVerticalMove;

        if (IsSpaceClear(testPos))
        {
            correctionAmount = pushAmount * nudgeDir;
            
            if (_moveStats.DebugShowCornerCorrectionRays)
                DrawDebugBox(testPos, _coll.bounds.size, Color.green, 0.05f);

            return true;
        }
        else
        {
            if (_moveStats.DebugShowCornerCorrectionRays)
                DrawDebugBox(testPos, _coll.bounds.size, Color.red, 0.05f);
            
            return false;
        }
    }
    
    private void DrawDebugBox(Vector2 center, Vector2 size, Color color, float duration)
    {
        Vector2 halfSize = size / 2;
        Vector2 topLeft = center + new Vector2(-halfSize.x, halfSize.y);
        Vector2 topRight = center + new Vector2(halfSize.x, halfSize.y);
        Vector2 bottomLeft = center + new Vector2(-halfSize.x, -halfSize.y);
        Vector2 bottomRight = center + new Vector2(halfSize.x, -halfSize.y);
        
        Debug.DrawLine(topLeft, topRight, color, duration);
        Debug.DrawLine(topRight, bottomRight, color, duration);
        Debug.DrawLine(bottomRight, bottomLeft, color, duration);
        Debug.DrawLine(bottomLeft, topLeft, color, duration);
    }

    private bool IsSpaceClear(Vector2 targetPosition)
    {
        Vector2 checkSize = _coll.bounds.size - (Vector3.one * (CollisionPadding * 2f));
        Vector2 center = targetPosition + _coll.offset;
        
        ContactFilter2D filter = new ContactFilter2D();
        filter.SetLayerMask(_moveStats.GroundLayer);
        filter.useTriggers = false;

        int hitCount = Physics2D.OverlapBox(center, checkSize, 0f, filter, _overlapBuffer);

        return hitCount == 0;
    }

    private bool DetectHeadCornerCorrection(Vector2 velocity)
    {
        return CalculateHeadCornerCorrection(velocity, out _);
    }

    private void ApplyHeadCornerCorrection(ref Vector2 velocity)
    {
        if (CalculateHeadCornerCorrection(velocity, out float amount))
        {
            velocity.x += amount;
            _isCornerCorrectingThisFrame = true;
        }
    }

    private bool CalculateHorizontalCornerCorrection(Vector2 velocity, out float correctionAmount)
    {
        // feet
        correctionAmount = 0f;
        
        if (!_playerMovement.IsDashing || IsClimbingSlope) return false;

        float directionX = Mathf.Sign(velocity.x);
        float rayLength = Mathf.Abs(velocity.x) + CollisionPadding;
        if (Mathf.Abs(velocity.x) < CollisionPadding) rayLength = CollisionPadding * 2;
        
        Vector2 footOrigin = (directionX == -1) ? RayCastCorners.bottomLeft : RayCastCorners.bottomRight;
        Vector2 headOrigin = (directionX == -1) ? RayCastCorners.topLeft : RayCastCorners.topRight;

        bool validlift = false;
        float potentialLiftAmount = 0f;

        RaycastHit2D footHit = Physics2D.Raycast(footOrigin, Vector2.right * directionX, rayLength, _moveStats.GroundLayer);
        
        if (_moveStats.DebugShowCornerCorrectionRays) 
            Debug.DrawRay(footOrigin, Vector2.right * directionX * rayLength, footHit ? Color.red : Color.green);

        if (footHit)
        {
            float hitAngle = Mathf.Round(Vector2.Angle(footHit.normal, Vector2.up));
            if (hitAngle <= _moveStats.MaxSlopeAngle)
            {
                return false;
            }
            
            float clearanceHeight = _moveStats.HorizontalCornerCorrectionHeight;
            Vector2 highProbeOrigin = footOrigin + Vector2.up * clearanceHeight;
            float checkDist = footHit.distance + CollisionPadding;

            bool highHit = Physics2D.Raycast(highProbeOrigin, Vector2.right * directionX * checkDist,
                _moveStats.GroundLayer);
            
            if (_moveStats.DebugShowCornerCorrectionRays)
                Debug.DrawRay(highProbeOrigin, Vector2.right * directionX * checkDist, highHit ? Color.red : Color.green);
            
            if (!highHit)
            {
                Vector2 downRayOrigin = footHit.point + (Vector2.right * directionX * CollisionPadding) +
                                        (Vector2.up * clearanceHeight);
                RaycastHit2D surfaceHit = Physics2D.Raycast(downRayOrigin, Vector2.down,
                    clearanceHeight + CollisionPadding, _moveStats.GroundLayer);
                
                if (_moveStats.DebugShowCornerCorrectionRays)
                    Debug.DrawRay(downRayOrigin, Vector2.down * (clearanceHeight + CollisionPadding), Color.cyan);

                if (surfaceHit)
                {
                    float lift = (surfaceHit.point.y + CollisionPadding) - footOrigin.y;
                    if (lift > 0 && lift <= clearanceHeight + CollisionPadding)
                    {
                        Vector2 targetHeadPos = headOrigin + (Vector2.right * directionX * CollisionPadding) +
                                                (Vector2.up * lift);
                        bool ceilingBlocked = Physics2D.Raycast(targetHeadPos, Vector2.up, CollisionPadding,
                            _moveStats.GroundLayer);

                        if (!ceilingBlocked)
                        {
                            potentialLiftAmount = lift;
                            validlift = true;
                        }
                    }
                }
            }
        }

        // head
        bool validPush = false;
        float potentialPushAmount = 0f;

        RaycastHit2D headHit =
            Physics2D.Raycast(headOrigin, Vector2.right * directionX, rayLength, _moveStats.GroundLayer);
        
        if (_moveStats.DebugShowCornerCorrectionRays)
            Debug.DrawRay(headOrigin, Vector2.right * directionX * rayLength, headHit ? Color.red : Color.green);

        if (headHit)
        {
            float clearanceHeight = _moveStats.HorizontalCornerCorrectionHeight;
            Vector2 lowProbeOrigin = headOrigin - Vector2.up * clearanceHeight;
            float checkDist = headHit.distance + CollisionPadding;

            bool lowHit = Physics2D.Raycast(lowProbeOrigin, Vector2.right * directionX, checkDist,
                _moveStats.GroundLayer);
            
            if (_moveStats.DebugShowCornerCorrectionRays)
                Debug.DrawRay(lowProbeOrigin, Vector2.right * directionX * checkDist, lowHit ? Color.red : Color.green);

            if (!lowHit)
            {
                Vector2 upRayOrigin = headHit.point + (Vector2.right * directionX * CollisionPadding) - (Vector2.up * clearanceHeight);
                RaycastHit2D ceilingHit = Physics2D.Raycast(upRayOrigin, Vector2.up, clearanceHeight + CollisionPadding,
                    _moveStats.GroundLayer);
                
                if (_moveStats.DebugShowCornerCorrectionRays)
                    Debug.DrawRay(upRayOrigin, Vector2.up * (clearanceHeight + CollisionPadding), Color.cyan);

                if (ceilingHit)
                {
                    float push = (ceilingHit.point.y - CollisionPadding) - headOrigin.y;
                    if (push < 0 && Mathf.Abs(push) <= clearanceHeight + CollisionPadding)
                    {
                        if (Mathf.Abs(push) > _moveStats.HorizontalPushDownMaximum)
                        {
                            validPush = false;
                        }
                        
                        Vector2 targetFootPos = footOrigin + (Vector2.right * directionX * CollisionPadding) + (Vector2.up * push);
                        bool floorBlocked = Physics2D.Raycast(targetFootPos, Vector2.down, CollisionPadding,
                            _moveStats.GroundLayer);

                        if (!floorBlocked)
                        {
                            potentialPushAmount = push;
                            validPush = true;
                        }
                    }
                }
                    
            }
        }

        if (validlift && validPush)
        {
            return false;
        }

        if (validlift)
        {
            correctionAmount = potentialLiftAmount;
            return true;
        }

        if (validPush)
        {
            correctionAmount = potentialPushAmount;
            return true;
        }
        
        return false;
    }

    private void ApplyHorizontalCornerCorrection(ref Vector2 velocity)
    {
        if (CalculateHorizontalCornerCorrection(velocity, out float liftAmount))
        {
            velocity.y = liftAmount;
            _isHorizontalCornerCorrectingThisFrame = true;
        }
    }

    #endregion

    #region Visuals
    private void DetectSlopeNormalsForVisuals(Vector2 velocity)
    {
        if (_internalState.IsGrounded)
        {
            float downwardRayLength = _moveStats.SlopeAverageNormalsRayLength;
            
            Vector2 compositeNormal = Vector2.zero;
            int hitCount = 0;

            float visualWidth = _moveStats.VisualRaycastWidth;
            float startX = _coll.bounds.center.x - (visualWidth / 2);
            float bottomY = _coll.bounds.min.y;

            float visualRaySpace = 0;
            if (NumOfVerticalRaysForVisualNormals > 1)
            {
                visualRaySpace = visualWidth / (NumOfVerticalRaysForVisualNormals -1);
            }

            for (int i = 0; i < NumOfVerticalRaysForVisualNormals; i++)
            {
                float xPos = startX + (visualRaySpace * i);
                Vector2 rayOrigin = new Vector2(xPos, bottomY) + (Vector2.right * velocity.x);

                RaycastHit2D hit =
                    Physics2D.Raycast(rayOrigin, Vector2.down, downwardRayLength, _moveStats.GroundLayer);

                if (hit)
                {
                    compositeNormal += hit.normal;
                    hitCount++;
                }
            }

            if (hitCount > 0)
            {
                compositeNormal /= hitCount;
                compositeNormal.Normalize();
                _internalState.AveragedVisualNormal = compositeNormal;
            }
            else
            {
                _internalState.AveragedVisualNormal = Vector2.up;
            }
        }
        else
        {
            _internalState.AveragedVisualNormal = Vector2.Lerp(_internalState.AveragedVisualNormal, Vector2.up,
                _moveStats.SlopeRotationSpeed * Time.fixedDeltaTime);
        }
    }

    #endregion

    #region Velocity Inheritable

    public void ApplyExternalPush(Vector2 pushAmount, Transform pusher)
    {
        Vector2 direction = pushAmount.normalized;
        float distance = pushAmount.magnitude;

        if (distance > 0.001f)
        {
            Vector2 origin = _coll.bounds.center;
            Vector2 size = _coll.bounds.size - (Vector3.one * CollisionPadding * 2f);
            
            RaycastHit2D hit = Physics2D.BoxCast(origin, size, 0f, direction, distance, _moveStats.GroundLayer);

            if (hit)
            {
                bool hitsPusher = pusher != null && hit.transform == pusher;

                if (!hitsPusher)
                {
                    bool steppedUp = false;
                    if (Mathf.Abs(pushAmount.x) > 0.001f)
                    {
                        float directionX = Mathf.Sign(pushAmount.x);

                        if (GetStepInfo(hit.distance, directionX, out float stepHeight))
                        {
                            _rb.position += Vector2.up * (stepHeight + CollisionPadding + 0.001f);
                            steppedUp = true;
                        }
                    }

                    if (!steppedUp)
                    {
                        OnCrush?.Invoke();
                    }
                }
            }
        }
        
        _rb.position += pushAmount;

        _wasPushedThisFrame = true;
        _pushAmountThisFrame = pushAmount;
    }

    #endregion
    
    #region Helpers Methods

    public bool IsGrounded() => _internalState.IsGrounded;
    public bool BumpedHead() => _internalState.IsHittingCeiling;
    public bool IsTouchingWall() => _internalState.IsAgainstWall;
    public int GetWallDirection()
    {
        return _internalState.WallDirection;
    }

    #endregion
}

#if UNITY_EDITOR

[CustomEditor(typeof(MovementController))]
public class MovementControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        MovementController controller = (MovementController)target;

        if (Application.isPlaying)
        {
            EditorGUILayout.LabelField("Collision State (Read Only)", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(true);

            EditorGUILayout.Toggle("Is On Platform", controller.LastKnownPlatform != null);
            EditorGUILayout.Space();
            
            EditorGUILayout.Toggle("Is Grounded", controller.State.IsGrounded);
            EditorGUILayout.Toggle("Is Against Wall", controller.State.IsAgainstWall);
            EditorGUILayout.IntField("Wall Direction", controller.State.WallDirection);
            EditorGUILayout.FloatField("Wall Angle", controller.State.WallAngle);

            EditorGUILayout.Space();
            EditorGUILayout.Toggle("Is On Slope", controller.State.IsOnSlope);
            EditorGUILayout.FloatField("Slope Angle", controller.State.SlopeAngle);
            EditorGUILayout.Vector2Field("Slope Normal", controller.State.SlopeNormal);

            EditorGUILayout.Space();
            EditorGUILayout.Toggle("Is Against Steep Slope", controller.State.IsAgainstSteepSlope);

            EditorGUILayout.Space();
            EditorGUILayout.Toggle("Is Hitting Ceiling", controller.State.IsHittingCeiling);
            EditorGUILayout.FloatField("Ceiling Angle", controller.State.CeilingAngle);
            EditorGUILayout.Vector2Field("Ceiling Normal", controller.State.CeilingNormal);
            EditorGUILayout.Vector2Field("Averaged Visual Normals:", controller.State.AveragedVisualNormal);

            EditorGUILayout.Space();
            EditorGUILayout.Toggle("Is Sliding", controller.IsSliding);

            EditorGUILayout.Space();
            EditorGUILayout.Toggle("Is Descending Slope", controller.IsDescendingSlope);
            EditorGUILayout.Toggle("Is Climbing Slope", controller.IsClimbingSlope);

            EditorGUI.EndDisabledGroup();

            Repaint();
        }
    }
}

#endif