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
        float rayLength = Mathf.Abs(velocity.x) + CollisionPadding;

        bool hitLeftCorner = false;
        bool hitRightCorner = false;

        for (int i = 0; i < NumOfHorizontalRays; i++)
        {
            Vector2 rayOrigin = (directionX == -1) ? RayCastCorners.bottomLeft : RayCastCorners.bottomRight;
            rayOrigin += Vector2.up * (_horizontalRaySpace * i);
            RaycastHit2D hit =
                Physics2D.Raycast(rayOrigin, Vector2.right * directionX, rayLength, _moveStats.GroundLayer);

            if (hit)
            {
                velocity.x = (hit.distance - CollisionPadding) * directionX;
                rayLength = hit.distance;

                if (directionX == -1)
                {
                    IsCollidingLeft = true;
                }
                else
                {
                    IsCollidingRight = true;
                    
                    if (i == 0) hitLeftCorner = true;
                    if (i == NumOfHorizontalRays - 1) hitRightCorner = true;

                    if (_moveStats.UseHeadBumpSlide)
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
        
        IsHittingBothCorners = hitLeftCorner && hitRightCorner;
    }

    private void ResolveVerticalMovement(ref Vector2 velocity)
    {
        float directionY = Mathf.Sign(velocity.y);
        float rayLength = Mathf.Abs(velocity.y) + CollisionPadding;

        for (int i = 0; i < NumOfVerticalRays; i++)
        {
            Vector2 rayOrigin = (directionY == -1) ? RayCastCorners.bottomLeft : RayCastCorners.topLeft;
            rayOrigin += Vector2.right * (_verticalRaySpace * i + velocity.x);

            RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.up * directionY, rayLength, _moveStats.GroundLayer);

            if (hit)
            {
                velocity.y = (hit.distance - CollisionPadding) * directionY;
                rayLength = hit.distance;

                if (directionY == -1)
                {
                    IsCollidingBelow = true;
                }
                else
                {
                    IsCollidingAbove = true;
                }
            }

            #region Debug Visualization

            if (_moveStats.DebugShowIsGrounded)
            {
                float debugRayLength = _moveStats.ExtraRayDebugDistance;
                Vector2 debugRayOrigin = RayCastCorners.bottomLeft + Vector2.right * (_verticalRaySpace * i);
                bool didHit = Physics2D.Raycast(debugRayOrigin, Vector2.down, debugRayLength, _moveStats.GroundLayer);
                Color rayColor = didHit ? Color.cyan : Color.red;
                Debug.DrawRay(debugRayOrigin, Vector2.down * debugRayLength, rayColor);
            }

            if (_moveStats.DebugShowHeadRays)
            {
                float debugRayLength = _moveStats.ExtraRayDebugDistance;
                Vector2 debugRayOrigin = RayCastCorners.topLeft + Vector2.right * (_verticalRaySpace * i);
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
