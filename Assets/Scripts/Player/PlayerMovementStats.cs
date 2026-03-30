using System;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(menuName = "Player Movement")]
public class PlayerMovementStats : ScriptableObject
{
    [Header("Walk")]
    [Range(0f, 1f)] public float MoveThreshold = 0.25f;
    [Range(1f, 100f)] public float MaxWalkSpeed = 12.5f;
    [Range(0.25f, 50f)] public float GroundAcceleration = 5f;
    [Range(0.25f, 50f)] public float GroundDeceleration = 20f;
    [Range(0.25f, 50f)] public float AirAcceleration = 5f;
    [Range(0.25f, 50f)] public float AirDeceleration = 5f;
    [Range(0.25f, 50f)] public float WallJumpMoveAcceleration = 5f;
    [Range(0.25f, 50f)] public float WallJumpMoveDeceleration = 5f;

    [Header("Run")]
    [Range(1f, 100f)] public float MaxRunSpeed = 20f;

    [Header("Grounded/Collision Checks")] 
    public LayerMask GroundLayer;

    [Header("Corner Correction")] 
    public bool EnableCornerCorrection = true;
    [Range(0.01f, 1f)] public float CornerCorrectionWidth = 0.3f;
    [Range(0.01f, 1f)] public float HorizontalCornerCorrectionHeight = 0.6f;

    [Header("Slopes")]
    public bool DashDirectionMatchesSlopeDirection = true;
    public bool CanJumpOnMaxSlopes = false;
    public bool JumpFollowSlopesWhenHeadTouching = true;
    public bool DashFollowSlopesWhenHeadTouching = true;
    [Range(0f, 90f)] public float MaxSlopeAngle = 70f;
    [Range(1f, 100f)] public float SlideSpeed = 30f;

    [Header("Slope Visual Rotation")]
    public bool MatchVisualsToSlope = true;
    [Range(0.001f, 2f)] public float SlopeAverageNormalsRayLength = 1f;
    [Range(0.1f, 5f)] public float VisualRaycastWidth = 1.5f;
    [Range(0.05f, 100f)] public float SlopeRotationSpeed = 20f;
    [Range(0f, 70f)] public float MaxVisualRotationAngle = 45f;

    [Header("Jump")]
    public float JumpHeight = 6.5f;
    [Range(1f, 1.1f)] public float JumpHeightCompensationFactor = 1.054f;
    public float TimeTillJumpApex = 0.35f;
    [Range(0.01f, 5f)] public float GravityOnReleaseMultiplier = 2f;
    public float MaxFallSpeed = 26f;
    [Range(0, 5)] public int NumberOfAirJumpsAllowed = 1;

    [Header("Reset Jump Options")] 
    public bool ResetJumpsOnWallSlide = true;
    public bool ResetAirJumpsOnMaxSlopeLand = false;
    
    [Header("Jump Cut")] 
    [Range(0.02f, 0.3f)] public float TimeForUpwardsCancel = 0.027f;

    [Header("Jump Apex")] 
    [Range(0.5f, 1f)] public float ApexThreshold = 0.97f;
    [Range(0.01f, 1f)] public float ApexHangTime = 0.075f;

    [Header("Jump Buffer")]
    [Range(0f, 1f)] public float JumpBufferTime = 0.125f;
    
    [Header("Jump Coyote Time")]
    [Range(0f, 1f)] public float JumpCoyoteTime = 0.1f;

    [Header("Wall Slide")] public bool CanWallSlideFacingAwayFormWall = false;
    [Min(0.01f)] public float WallSlideSpeed = 5f;
    [Range(0.25f, 50f)] public float WallSlideDecelerationSpeed = 50f;
    [Range(70f, 90f)] public float MinAngleForWallSlide = 85f;
    [Range(90f, 135f)] public float MaxAngleForWallSlide = 95f;

    [Header("Wall Jump")] 
    public Vector2 WallJumpDirection = new Vector2(-20f, 6.5f);
    [Range(0f, 1f)] public float WallJumpCoyoteTime = 0.125f;
    [Range(0f, 0.5f)] public float WallJumpInputBufferDistance = 0.3f;
    [Range(0.01f, 5f)] public float WallJumpGravityOnReleaseMultiplier = 1f;

    [Header("Dash")]
    public bool CancelDashWhenYouHitCeiling = false;
    [Range(0f, 1f)] public float DashTime = 0.11f;
    [Range(1f, 200f)] public float DashSpeed = 40f;
    [Range(0f, 1f)] public float TimeBtwDashesOnGround = 0.225f;
    public bool ResetDashOnWallSlide = true;
    [Range(0, 5)] public int NumberOfDashes = 2;
    [Range(0f, 0.5f)] public float DashDiagonallyBias = 0.4f;
    [Range(0f, 1f)] public float DashBufferTime = 0.125f;

    [Header("Dash Feel")]
    [Range(0f, 0.5f)] public float DashFreezeTime = 0.05f;

    [Header("Dash Coyote Time")]
    [Range(0f, 1f)] public float DashCoyoteTime = 0.125f;

    [Header("Dash Cancel Time")]
    [Range(0.01f, 5f)] public float DashGravityOnReleaseMultiplier = 1f;
    [Range(0.02f, 0.3f)] public float DashTimeForUpwardsCancel = 0.027f;

    [Header("Trackers")] 
    public bool DebugTrackJumpHeight;
    
    [Header("Debug")]
    public bool DebugShowIsGrounded = false;
    public bool DebugShowHeadRays = false;
    public bool DebugShowCornerCorrectionRays = false;
    public bool DebugShowWallHit = false;
    public bool DebugShowWallJumpBufferBox = false;
    public bool DebugShowSlopeNormal = false;
    public bool DebugShowDashAngle = false;
    public bool DebugShowDescendSlopeRay = false;
    [Range(0f, 1f)] public float ExtraRayDebugDistance = 0.25f;

    [Header("JumpVisualization Tool")] 
    public bool ShowWalkJumpArc = false;
    public bool ShowRunJumpArc = false;
    public bool StopOnCollision = false;
    public bool DrawRight = true;
    [Range(5, 100)] public int ArcResolution = 20;
    [Range(0, 500)] public int VisualizationSteps = 90;

    public readonly Vector2[] DashDirections = new Vector2[]
    {
        new Vector2(0, 0),                 // Nothing
        new Vector2(1, 0),                 // Right
        new Vector2(1, 1).normalized,      // TOP-Right
        new Vector2(0, 1),                 // Up
        new Vector2(-1, 1).normalized,     // TOP-Left
        new Vector2(-1, 0),                // Left
        new Vector2(-1, -1).normalized,    // BOTTOM-Left
        new Vector2(0, -1),                // BOTTOM
        new Vector2(1, -1).normalized,     // BOTTOM-Right
    };
    
    // Jump
    public float Gravity { get; private set; }
    public float InitialJumpVelocity { get; private set; }
    public float AdjustedJumpHeight { get; private set; }

    // Wall Jump
    public float WallJumpGravity { get; private set; }
    public float InitialWallJumpVelocity { get; private set; }
    public float AdjustedWallJumpHeight { get; private set; }
    
    private void OnValidate()
    {
        CalculateValues();
    }

    private void OnEnable()
    {
        CalculateValues();
    }

    private void CalculateValues()
    {
        // Jump
        AdjustedJumpHeight = JumpHeight * JumpHeightCompensationFactor;
        Gravity = -(2f * AdjustedJumpHeight) / Mathf.Pow(TimeTillJumpApex, 2f);
        InitialJumpVelocity = Mathf.Abs(Gravity) * TimeTillJumpApex;
        
        // Wall Jump
        AdjustedWallJumpHeight = WallJumpDirection.y * JumpHeightCompensationFactor;
        WallJumpGravity = -(2f * AdjustedWallJumpHeight) / Mathf.Pow(TimeTillJumpApex, 2f);
        InitialWallJumpVelocity = Mathf.Abs(WallJumpGravity) * TimeTillJumpApex;
    }
}
