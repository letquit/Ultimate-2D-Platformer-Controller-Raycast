using System;
using UnityEngine;

public class MovementController : MonoBehaviour
{
    public const float CollisionPadding = 0.015f;

    [Range(2, 100)] public int NumOfHorizontalRays = 4;
    [Range(2, 100)] public int NumOfVerticalRays = 4;

    private float _horizontalRaySpace;
    private float _verticalRaySpace;

    private BoxCollider2D _coll;
    public RaycastCorners RayCastCorners;
    private PlayerMovementStats _moveStats;
    
    public bool IsCollidingAbove { get; private set; }
    public bool IsCollidingBelow { get; private set; }
    public bool IsCollidingLeft { get; private set;}
    public bool IsCollidingRight { get; private set; }
    public int HeadBumpSlideDirection { get; private set; }
    public bool IsHittingCeilingCenter { get; private set; }
    public bool IsHittingBothCorners { get; private set; }
    
    public bool IsClimbingSlope { get; private set; }
    public bool WasClimbingSlopeLastFrame { get; private set; }
    public bool IsDescendingSlope { get; private set; }
    public float SlopeAngle { get; private set; }
    public Vector2 SlopeNormal { get; private set; }
    public float WallAngle { get; private set; }
    public bool IsSliding { get; private set; }
    public bool IsOnSlideableSlope { get; private set; }
    public int FaceDirection { get; private set; }
    public float CeilingAngle { get; private set; }
    public Vector2 CeilingNormal { get; private set; }

    private PlayerMovement _playerMovement;
    private Rigidbody2D _rb;
    
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

    public void Move(Vector2 velocity)
    {
        UpdateRayCastCorners();
        ResetCollisionStates();
        CheckCeilingBoxCast(velocity);

        if (velocity.y <= 0f && !_playerMovement.IsDashing && !WasClimbingSlopeLastFrame)
        {
            DescendSlope(ref velocity);
        }

        if (velocity.x != 0)
        {
            FaceDirection = (int)Mathf.Sign(velocity.x);
        }

        ResolveHorizontalMovement(ref velocity);
        ResolveVerticalMovement(ref velocity);
        
        _rb.MovePosition(_rb.position + velocity);
    }

    private void ResetCollisionStates()
    {
        IsCollidingAbove = false;
        IsCollidingBelow = false;
        IsCollidingLeft = false;
        IsCollidingRight = false;
        
        HeadBumpSlideDirection = 0;
        IsHittingCeilingCenter = false;
        IsHittingBothCorners = false;

        WasClimbingSlopeLastFrame = IsClimbingSlope;
        IsClimbingSlope = false;
        IsDescendingSlope = false;
        SlopeAngle = 0f;
        SlopeNormal = Vector2.zero;
        WallAngle = 0f;
        IsSliding = false;
        IsOnSlideableSlope = false;
        CeilingAngle = 0f;
        CeilingNormal = Vector2.zero;
    }

    private void CheckCeilingBoxCast(Vector2 velocity)
    {
        if (velocity.y < 0) return;
        if (!_moveStats.UseHeadBumpSlide) return;

        float boxCastDistance = Mathf.Abs(velocity.y) + CollisionPadding;
        Vector2 boxSize =
            new Vector2(_coll.bounds.center.x * _moveStats.HeadBumpBoxWidth, _moveStats.HeadBumpBoxHeight);
        Vector2 boxOrigin = new Vector2(_coll.bounds.center.x + velocity.x, _coll.bounds.max.y);

        RaycastHit2D hit =
            Physics2D.BoxCast(boxOrigin, boxSize, 0f, Vector2.up, boxCastDistance, _moveStats.GroundLayer);

        if (hit)
        {
            IsHittingCeilingCenter = true;
        }

        #region Debug Visualization

        if (_moveStats.DebugShowHeadBumpBox)
        {
            Vector2 drawCenter = boxOrigin + (Vector2.up * boxCastDistance / 2f);
            Vector2 drawSize = new Vector2(boxSize.x, boxSize.y + boxCastDistance);
            Vector2 halfSize = drawSize / 2f;
            
            // 4 corners
            Vector2 topLeft = drawCenter + new Vector2(-halfSize.x, halfSize.y);
            Vector2 topRight = drawCenter + new Vector2(halfSize.x, halfSize.y);
            Vector2 bottomRight = drawCenter + new Vector2(halfSize.x, -halfSize.y);
            Vector2 bottomLeft = drawCenter + new Vector2(-halfSize.x, -halfSize.y);
            
            Color color = hit ? Color.green : Color.red;
            
            Debug.DrawLine(topLeft, topRight, color);
            Debug.DrawLine(topRight, bottomRight, color);
            Debug.DrawLine(bottomRight, bottomLeft, color);
            Debug.DrawLine(bottomLeft, topLeft, color);
        }

        #endregion
    }

    private void ResolveHorizontalMovement(ref Vector2 velocity)
    {
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

        for (int i = 0; i < NumOfHorizontalRays; i++)
        {
            Vector2 rayOrigin = (directionX == -1) ? RayCastCorners.bottomLeft : RayCastCorners.bottomRight;
            rayOrigin += Vector2.up * (_horizontalRaySpace * i);
            RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.right * directionX, rayLength, _moveStats.GroundLayer);

            if (hit)
            {
                float slopeAngle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.up));
                bool isSlideableSlope = slopeAngle > _moveStats.MaxSlopeAngle && slopeAngle < _moveStats.MinAngleForWallSlide;

                if (isSlideableSlope)
                {
                    IsOnSlideableSlope = true;
                
                    velocity.x = (hit.distance - CollisionPadding) * directionX;
                    rayLength = hit.distance;

                    if (IsClimbingSlope)
                    {
                        velocity.y = Mathf.Tan(SlopeAngle * Mathf.Deg2Rad) * Mathf.Abs(velocity.x);
                    }

                    if (directionX == -1)
                    {
                        IsCollidingLeft = true;
                    }
                    else if (directionX == 1)
                    {
                        IsCollidingRight = true;
                    }
                    
                    continue;
                }

                if (IsSliding)
                {
                    continue;
                }

                if (i == 0 && slopeAngle <= _moveStats.MaxSlopeAngle)
                {
                    ClimbSlope(ref velocity, slopeAngle, hit.normal);
                    continue;
                }

                if (IsClimbingSlope && slopeAngle <= _moveStats.MaxSlopeAngle)
                {
                    continue;
                }
                
                velocity.x = (hit.distance - CollisionPadding) * directionX;
                rayLength = hit.distance;
                WallAngle = slopeAngle;

                if (IsClimbingSlope)
                {
                    velocity.y = Mathf.Tan(SlopeAngle * Mathf.Deg2Rad) * Mathf.Abs(velocity.x);
                }

                IsCollidingLeft = directionX == -1;
                IsCollidingRight = directionX == 1;
            }

            #region Debug Visualization

            if (_moveStats.DebugShowWallHit)
            {
                float debugRayLength = _moveStats.ExtraRayDebugDistance;
                Vector2 debugRayOrigin = (directionX == -1) ? RayCastCorners.bottomLeft : RayCastCorners.bottomRight;
                debugRayOrigin += Vector2.up * (_horizontalRaySpace * i);

                bool didHit = Physics2D.Raycast(debugRayOrigin, Vector2.right * directionX, debugRayLength,
                    _moveStats.GroundLayer);
                Color rayColor = didHit ? Color.cyan : Color.red;
                Debug.DrawRay(debugRayOrigin, Vector2.right * directionX * debugRayLength, rayColor);
            }

            #endregion
        }
    }

    private void ResolveVerticalMovement(ref Vector2 velocity)
    {
        bool hitLeftCorner = false;
        bool hitRightCorner = false;

        #region Ceiling Check

        if (velocity.y > 0f)
        {
            float upwardRayLength = Mathf.Abs(velocity.y) + CollisionPadding;
            for (int i = 0; i < NumOfVerticalRays; i++)
            {
                Vector2 rayOrigin = RayCastCorners.topLeft;

                float horizontalProjection = velocity.x;
                if (_playerMovement.IsHeadBumpSliding)
                {
                    horizontalProjection = 0f;
                }

                rayOrigin += Vector2.right * (_verticalRaySpace * i + horizontalProjection);
                RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.up, upwardRayLength, _moveStats.GroundLayer);

                if (hit)
                {
                    float currentCeilingAngle;
                    if (hit.distance == 0f)
                    {
                        velocity.y = 0f;

                        Vector2 safetyRayOrigin = rayOrigin + (Vector2.down * CollisionPadding * 2);
                        RaycastHit2D safetyHit = Physics2D.Raycast(safetyRayOrigin, Vector2.up, CollisionPadding * 3,
                            _moveStats.GroundLayer);

                        if (safetyHit)
                        {
                            currentCeilingAngle = Mathf.Round(Vector2.Angle(safetyHit.normal, Vector2.down));
                            CeilingNormal = safetyHit.normal;
                        }
                        else
                        {
                            currentCeilingAngle = 0f;
                            CeilingNormal = Vector2.down;
                        }
                    }

                    else
                    {
                        velocity.y = (hit.distance - CollisionPadding);
                        upwardRayLength = hit.distance;
                        currentCeilingAngle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.down));
                        CeilingNormal = hit.normal;
                    }

                    IsCollidingAbove = true;

                    if (i == 0)
                    {
                        hitLeftCorner = true;
                    }

                    if (i == NumOfVerticalRays - 1)
                    {
                        hitRightCorner = true;
                    }

                    if (currentCeilingAngle > CeilingAngle)
                    {
                        CeilingAngle = currentCeilingAngle;
                        CeilingNormal = hit.normal;
                    }
                    
                    if (_moveStats.UseHeadBumpSlide && currentCeilingAngle <= _moveStats.MaxSlopeAngleForHeadBump)
                    {
                        int slideDir = 0;
                        if (i == 0) slideDir = 1;
                        else if (i == NumOfVerticalRays - 1) slideDir = -1;

                        if (slideDir != 0)
                        {
                            Vector2 slideCheckRayOrigin = hit.point + (Vector2.down * CollisionPadding * 2);
                            float slideCheckRayLength = CollisionPadding * 2;
                            RaycastHit2D slideCheckHit = Physics2D.Raycast(slideCheckRayOrigin,
                                Vector2.right * slideDir, slideCheckRayLength, _moveStats.GroundLayer);

                            if (!slideCheckHit)
                            {
                                HeadBumpSlideDirection = slideDir;
                            }
                        }
                    }
                }
                
                #region Debug Visualization

                if (_moveStats.DebugShowHeadRays)
                {
                    float debugRayLength = _moveStats.ExtraRayDebugDistance;
                    Vector2 debugRayOrigin = RayCastCorners.topLeft + Vector2.right * (_verticalRaySpace * i + horizontalProjection);
                    bool didHit = Physics2D.Raycast(debugRayOrigin, Vector2.up, debugRayLength, _moveStats.GroundLayer);
                    Color rayColor = didHit ? Color.cyan : Color.red;

                    if (i == 0 || i == NumOfVerticalRays - 1)
                    {
                        rayColor = didHit ? Color.green : Color.magenta;
                    }
                    Debug.DrawRay(debugRayOrigin, Vector2.up * debugRayLength, rayColor);
                }

                #endregion
            }
        }

        #endregion

        #region Ground Check

        float downwardRayLength;

        if (velocity.y < 0)
        {
            downwardRayLength = Mathf.Abs(velocity.y) + CollisionPadding;
        }
        else
        {
            downwardRayLength = CollisionPadding * 2;
        }

        float smallestHitDistance = float.MaxValue;
        RaycastHit2D groundHit = new RaycastHit2D();
        bool foundGround = false;
        
        for (int i = 0; i < NumOfVerticalRays; i++)
        {
            Vector2 rayOrigin = RayCastCorners.bottomLeft;
            rayOrigin += Vector2.right * (_verticalRaySpace * i + velocity.x);

            RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.down, downwardRayLength, _moveStats.GroundLayer);
            
            if (hit)
            {
                if (hit.distance < smallestHitDistance)
                {
                    smallestHitDistance = hit.distance;
                    groundHit = hit;
                    foundGround = true;
                }
            }
            
            #region Debug Visualization

            if (_moveStats.DebugShowIsGrounded)
            {
                float debugRayLength = _moveStats.ExtraRayDebugDistance;
                Vector2 debugRayOrigin = RayCastCorners.bottomLeft + Vector2.right * (_verticalRaySpace * i + velocity.x);
                bool didHit = Physics2D.Raycast(debugRayOrigin, Vector2.down, debugRayLength, _moveStats.GroundLayer);
                Color rayColor = didHit ? Color.cyan : Color.red;
                Debug.DrawRay(debugRayOrigin, Vector2.down * debugRayLength, rayColor);
            }

            #endregion
        }

        if (foundGround)
        {
            IsCollidingBelow = true;
            if (velocity.y <= 0)
            {
                velocity.y = (groundHit.distance - CollisionPadding) * -1;
            }

            float slopeAngle = Mathf.Round(Vector2.Angle(groundHit.normal, Vector2.up));
            bool isGroundAWall = slopeAngle >= _moveStats.MinAngleForWallSlide &&
                                 slopeAngle <= _moveStats.MaxAngleForWallSlide;
            if (!isGroundAWall)
            {
                if (slopeAngle > 0f)
                {
                    SlopeAngle = slopeAngle;
                    SlopeNormal = groundHit.normal;
                }
            }
        }
        else
        {
            if (IsOnSlideableSlope)
            {
                IsSliding = true;
            }
        }
        
        #endregion
        
        IsHittingBothCorners = hitLeftCorner && hitRightCorner;

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

        #region Debug Slope Normal

        if (_moveStats.DebugShowSlopeNormal && (IsClimbingSlope || IsDescendingSlope))
        {
            Vector2 drawOrigin = new Vector2(_coll.bounds.center.x, _coll.bounds.min.y);
            float drawLength = _moveStats.ExtraRayDebugDistance * 3f;
            
            Debug.DrawRay(drawOrigin, SlopeNormal * drawLength, Color.yellow);
        }

        #endregion
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

    private void ClimbSlope(ref Vector2 velocity, float slopeAngle, Vector2 slopeNormal)
    {
        float moveDistance = Mathf.Abs(velocity.x);
        float climbVelocityY = Mathf.Sign(slopeAngle * Mathf.Deg2Rad) * moveDistance;

        if (velocity.y <= climbVelocityY)
        {
            velocity.y = climbVelocityY;
            velocity.x = Mathf.Cos(slopeAngle * Mathf.Deg2Rad) * moveDistance * Mathf.Sign(velocity.x);

            IsCollidingBelow = true;
            IsClimbingSlope = true;
            SlopeAngle = slopeAngle;
            SlopeNormal = slopeNormal;
        }
    }

    private void DescendSlope(ref Vector2 velocity)
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

        if (!IsSliding)
        {
            float directionX = FaceDirection;
            
            Vector2 rayOrigin = (directionX == -1) ? RayCastCorners.bottomRight : RayCastCorners.bottomLeft;

            float maxExpectedVerticalDrop = Mathf.Tan(_moveStats.MaxSlopeAngle * Mathf.Deg2Rad) * Mathf.Abs(velocity.x);
            float dynamicRayLength = Mathf.Abs(velocity.y) + CollisionPadding + maxExpectedVerticalDrop;
            float rayLength = Mathf.Max(dynamicRayLength, CollisionPadding * 2f);

            RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.down, rayLength, _moveStats.GroundLayer);

            if (hit)
            {
                float slopeAngle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.up));
                ApplySlopeStick(ref velocity, slopeAngle, hit);
            }

            #region Debug Visualization

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
        }
    }

    private void ApplySlopeStick(ref Vector2 moveAmount, float slopeAngle, RaycastHit2D hit)
    {
        if (hit.distance - CollisionPadding <= Mathf.Tan(slopeAngle * Mathf.Deg2Rad) * Mathf.Abs(moveAmount.x))
        {
            float moveDistance = Mathf.Abs(moveAmount.x);
            float descendMoveAmountY = Mathf.Sin(slopeAngle * Mathf.Deg2Rad) * moveDistance;

            moveAmount.x = Mathf.Cos(slopeAngle * Mathf.Deg2Rad) * moveDistance * Mathf.Sign(moveAmount.x);
            moveAmount.y -= descendMoveAmountY;

            IsDescendingSlope = true;
            IsCollidingBelow = true;
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
                float tanAngle = Mathf.Clamp(slopeAngle, 0, 89.9f);
                
                velocity.x = Mathf.Sign(hit.normal.x) * (Mathf.Abs(velocity.y) - hit.distance) /
                             Mathf.Tan(tanAngle * Mathf.Deg2Rad);

                IsSliding = true;
                IsCollidingBelow = true;
                SlopeAngle = slopeAngle;
                SlopeNormal = hit.normal;
            }
        }
    }

    #endregion

    #region Helpers Methods

    public bool IsGrounded() => IsCollidingBelow;
    public bool BumpedHead() => IsCollidingAbove;
    public bool IsTouchingWall(bool isFacingRight) =>
        (isFacingRight && IsCollidingRight) || (!isFacingRight && IsCollidingLeft);
    public int GetWallDirection()
    {
        if (IsCollidingLeft) return -1;
        if (IsCollidingRight) return 1;
        return 0;
    }

    #endregion
}
