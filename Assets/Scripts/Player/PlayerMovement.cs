using System;
using UnityEngine;
using UnityEngine.Serialization;

[SelectionBase]
public class PlayerMovement : MonoBehaviour
{
    [Header("References")] public PlayerMovementStats MoveStats;
    [SerializeField] private Collider2D _coll;
    [SerializeField] private Transform _visualsTransform;
    [SerializeField] private Transform _respawnPoint;

    private Rigidbody2D _rb;

    // movement vars
    public bool IsFacingRight { get; private set; }
    public MovementController Controller { get; private set; }
    [HideInInspector] public Vector2 Velocity;

    // input
    private Vector2 _moveInput;
    private bool _runHeld;
    private bool _jumpPressed;
    private bool _jumpReleased;
    private bool _dashPressed;
    
    // jump vars
    public bool IsJumping  { get; private set; }
    private bool _isFastFalling;
    private bool _isFalling;
    private float _fastFallTime;
    private float _fastFallReleaseSpeed;
    private int _numberOfAirJumpsUsed;

    // apex vars
    private float _apexPoint;
    private float _timePastApexThreshold;
    private bool _isPastApexThreshold;

    // jump buffer vars
    private float _jumpBufferTimer;
    private bool _jumpReleasedDuringBuffer;

    // coyote time vars
    private float _coyoteTimer;

    // wall slide
    private bool _isWallSliding;
    private bool _isWallSlideFalling;

    // wall jump
    private bool _useWallJumpMoveStats;
    private bool _isWallJumping;
    private float _wallJumpTime;
    private bool _isWallJumpFastFalling;
    private bool _isWallJumpFalling;
    private float _wallJumpFastFallTime;
    private float _wallJUmpFastFallReleaseSpeed;
    private int _lastWallDir;

    private float _wallJumpCoyoteTimer;

    private float _wallJumpApexPoint;
    private float _timePastWallJumpApexThreshold;
    private bool _isPastWallJumpApexThreshold;

    // dash var
    public bool IsDashing { get; private set; }
    private bool _isAirDashing;
    private float _dashTimer;
    private float _dashOnGroundTimer;
    private int _numberOfDashesUsed;
    private Vector2 _dashDirection;
    private bool _isDashFastFalling;
    private float _dashFastFallTime;
    private float _dashFastFallReleaseSpeed;
    private float _dashBufferTimer;

    private Vector2 _dashIntentDirection;
    private float _dashCoyoteTimer;
    private float _dashDelayTimer;
    
    // head bump slide vars
    private float _debugMaxHeightY;
    private bool _debugTrackingJump;
    private float _debugJumpStartY;
    
    // slopes
    private bool _isPerformingSLopeDash;
    private float _slopeDashAngle;
    private Quaternion _targetRotation = Quaternion.identity;
    
    // platforms
    private Vector2 _storePlatformVelocity;
    private float _platformMomentumRetentionTimer;
    
    // visuals
    private VisualInterpolator _visuals;

    public bool IsRunning => InputManager.RunIsHeld;
    
    private void Awake()
    {
        // Time.timeScale = 0.05f;
        IsFacingRight = true;
        _rb = GetComponent<Rigidbody2D>();
        Controller = GetComponent<MovementController>();
        _visuals = GetComponentInChildren<VisualInterpolator>();
    }

    private void OnEnable()
    {
        Controller.OnCrush += HandleCrush;
    }
    
    private void OnDisable()
    {
        Controller.OnCrush -= HandleCrush;
    }

    private void Update()
    {
        _moveInput = InputManager.Movement;
        _runHeld = InputManager.RunIsHeld;
        if (InputManager.JumpWasPressed) _jumpPressed = true;
        if (InputManager.JumpWasReleased) _jumpReleased = true;
        if (InputManager.DashWasPressed) _dashPressed = true;
    }

    private void LateUpdate()
    {
        if (MoveStats.MatchVisualsToSlope)
        {
            RotateVisualTarget(Time.deltaTime);
        }
    }

    private void FixedUpdate()
    {
        Controller.PollSensors(Velocity * Time.fixedDeltaTime);
        
        CountTimers(Time.fixedDeltaTime);
        
        JumpChecks();
        LandCheck();
        WallJumpCheck();
        WallSlideCheck();
        DashCheck();
        VelocityReset();
        PreventWalkStick();
        PreventCeilingStick();
        CalculateRunOffMomentum();
        
        CalculateTargetRotation();
        
        HandleHorizontalMovement(Time.fixedDeltaTime);
        Jump(Time.fixedDeltaTime);
        WallSlide(Time.fixedDeltaTime);
        WallJump(Time.fixedDeltaTime);
        Dash(Time.fixedDeltaTime);
        Fall(Time.fixedDeltaTime);
        HandleSlide(Time.fixedDeltaTime);

        ClampVelocity();
        
        Controller.Move(Velocity * Time.fixedDeltaTime);
        
        TrackJump();
        
        _jumpPressed = false;
        _jumpReleased = false;
        _dashPressed = false;
        
        _visuals.UpdatePhysicsState();
    }

    private void TrackJump()
    {
        if (_debugTrackingJump)
        {
            if (_rb.position.y > _debugMaxHeightY)
            {
                _debugMaxHeightY = _rb.position.y;
            }

            if (Velocity.y <= 0)
            {
                float totalHeight = _debugMaxHeightY - _debugJumpStartY;
                string collisionNote = "";
                if (Controller.State.IsHittingCeiling) collisionNote = " (Hit Ceiling)";
                
                Debug.Log($"[Jump Debug] REACHED APEX. Max Height: {totalHeight:F5} units.{collisionNote}");
                
                _debugTrackingJump = false;
            }
        }
    }

    private void ClampVelocity()
    {
        if (Controller.IsSliding)
        {
            Velocity.y = Mathf.Clamp(Velocity.y, -MoveStats.SlideSpeed, 50);
        }
        else if (IsDashing)
        {
            Velocity.y = Mathf.Clamp(Velocity.y, -50f, 50f);
        }
        else
        {
            float dynamicFallSpeed = MoveStats.MaxFallSpeed;

            if (_platformMomentumRetentionTimer > 0f)
            {
                if (_storePlatformVelocity.y < -dynamicFallSpeed)
                {
                    dynamicFallSpeed = Mathf.Abs(_storePlatformVelocity.y);
                }
            }

            Velocity.y = Mathf.Clamp(Velocity.y, -dynamicFallSpeed, 100f);
        }
    }

    private void OnDrawGizmos()
    {
        if (MoveStats.ShowWalkJumpArc)
        {
            DrawJumpArc(MoveStats.MaxWalkSpeed, Color.white);
        }

        if (MoveStats.ShowRunJumpArc)
        {
            DrawJumpArc(MoveStats.MaxRunSpeed, Color.red);
        }
    }

    #region Movement

    private void HandleHorizontalMovement(float timeStep)
    {
        if (!IsDashing)
        {
            float acceleration = Controller.IsGrounded() ? MoveStats.GroundAcceleration : MoveStats.AirAcceleration;
            float deceleration = Controller.IsGrounded() ? MoveStats.GroundDeceleration : MoveStats.AirDeceleration;

            if (_useWallJumpMoveStats)
            {
                acceleration = MoveStats.WallJumpMoveAcceleration;
                deceleration = MoveStats.WallJumpMoveDeceleration;
            }
            
            if (Mathf.Abs(_moveInput.x) >= MoveStats.MoveThreshold)
            {
                TurnCheck(_moveInput);
                float moveDirection = Mathf.Sign(_moveInput.x);
                float targetVelocityX = _runHeld ? moveDirection * MoveStats.MaxRunSpeed : moveDirection * MoveStats.MaxWalkSpeed;

                float t = Mathf.Clamp01(acceleration * timeStep);
                
                Velocity.x = Mathf.Lerp(Velocity.x, targetVelocityX, t);

                if (Mathf.Abs(Velocity.x - targetVelocityX) <= 0.01f)
                {
                    Velocity.x = targetVelocityX;
                }
            }
            
            else
            {
                float t = Mathf.Clamp01(deceleration * timeStep);
                Velocity.x = Mathf.Lerp(Velocity.x, 0, t);

                if (Mathf.Abs(Velocity.x) <= 0.01f)
                {
                    Velocity.x = 0f;
                }
            }
        }
    }

    private void PreventWalkStick()
    {
        if (Controller.IsTouchingWall())
        {
            if ((Velocity.x > 0 && Controller.State.WallDirection == 1) ||
                (Velocity.x < 0 && Controller.State.WallDirection == -1))
            {
                if (Controller.IsStep(Velocity))
                {
                    return;
                }
                
                Velocity.x = 0f;
            }
        }
    }

    private void PreventCeilingStick()
    {
        if (Controller.BumpedHead())
        {
            if (Velocity.y > 0)
            {
                bool isSlideableCeiling =
                    MoveStats.JumpFollowSlopesWhenHeadTouching && Controller.State.CeilingAngle > 0;
                
                if (!IsDashing && !isSlideableCeiling)
                {
                    Velocity.y = 0f;
                }
            }
        }
    }

    private void CalculateRunOffMomentum()
    {
        if (Controller.IsGrounded() || !Controller.State.WasGroundedLastFrame || IsJumping || _isWallJumping ||
            Controller.PlatformFromLastFrame == null) return;

        if (_platformMomentumRetentionTimer > 0f)
        {
            Vector2 platformVel = _storePlatformVelocity;

            float hBoost = platformVel.x * MoveStats.PlatformHorizontalMomentumMultiplier;
            Velocity.x += hBoost;

            if (platformVel.y < 0f)
            {
                Velocity.y += platformVel.y;
            }
            else if (Controller.PlatformFromLastFrame.LaunchVerticallyOnExit)
            {
                float vBoost = platformVel.y * MoveStats.VerticalLaunchMultiplierOnLaunchExit;
                Velocity.y += vBoost;
            }
        }
    } 
    
    private void TurnCheck(Vector2 moveInput)
    {
        if (IsFacingRight && moveInput.x < 0)
        {
            Turn(false);
        }
        else if (!IsFacingRight && moveInput.x > 0)
        {
            Turn(true);
        }
    }

    private void Turn(bool turnRight)
    {
        IsFacingRight = turnRight;
        int mult = IsFacingRight ? 1 : -1;
        _visualsTransform.localScale = new Vector3(Mathf.Abs(_visualsTransform.localScale.x) * mult,
            _visualsTransform.localScale.y, _visualsTransform.localScale.z);
    }

    private void CalculateTargetRotation()
    {
        if (!MoveStats.MatchVisualsToSlope) return;

        Vector3 targetNormal = Controller.State.AveragedVisualNormal;
        float signedAngle = Vector2.SignedAngle(Vector2.up, targetNormal);
        float clampedAngle =
            Mathf.Clamp(signedAngle, -MoveStats.MaxVisualRotationAngle, MoveStats.MaxVisualRotationAngle);
        Quaternion slopeRotation = Quaternion.AngleAxis(clampedAngle, Vector3.forward);

        _targetRotation = slopeRotation;
    }

    private void RotateVisualTarget(float timeStep)
    {
        _visualsTransform.rotation = Quaternion.Slerp(_visualsTransform.rotation, _targetRotation,
            MoveStats.SlopeRotationSpeed * timeStep);

        float angleDegrees = _visualsTransform.rotation.eulerAngles.z;
        if (angleDegrees > 180) angleDegrees -= 360;
        float angleRad = Mathf.Abs(angleDegrees * Mathf.Deg2Rad);
        
        float halfWidth = _coll.bounds.size.x / 2f;
        float halfHeight = _coll.bounds.size.y / 2f;

        float currentDistanceToBottom = (halfWidth * Mathf.Sin(angleRad)) + (halfHeight * Mathf.Cos(angleRad));
        
        float liftAmount = currentDistanceToBottom - halfHeight;

        _visuals.PivotOffset = new Vector3(0f, -liftAmount, 0f);
    }

    #endregion

    #region Land/Fall

    private void LandCheck()
    {
        if (Controller.IsGrounded())
        {
            bool isGroundAWall = Controller.State.SlopeAngle >= MoveStats.MinAngleForWallSlide &&
                                 Controller.State.SlopeAngle <= MoveStats.MaxAngleForWallSlide;
            if (isGroundAWall)
            {
                return;
            }
            
            // LANDED
            if ((IsJumping || _isFalling || _isWallJumpFalling || _isWallJumping || _isWallSlideFalling ||
                 _isWallSliding || _isDashFastFalling) && Velocity.y <= 0f)
            {
                ResetJumpValues();
                StopWallSlide();
                ResetWallJumpValues();
                ResetDashes();
                DashLand();

                Controller.UpdateSlopeMemory();
            }

            bool isStable = Controller.State.SlopeAngle <= MoveStats.MaxSlopeAngle;
            bool isWedged = Controller.State.IsAgainstWall;
            
            if (MoveStats.ResetAirJumpsOnMaxSlopeLand || isStable || isWedged)
            {
                _numberOfAirJumpsUsed = 0;
            }
        }
    }

    private void Fall(float timeStep)
    {
        // NORMAL GRAVITY WHILE FALLING
        if (!Controller.IsGrounded() && !IsJumping && !_isWallSliding && !_isWallJumping && !IsDashing && !_isDashFastFalling)
        {
            if (!_isFalling)
            {
                _isFalling = true;
            }
            
            Velocity.y += MoveStats.Gravity * timeStep;
        }
    }

    private void VelocityReset()
    {
        if (Controller.IsSliding) return;

        if (Controller.IsGrounded())
        {
            if (!IsSlideableSlope(Controller.State.SlopeAngle))
            {
                if (Velocity.y <= 0f)
                {
                    Velocity.y = -2f;
                }
            }
        }
    }

    #endregion
    
    #region Jump

    private void ResetJumpValues()
    {
        IsJumping = false;
        _isFalling = false;
        _isFastFalling = false;
        _fastFallTime = 0f;
        _isPastApexThreshold = false;
    }
    
    private void JumpChecks()
    {
        // WHEN WE PRESS THE JUMP BUTTON
        if (_jumpPressed)
        {
            if (_isWallSlideFalling && _wallJumpCoyoteTimer >= 0f)
            {
                return;
            }
            
            else if (_isWallSliding || (Controller.IsTouchingWall() &&
                                        (!Controller.IsGrounded() || Controller.IsSliding ||
                                         Controller.State.IsAgainstSteepSlope)))
            {
                return;
            }
            
            _jumpBufferTimer = MoveStats.JumpBufferTime;
            _jumpReleasedDuringBuffer = false;
        }
        
        // WHEN WE RELEASE THE JUMP BUTTON
        if (_jumpReleased)
        {
            if (_jumpBufferTimer > 0f)
            {
                _jumpReleasedDuringBuffer = true;
            }
            
            if (IsJumping && Velocity.y > 0f)
            {
                if (_isPastApexThreshold)
                {
                    _isPastApexThreshold = false;
                    _isFastFalling = true;
                    _fastFallTime = MoveStats.TimeForUpwardsCancel;
                    Velocity.y = 0f;
                }
                else
                {
                    _isFastFalling = true;
                    _fastFallReleaseSpeed = Velocity.y;
                }
            }
        }

        // INITIATE JUMP WITH JUMP BUFFERING AND COYOTE TIME
        if (_jumpBufferTimer > 0f && !IsJumping && (Controller.IsGrounded() || _coyoteTimer > 0f) &&
            (MoveStats.CanJumpOnMaxSlopes || Controller.State.SlopeAngle <= MoveStats.MaxSlopeAngle))
        {
            InitiateJump(0);

            if (_jumpReleasedDuringBuffer)
            {
                _isFastFalling = true;
                _fastFallReleaseSpeed = Velocity.y;
            }
        }

        // DOUBLE JUMP
        else if (_jumpBufferTimer > 0f && !Controller.IsGrounded() && !Controller.IsTouchingWall())
        {
            if (AttemptWallJumpBuffer())
            {
                _jumpBufferTimer = 0f;
                return;
            }
            
            if (!IsDashing && (_isFalling || IsJumping || _isWallJumping || _isWallSlideFalling || _isAirDashing ||
                 _isDashFastFalling || Controller.IsSliding) && _numberOfAirJumpsUsed < MoveStats.NumberOfAirJumpsAllowed)
            {
                _isFastFalling = false;
                InitiateJump(1);

                if (_isDashFastFalling)
                {
                    _isDashFastFalling = false;
                }
            }
        } 

        // handle air jump AFTER the coyote time has lapsed (take off an extra jump so we don't get a bonus jump)
        else if (_jumpBufferTimer > 0f && _isFalling && !_isWallSlideFalling && _numberOfAirJumpsUsed < MoveStats.NumberOfAirJumpsAllowed)
        {
            InitiateJump(1);
            _isFastFalling = false;
        }
    }

    private void InitiateJump(int numberOfAirJumpsUsed)
    {
        if (!IsJumping)
        {
            IsJumping = true;
        }

        _jumpPressed = false;
        
        ResetWallJumpValues();

        _fastFallTime = 0f;
        _isFastFalling = false;
        _isPastApexThreshold = false;
        _jumpBufferTimer = 0f;
        _numberOfAirJumpsUsed += numberOfAirJumpsUsed;
        Velocity.y = MoveStats.InitialJumpVelocity;

        IsDashing = false;
        _isAirDashing = false;
        _isDashFastFalling = false;

        if (MoveStats.InheritPlatformMomentum && _platformMomentumRetentionTimer > 0f)
        {
            Vector2 platformVel = _storePlatformVelocity;

            float hBoost = platformVel.x * MoveStats.PlatformHorizontalMomentumMultiplier;
            float vBoost = platformVel.y * MoveStats.PlatformVerticalMomentumMultiplier;

            vBoost = Mathf.Clamp(vBoost, 0f, MoveStats.MaxVerticalBoost);
            
            Velocity.x += hBoost;
            Velocity.y += vBoost;
        }
        
        if (MoveStats.DebugTrackJumpHeight)
        {
            _debugJumpStartY = _rb.position.y;
            _debugMaxHeightY = _rb.position.y;
            _debugTrackingJump = true;
        }
    }
    
    private void Jump(float timeStep)
    { 
        // APPLY GRAVITY WHILE JUMPING
        if (IsJumping)
        {
            // CHECK FOR HEAD BUMP
            if (Controller.BumpedHead())
            {
                if (MoveStats.JumpFollowSlopesWhenHeadTouching && Controller.State.CeilingAngle > 0f)
                {
                    Vector2 ceilingNormal = Controller.State.CeilingNormal;
                    Velocity -= (Vector2.Dot(Velocity, ceilingNormal) * ceilingNormal);
                }
                else
                {
                    Velocity.y = 0f;
                    _isFastFalling = true;
                }
            }
            
            // GRAVITY ON ASCENDING
            if (Velocity.y >= 0f)
            {
                // APEX CONTROLS
                _apexPoint = Mathf.InverseLerp(MoveStats.InitialJumpVelocity, 0f, Velocity.y);

                if (_apexPoint > MoveStats.ApexThreshold)
                {
                    if (!_isPastApexThreshold)
                    {
                        _isPastApexThreshold = true;
                        _timePastApexThreshold = 0f;
                    }

                    if (_isPastApexThreshold)
                    {
                        _timePastApexThreshold += timeStep;
                        if (_timePastApexThreshold < MoveStats.ApexHangTime)
                        {
                            Velocity.y = 0f;
                        }
                        else
                        {
                            Velocity.y = -0.01f;
                        }
                    }
                }

                // GRAVITY ON ASCENDING BUT NOT PAST APEX THRESHOLD
                else if (!_isFastFalling)
                {
                    Velocity.y += MoveStats.Gravity * timeStep;
                    if (_isPastApexThreshold)
                    {
                        _isPastApexThreshold = false;
                    }
                }
            }

            // GRAVITY ON DESCENDING
            else if (!_isFastFalling)
            {
                Velocity.y += MoveStats.Gravity * MoveStats.GravityOnReleaseMultiplier * timeStep;
            }

            else if (Velocity.y < 0f)
            {
                if (!_isFalling)
                {
                    _isFalling = true;
                }
            }
        }
        
        // JUMP CUT
        if (_isFastFalling)
        {
            if (_fastFallTime >= MoveStats.TimeForUpwardsCancel)
            {
                Velocity.y += MoveStats.Gravity * MoveStats.GravityOnReleaseMultiplier * timeStep;
            }
            else if (_fastFallTime < MoveStats.TimeForUpwardsCancel)
            {
                Velocity.y = Mathf.Lerp(_fastFallReleaseSpeed, 0f,
                    (_fastFallTime / MoveStats.TimeForUpwardsCancel));
            }
            
            _fastFallTime += timeStep;
        }
    }

    #endregion

    #region Wall Slide

    private void WallSlideCheck()
    {
        bool isTouchingSideWall = Controller.IsTouchingWall();
        bool isSideWallAngle = Controller.State.WallAngle >= MoveStats.MinAngleForWallSlide &&
                               Controller.State.WallAngle <= MoveStats.MaxAngleForWallSlide;

        if (isTouchingSideWall && !MoveStats.CanWallSlideFacingAwayFormWall)
        {
            int facingDir = IsFacingRight ? 1 : -1;
            if (facingDir != Controller.State.WallDirection)
            {
                isTouchingSideWall = false;
            }
        }
        
        if (!IsDashing && isTouchingSideWall && isSideWallAngle && !Controller.IsGrounded())
        {
            if (Velocity.y < 0f && !_isWallSliding)
            {
                ResetJumpValues();
                ResetWallJumpValues();
                ResetDashValues();

                if (MoveStats.ResetDashOnWallSlide)
                {
                    ResetDashes();
                }
                
                _isWallSlideFalling = false;
                _isWallSliding = true;

                if (MoveStats.ResetJumpsOnWallSlide)
                {
                    _numberOfAirJumpsUsed = 0;
                }
            }
        }
        
        else if (_isWallSliding && !isTouchingSideWall)
        {
            _isWallSlideFalling = true;

            if (_platformMomentumRetentionTimer > 0f)
            {
                Velocity.y += _storePlatformVelocity.y;
                Velocity.x += _storePlatformVelocity.x;
            }
            
            StopWallSlide();
        }

        else
        {
            StopWallSlide();
        }
    }

    private void StopWallSlide()
    {
        if (_isWallSliding)
        {
            _isWallSliding = false;
        }
    }

    private void WallSlide(float timeStep)
    {
        if (_isWallSliding)
        {
            Velocity.y = Mathf.Lerp(Velocity.y, -MoveStats.WallSlideSpeed,
                MoveStats.WallSlideDecelerationSpeed * timeStep);
        }
    }

    #endregion

    #region Wall Jump

    private bool AttemptWallJumpBuffer()
    {
        if (MoveStats.WallJumpInputBufferDistance <= 0f) return false;

        int[] directionsToCheck;

        if (MoveStats.CanWallSlideFacingAwayFormWall)
        {
            directionsToCheck = new int[] { 1, -1 };
        }
        else
        {
            directionsToCheck = new int[] { IsFacingRight ? 1 : -1 };
        }

        foreach (int dir in directionsToCheck)
        {
            Vector2 direction = Vector2.right * dir;
            Vector2 origin = _coll.bounds.center;
            Vector2 size = _coll.bounds.size;
            RaycastHit2D hit = Physics2D.BoxCast(origin, size, 0f, direction, MoveStats.WallJumpInputBufferDistance,
                MoveStats.GroundLayer);

            #region Debug Visualization

            if (MoveStats.DebugShowWallJumpBufferBox)
            {
                float duration = 0.5f;
                float drawDistance = hit ? hit.distance : MoveStats.WallJumpInputBufferDistance;
                bool isValidHit = false;

                if (hit)
                {
                    float angle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.up));
                    if (angle >= MoveStats.MinAngleForWallSlide && angle <= MoveStats.MaxAngleForWallSlide)
                    {
                        isValidHit = true;
                    }
                }
                
                Color debugColor = isValidHit ? Color.cyan : Color.red;
                
                DrawDebugBox(origin + (direction * drawDistance), size, debugColor, duration);
                Debug.DrawLine(origin, origin + (direction * drawDistance), debugColor, duration);
            }

            #endregion

            if (hit)
            {
                float wallAngle = Mathf.Round(Vector2.Angle(hit.normal, Vector2.up));
                bool isValidWall = wallAngle >= MoveStats.MinAngleForWallSlide &&
                                   wallAngle <= MoveStats.MaxAngleForWallSlide;

                if (isValidWall)
                {
                    var platform = hit.collider.GetComponent<IVelocityInheritable>();
                    if (platform != null)
                    {
                        _storePlatformVelocity = platform.GetVelocity();
                        _platformMomentumRetentionTimer = MoveStats.PlatformMomentumRetentionTime;
                    }
                    
                    _lastWallDir = -(int)Mathf.Sign(hit.normal.x);
                    
                    InitiateWallJump();
                    return true;
                }
            }
        }
        
        return false;
    }
    
    private void ResetWallJumpValues()
    {
        _isWallSlideFalling = false;
        _useWallJumpMoveStats = false;
        _isWallJumping = false;
        _isWallJumpFastFalling = false;
        _isWallJumpFalling = false;
        _isPastWallJumpApexThreshold = false;
        
        _wallJumpFastFallTime = 0f;
        _wallJumpTime = 0f;
    }
    
    private void WallJumpCheck()
    {
        if (ShouldWallJumpCoyote())
        {
            _wallJumpCoyoteTimer = MoveStats.WallJumpCoyoteTime;
        }
        
        // wall jump fast falling
        if (_jumpReleased && !_isWallSliding && !Controller.IsTouchingWall() && _isWallJumping)
        {
            if (Velocity.y > 0f)
            {
                if (_isPastWallJumpApexThreshold)
                {
                    _isPastWallJumpApexThreshold = false;
                    _isWallJumpFastFalling = true;
                    _wallJumpFastFallTime = MoveStats.TimeForUpwardsCancel;

                    Velocity.y = 0f;
                }
                else
                {
                    _isWallJumpFastFalling = true;
                    _wallJUmpFastFallReleaseSpeed = Velocity.y;
                }
            }
        }
        
        // actual jump with post wall jump buffer time
        if (_jumpPressed && _wallJumpCoyoteTimer > 0f)
        {
            InitiateWallJump();
        }
    }

    private void InitiateWallJump()
    {
        if (!_isWallJumping)
        {
            _isWallJumping = true;
            _useWallJumpMoveStats = true;
        }

        _jumpPressed = false;
        
        StopWallSlide();
        ResetJumpValues();
        _wallJumpTime = 0f;
        _numberOfAirJumpsUsed = 0;

        float calculatedBaseX = Mathf.Abs(MoveStats.WallJumpDirection.x) * -_lastWallDir;

        Velocity.y = MoveStats.InitialWallJumpVelocity;
        Velocity.x = calculatedBaseX;
        
        if (MoveStats.InheritPlatformMomentum && _platformMomentumRetentionTimer > 0f)
        {
            Vector2 platformVel = _storePlatformVelocity;

            float hBoost = platformVel.x * MoveStats.PlatformHorizontalMomentumMultiplier;
            float vBoost = platformVel.y * MoveStats.PlatformVerticalMomentumMultiplier;

            vBoost = Mathf.Clamp(vBoost, 0f, MoveStats.MaxVerticalBoost);

            if (Mathf.Sign(hBoost) != Mathf.Sign(calculatedBaseX) && Mathf.Abs(hBoost) > 0.01f)
            {
                hBoost = 0f;
            }
            
            Velocity.x += hBoost;
            Velocity.y += vBoost;
        }
    }

    private void WallJump(float timeStep)
    {
        // APPLY WALL JUMP GRAVITY
        if (_isWallJumping)
        {
            // TIME TO TAKE OVER MOVEMENT CONTROLS WHILE ALL JUMPING
            _wallJumpTime += timeStep;
            if (_wallJumpTime >= MoveStats.TimeTillJumpApex)
            {
                _useWallJumpMoveStats = false;
            }
            
            // HIT HEAD
            if (Controller.BumpedHead())
            {
                if (MoveStats.JumpFollowSlopesWhenHeadTouching && Controller.State.CeilingAngle > 0f)
                {
                    Vector2 ceilingNormal = Controller.State.CeilingNormal;
                    Velocity -= (Vector2.Dot(Velocity, ceilingNormal) * ceilingNormal);
                }
                else
                {
                    Velocity.y = 0f;
                    _isWallJumpFastFalling = true;
                    _useWallJumpMoveStats = false;
                }
            }
            
            
            // GRAVITY IN ASCENDING
            if (Velocity.y >= 0f)
            {
                // APEX CONTROLS
                _wallJumpApexPoint = Mathf.InverseLerp(MoveStats.WallJumpDirection.y, 0f, Velocity.y);

                if (_wallJumpApexPoint > MoveStats.ApexThreshold)
                {
                    if (!_isPastWallJumpApexThreshold)
                    {
                        _isPastWallJumpApexThreshold = true;
                        _timePastWallJumpApexThreshold = 0f;
                    }

                    if (_isPastWallJumpApexThreshold)
                    {
                        _timePastWallJumpApexThreshold += timeStep;
                        if (_timePastWallJumpApexThreshold < MoveStats.ApexHangTime)
                        {
                            Velocity.y = 0f;
                        }
                        else
                        {
                            Velocity.y = -0.01f;
                        }
                    }
                }

                // GRAVITY IN ASCENDING BUT NOT PAST APEX THRESHOLD
                else if (!_isWallJumpFastFalling)
                {
                    Velocity.y += MoveStats.WallJumpGravity * timeStep;

                    if (_isPastWallJumpApexThreshold)
                    {
                        _isPastWallJumpApexThreshold = false;
                    }
                }
            }

            // GRAVITY ON DESENDING
            else if (!_isWallJumpFastFalling)
            {
                Velocity.y += MoveStats.WallJumpGravity * timeStep;
            }

            else if (Velocity.y < 0f)
            {
                if (!_isWallJumpFalling)
                    _isWallJumpFalling = true;
            }
        }
        
        // HANDLE WALL JUMP CUT TIME
        if (_isWallJumpFastFalling)
        {
            if (_wallJumpFastFallTime >= MoveStats.TimeForUpwardsCancel)
            {
                Velocity.y += MoveStats.WallJumpGravity * MoveStats.WallJumpGravityOnReleaseMultiplier * timeStep;
            }
            else if (_wallJumpFastFallTime < MoveStats.TimeForUpwardsCancel)
            {
                Velocity.y = Mathf.Lerp(_wallJUmpFastFallReleaseSpeed, 0f,
                    (_wallJumpFastFallTime / MoveStats.TimeForUpwardsCancel));
            }
            
            _wallJumpFastFallTime += timeStep;
        }
    }

    private bool ShouldWallJumpCoyote()
    {
        bool isWallAngleValid = Controller.State.WallAngle >= MoveStats.MinAngleForWallSlide &&
                                Controller.State.WallAngle <= MoveStats.MaxAngleForWallSlide;
        
        if (Controller.IsTouchingWall() && isWallAngleValid || _isWallSliding)
        {
            if (Controller.State.WallDirection != 0)
            {
                _lastWallDir = Controller.GetWallDirection();
            }
            return true;
        }
        else
        {
            return false;
        }
    }

    #endregion

    #region Dash

    private void DashCheck()
    {
        if (_dashPressed)
        {
            _dashBufferTimer = MoveStats.DashBufferTime;
        }
        
        if (_dashBufferTimer > 0f)
        {
            // ground dash
            if ((Controller.IsGrounded() || _dashCoyoteTimer > 0f) && _dashOnGroundTimer < 0 && !IsDashing)
            {
                InitiateDash();
                _dashBufferTimer = 0f;

                if (!Controller.IsGrounded())
                {
                    _numberOfDashesUsed--;
                }
            }
            
            // air dash
            else if (!Controller.IsGrounded() && !IsDashing && _numberOfDashesUsed < MoveStats.NumberOfDashes)
            {
                _isAirDashing = true;
                InitiateDash();
                _dashBufferTimer = 0f;
            }
        }
    }

    private void CalculateDashDirection()
    {
        Vector2 input = _moveInput;
        // if (input.magnitude > 0.1f) input.Normalize();
        
        TurnCheck(input);

        if (input == Vector2.zero)
        {
            _dashDirection = IsFacingRight ? Vector2.right : Vector2.left;
            return;
        }

        bool isUpperHemisphere = input.y >= 0;
        Vector2 verticalDirection = isUpperHemisphere ? Vector2.up : Vector2.down;

        float verticalAngleTolerance =
            isUpperHemisphere ? MoveStats.DashUpwardAngleTolerance : MoveStats.DashDownwardAngleTolerance;
        float verticalThreshold = Mathf.Cos(verticalAngleTolerance * Mathf.Deg2Rad);

        float horizontalThreshold = Mathf.Cos(MoveStats.DashHorizontalAngleTolerance * Mathf.Deg2Rad);
        
        Vector2 finalDir = Vector2.zero;

        if (Vector2.Dot(input, verticalDirection) >= verticalThreshold)
        {
            finalDir = verticalDirection;
        }
        else if (Mathf.Abs(Vector2.Dot(input, Vector2.right)) >= horizontalThreshold)
        {
            finalDir = Mathf.Sign(input.x) == 1 ? Vector2.right : Vector2.left;
        }
        else
        {
            finalDir = new Vector2(Mathf.Sign(input.x), isUpperHemisphere ? 1 : -1).normalized;
        }

        _dashDirection = finalDir;
        _dashIntentDirection = finalDir;

        if (Controller.IsGrounded() && finalDir.y < 0 && finalDir.x != 0)
        {
            _dashDirection = new Vector2(Mathf.Sign(finalDir.x), 0);
        }
    }

    private void InitiateDash()
    {
        _dashPressed = false;
        
        _numberOfDashesUsed++;
        IsDashing = true;

        _dashDelayTimer = MoveStats.DashFreezeTime;
        
        _dashTimer = 0f;
        _dashOnGroundTimer = MoveStats.TimeBtwDashesOnGround;
        
        ResetJumpValues();
        ResetWallJumpValues();
        StopWallSlide();

        Velocity.y = 0f;
        Velocity.x = 0f;
    }

    private void Dash(float timeStep)
    {
        if (IsDashing)
        {
            if (_dashDelayTimer > 0f)
            {
                CalculateDashDirection();
                Velocity.x = 0f;
                Velocity.y = 0f;
                
                _dashDelayTimer -= timeStep;
                
                if (_dashDelayTimer <= 0f)
                {
                    _isPerformingSLopeDash = Controller.IsGrounded() && Controller.State.SlopeAngle > 0 && _dashDirection.y == 0 &&
                                             !IsJumping && Mathf.Sign(_dashDirection.x) != Mathf.Sign(Controller.State.SlopeNormal.x);
                    if (_isPerformingSLopeDash)
                    {
                        _slopeDashAngle = Controller.State.SlopeAngle;
                    }
                }
                else
                {
                    return;
                }
            }
            
            if (Controller.BumpedHead())
            {
                if (MoveStats.DashFollowSlopesWhenHeadTouching && Controller.State.CeilingAngle > 0f)
                {
                    Vector2 ceilingNormal = Controller.State.CeilingNormal;
                    Velocity -= (Vector2.Dot(Velocity, ceilingNormal) * ceilingNormal);
                }
                else
                {
                    if (MoveStats.CancelDashWhenYouHitCeiling)
                    {
                        Velocity.y = 0;
                        IsDashing = false;
                        _isAirDashing = false;
                        _dashTimer = MoveStats.DashTime;
                    }
                    else
                    {
                        
                    }
                }
            }
            
            // stop the dash after the timer
            _dashTimer += timeStep;
            if (_dashTimer >= MoveStats.DashTime)
            {
                if (_dashIntentDirection.y >= 0)
                {
                    if (Mathf.Abs(Velocity.x) > MoveStats.MaxRunSpeed)
                    {
                        Velocity.x = MoveStats.MaxRunSpeed * Mathf.Sign(Velocity.x);
                    }
                }
                
                if (Controller.IsGrounded())
                {
                    ResetDashes();
                }

                _isAirDashing = false;
                IsDashing = false;

                if (!IsJumping && !_isWallJumping)
                {
                    _dashFastFallTime = 0f;
                    _dashFastFallReleaseSpeed = Velocity.y;

                    if (!Controller.IsGrounded())
                    {
                        _isDashFastFalling = true;
                    }
                    else
                    {
                        Velocity.y = 0f;
                    }
                }
                
                return;
            }

            if (MoveStats.DashDirectionMatchesSlopeDirection && _isPerformingSLopeDash)
            {
                Velocity.x = Mathf.Cos(_slopeDashAngle * Mathf.Deg2Rad) * MoveStats.DashSpeed * _dashDirection.x;
                Velocity.y = Mathf.Sin(_slopeDashAngle * Mathf.Deg2Rad) * MoveStats.DashSpeed;
            }
            else
            {
                Velocity.x = MoveStats.DashSpeed * _dashDirection.x;

                if (_dashDirection.y != 0f || _isAirDashing)
                {
                    Velocity.y = MoveStats.DashSpeed * _dashDirection.y;
                }
                else if (!IsJumping && _dashDirection.y == 0f)
                {
                    Velocity.y = -0.001f;
                }
            }

            #region Debug Dash Angle Visualization

            if (MoveStats.DebugShowDashAngle)
            {
                Vector2 drawOrigin = _coll.bounds.center;
                Vector2 drawDirection = Velocity.normalized;
                float drawLength = MoveStats.ExtraRayDebugDistance * 4f;
                
                Debug.DrawRay(drawOrigin, drawDirection * drawLength, Color.cyan);
            }

            #endregion
        }
        
        // HANDLE DASH CUT TIME
        else if (_isDashFastFalling)
        {
            if (Velocity.y > 0f)
            {
                if (_dashFastFallTime < MoveStats.DashTimeForUpwardsCancel)
                {
                    Velocity.y = Mathf.Lerp(_dashFastFallReleaseSpeed, 0f,
                        (_dashFastFallTime / MoveStats.DashTimeForUpwardsCancel));
                }
                else if (_dashFastFallTime >= MoveStats.DashTimeForUpwardsCancel)
                {
                    Velocity.y += MoveStats.Gravity * MoveStats.DashGravityOnReleaseMultiplier * timeStep;
                }
                
                _dashFastFallTime += timeStep;
            }

            else
            {
                Velocity.y += MoveStats.Gravity * MoveStats.DashGravityOnReleaseMultiplier * timeStep;
            }
        }
    }
    
    private void ResetDashValues()
    {
        _isDashFastFalling = false;
        _dashOnGroundTimer = -0.01f;

        _dashFastFallReleaseSpeed = 0f;
        _dashFastFallTime = 0f;
        _dashDirection = Vector2.zero;
        _isPerformingSLopeDash = false;
    }

    private void DashLand()
    {
        _isDashFastFalling = false;
        _dashOnGroundTimer = -0.01f;
        _dashFastFallReleaseSpeed = 0f;
        _dashFastFallTime = 0f;
        _isPerformingSLopeDash = false;
    }

    private void ResetDashes()
    {
        _numberOfDashesUsed = 0;
    }

    #endregion

    #region Slide

    private void HandleSlide(float timeStep)
    {
        if (Controller.IsSliding)
        {
            if (IsJumping) return;
            if (_isWallJumping) return;

            Velocity.y += MoveStats.Gravity * timeStep;
        }
    }

    #endregion
    
    #region Timers

    private void CountTimers(float timeStep)
    {
        // jump buffer
        _jumpBufferTimer -= timeStep;

        // jump coyote time
        HandleCoyoteTimer(timeStep);
        
        // wall jump buffer timer
        _wallJumpCoyoteTimer -= timeStep;
        
        
        // dash timer
        HandleDashOnGroundTimer(timeStep);
        HandleDashCoyoteTimer(timeStep);
        
        // dash buffer timer
        _dashBufferTimer -= timeStep;
        
        // platform momentum timer
        ManagePlatformMomentum(timeStep);
    }

    private void HandleCoyoteTimer(float timeStep)
    {
        if (Controller.IsGrounded() && !Controller.IsSliding)
        {
            _coyoteTimer = MoveStats.JumpCoyoteTime;
        }
        else
        {
            _coyoteTimer -= timeStep;
        }
    }

    private void HandleDashOnGroundTimer(float timeStep)
    {
        if (Controller.IsGrounded() && !Controller.IsSliding)
        {
            _dashOnGroundTimer -= timeStep;
        }
    }

    private void HandleDashCoyoteTimer(float timeStep)
    {
        if (Controller.IsGrounded() && !Controller.IsSliding)
        {
            _dashCoyoteTimer = MoveStats.DashCoyoteTime;
        }
        else
        {
            _dashCoyoteTimer -= timeStep;
        }
    }

    private void ManagePlatformMomentum(float timeStep)
    {
        if (Controller.LastKnownPlatform != null)
        {
            _storePlatformVelocity = Controller.LastKnownPlatform.GetVelocity();
            _platformMomentumRetentionTimer = MoveStats.PlatformMomentumRetentionTime;
        }
        else
        {
            if (_platformMomentumRetentionTimer > 0f)
            {
                _platformMomentumRetentionTimer -= timeStep;

                if (_platformMomentumRetentionTimer <= 0f)
                {
                    _storePlatformVelocity = Vector2.zero;
                }
            }
        }
    }

    #endregion

    #region Crush

    public void HandleCrush()
    {
        Debug.Log("crushed");
        _visuals.ForceTeleport(_respawnPoint.position);
    }

    #endregion
    
    #region Helper Methods

    public bool IsSlideableSlope(float slopeAngle)
    {
        if (slopeAngle >= MoveStats.MaxSlopeAngle && slopeAngle < MoveStats.MinAngleForWallSlide)
        {
            return true;
        }
        
        return false;
    }

    public bool IsWalkableSlope(float angle)
    {
        return angle <= MoveStats.MaxSlopeAngle && angle < MoveStats.MinAngleForWallSlide;
    }

    public bool IsWallSlideable(float angle)
    {
        return angle >= MoveStats.MinAngleForWallSlide && angle <= MoveStats.MaxAngleForWallSlide;
    }

    #endregion
    
    #region Visualization

    private void DrawJumpArc(float moveSpeed, Color gizmoColor)
    {
        Vector2 startPosition = new Vector2(_coll.bounds.center.x, _coll.bounds.min.y);
        Vector2 previousPosition = startPosition;
        float speed = 0f;
        if (MoveStats.DrawRight)
        {
            speed = moveSpeed;
        }
        else { speed = -moveSpeed; }
        Vector2 velocity = new Vector2(speed, MoveStats.InitialJumpVelocity);

        Gizmos.color = gizmoColor;

        float timeStep = 2 * MoveStats.TimeTillJumpApex / MoveStats.ArcResolution; // time step for the simulation
        //float totalTime = (2 * MoveStats.TimeTillJumpApex + MoveStats.ApexHangTime); // total time of the arc including hang time

        for (int i = 0; i < MoveStats.VisualizationSteps; i++)
        {
            float simulationTime = i * timeStep;
            Vector2 displacement;
            Vector2 drawPoint;

            if (simulationTime < MoveStats.TimeTillJumpApex) // Ascending
            {
                displacement = velocity * simulationTime + 0.5f * new Vector2(0, MoveStats.Gravity) * simulationTime * simulationTime;
            }
            else if (simulationTime < MoveStats.TimeTillJumpApex + MoveStats.ApexHangTime) // Apex hang time
            {
                float apexTime = simulationTime - MoveStats.TimeTillJumpApex;
                displacement = velocity * MoveStats.TimeTillJumpApex + 0.5f * new Vector2(0, MoveStats.Gravity) * MoveStats.TimeTillJumpApex * MoveStats.TimeTillJumpApex;
                displacement += new Vector2(speed, 0) * apexTime; // No vertical movement during hang time
            }
            else // Descending
            {
                float descendTime = simulationTime - (MoveStats.TimeTillJumpApex + MoveStats.ApexHangTime);
                displacement = velocity * MoveStats.TimeTillJumpApex + 0.5f * new Vector2(0, MoveStats.Gravity) * MoveStats.TimeTillJumpApex * MoveStats.TimeTillJumpApex;
                displacement += new Vector2(speed, 0) * MoveStats.ApexHangTime; // Horizontal movement during hang time
                displacement += new Vector2(speed, 0) * descendTime + 0.5f * new Vector2(0, MoveStats.Gravity) * descendTime * descendTime;
            }

            drawPoint = startPosition + displacement;

            if (MoveStats.StopOnCollision)
            {
                RaycastHit2D hit = Physics2D.Raycast(previousPosition, drawPoint - previousPosition, Vector2.Distance(previousPosition, drawPoint), MoveStats.GroundLayer);
                if (hit.collider != null)
                {
                    // If a hit is detected, stop drawing the arc at the hit point
                    Gizmos.DrawLine(previousPosition, hit.point);
                    break;
                }
            }

            Gizmos.DrawLine(previousPosition, drawPoint);
            previousPosition = drawPoint;
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
    
    #endregion
}
