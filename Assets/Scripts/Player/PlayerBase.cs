using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using JetBrains.Annotations;
using NUnit.Framework;
using Unity.Collections.Tests.CoreCLR.TestJobs;
using Unity.Mathematics;
using Unity.Tutorials.Editor;
using Unity.VisualScripting;
using UnityEditor.Callbacks;
using UnityEditor.Experimental.GraphView;
using UnityEditor.Tilemaps;
using UnityEditor.U2D.Aseprite;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SearchService;
using UnityEngine.SocialPlatforms;
using UnityEngine.Serialization;

public class PlayerBase : MonoBehaviour
{
    private enum MantlePhase
    {
        Rising,
        MovingForward,
        Descending
    }

    // Vereables

    // Game Components
    protected Rigidbody2D rb;
    protected Animator anim;
    protected Collider2D col;
    protected SpriteRenderer sr;

    // Veriables

    [Header("Movement")]
    [SerializeField] private float movementSpeed = 4f;

    [Header("Jump")]
    [SerializeField] private float groundJumpForce = 8f;
    [SerializeField] private float airJumpForce = 7f;
    [SerializeField] private float maxJumpSteps = 12f;
    [SerializeField] private float coyoteTime = 0.2f;
    [SerializeField] private float maxFallSpeed = 18f;

    [Header("Dash")]
    [SerializeField] private float maxDashSteps = 20f;
    [SerializeField] private float dashSpeed = 10f;

    [Header("Wall Movement")]
    [SerializeField] private float stickDuration = 0.3f;
    [SerializeField] private float wallSlideSpeed = 2;
    [SerializeField] private float stickTimer = 0;
    [SerializeField] private float xWallJumpForce = 5;
    [SerializeField] private float yWallJumpForce = 8;

    [Header("Mantle")]
    [SerializeField] private Transform mantleWallCheckPoint;
    [SerializeField] private Transform mantleLedgeCheckPoint;
    [FormerlySerializedAs("mantleEyeLevelCheckPoint")]
    [FormerlySerializedAs("mantleChestCheckPoint")]
    [SerializeField] private Transform mantleHeadLevelCheckPoint;
    [SerializeField] private LayerMask mantleZoneLayer;
    [SerializeField] private float mantleSearchRadius = 0.25f;
    [SerializeField] private float mantleCheckDistance = 0.4f;
    [SerializeField] private float mantleRayLength = 1.2f;
    [SerializeField] private float mantleMoveSpeed = 4f;
    [SerializeField] private float mantleMaxRise = 2f;
    [SerializeField] private AnimationCurve mantleMotionCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private float mantleJumpForce = 14f;
    [SerializeField] private float mantleRayStartHeight = 0.8f;

    [Header("Mantle State")]
    [SerializeField] private bool mantleZoneUsed;
    [SerializeField] private bool mantleJumpCancelRequested;
    [SerializeField] private Vector2 mantleStart;
    [SerializeField] private Vector2 mantleTarget;
    [SerializeField] private float mantleOriginalGravity;
    [SerializeField] private RigidbodyInterpolation2D mantleOriginalInterpolation;
    [SerializeField] private MantlePhase mantlePhase;
    [SerializeField] private Vector2 mantlePhaseStart;
    [SerializeField] private Vector2 mantlePhaseTarget;
    [SerializeField] private float mantlePhaseDuration;
    [SerializeField] private float mantlePhaseElapsed;

    [Header("Collision details")]
    [SerializeField] private float groundCheckDisance = 0.69f;
    [SerializeField] private LayerMask whatIsGround;
    [SerializeField] private float xRayOffsetLeft = -0.14f;
    [SerializeField] private float xRayOffsetRight = 0.14f;
    [SerializeField] private float wallCheckDistance = 0.171f;
    [SerializeField] private string whatIsWall = "Wall";
    [SerializeField] private float yRayOffsetUpOne = 0.36f;
    [SerializeField] private float yRayOffsetUpTwo = 0.1f;
    [SerializeField] private float yRayOffsetDownOne = -0.425f;
    [SerializeField] private float yRayOffsetDownTwo = -0.6f;
    [SerializeField] private LayerMask mantleObstacleLayers;


    //States
    [Header("States")]
    [SerializeField] private bool isGrounded;
    [SerializeField] private bool isJumping;
    [SerializeField] private bool isFacingRight = true;
    [SerializeField] private bool isDashing;
    [SerializeField] private bool isRunning;
    [SerializeField] private bool isOnWall;
    [SerializeField] private bool isWallSliding;
    [SerializeField] private bool isMantling;

    // Others
    [Header("Others")]
    [SerializeField] private bool canMove = true;
    [SerializeField] private bool canJump = true;
    [SerializeField] private bool canDash;
    [SerializeField] private float velocityX;
    [SerializeField] private float velocityY;
    [SerializeField] private float xInput;
    [SerializeField] private float yInput;
    [SerializeField] private float direction = 1;
    [SerializeField] private int jumpSteps;
    [SerializeField] private int dashSteps;
    [SerializeField] private float coyoteTimer;
    [SerializeField] private bool wasFacingRight = true;
    [SerializeField] private float turnDamping = 3;
    [SerializeField] private float wallJumpLockTimer;
    [SerializeField] private bool didWallJump;

    // Permenent Commands
    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponentInChildren<Animator>();
        col = GetComponent<Collider2D>();
        sr = GetComponentInChildren<SpriteRenderer>();
    }

    void Update()
    {
        HandleInput();
        if (isMantling)
        {
            if (Input.GetKeyDown(KeyCode.Z))
                mantleJumpCancelRequested = true;

            HandleAnimations();
            return;
        }
        HandleDash();
        HandleRunning();
        HandleJump();
        HandleAnimations();
    }


    void FixedUpdate()
    {
        // Veriables
        velocityX = rb.linearVelocity.x;
        velocityY = rb.linearVelocity.y;
        wallJumpLockTimer = Mathf.Max(0f, wallJumpLockTimer - Time.fixedDeltaTime);

        // Commands
        if (isMantling)
        {
            AdvanceMantle();
            return;
        }
        HandleMantling();
        HandleCollision();
        bool inMantleZone =
            mantleLedgeCheckPoint != null &&
            Physics2D.OverlapCircle(
                mantleLedgeCheckPoint.position,
                mantleSearchRadius,
                mantleZoneLayer) != null;

        if (!inMantleZone)
            mantleZoneUsed = false;

        if (inMantleZone && !mantleZoneUsed && TryStartMantle())
        {
            mantleZoneUsed = true;
            return;
        }
        HandleFlip();
        HandleMovement();
        HandleRunning();
        HandleDashing();
        HandleWallSlide();
        HandleJumping();
    }

    private void HandleReturn()
    {
        if (!isGrounded && !isDashing && TryStartMantle())
        {
            return;
        }
    }
    private void HandleMantle()
    {
        if (isMantling)
        {
            if (Input.GetKeyDown(KeyCode.Z))
            {
                mantleJumpCancelRequested = true;
            }

            HandleAnimations();
            return;
        }
    }

    private void HandleMantling()
    {
        if (isMantling)
        {
            AdvanceMantle();
            return;
        }
    }

    // Spesific Commands
    private void HandleWallSlide()
    {
        if (isMantling)
        {
            isOnWall = false;
        }
        if (isOnWall)
        {
            isDashing = false;
        }
        if (isOnWall && !isGrounded)
        {

            if (!isWallSliding)
            {
                isWallSliding = true;
                stickTimer = stickDuration;
                if (!didWallJump && wallJumpLockTimer <= 0f)
                {
                    rb.linearVelocity = new Vector2(0f, 0f);
                }
            }

            if (stickTimer > 0)
            {
                stickTimer -= Time.fixedDeltaTime;
                if (!didWallJump && wallJumpLockTimer <= 0f)
                {
                    rb.linearVelocity = new Vector2(0f, 0f);
                }
            }
            else
            {
                float clapedY = MathF.Max(rb.linearVelocity.y, -wallSlideSpeed);
                if (!didWallJump && wallJumpLockTimer <= 0f)
                {
                    rb.linearVelocity = new Vector2(0f, clapedY);
                }
            }
        }
        else
        {
            isWallSliding = false;
        }
        didWallJump = false;

    }

    private void HandleDashing()
    {
        if (isDashing)
        {
            if (dashSteps < maxDashSteps)
            {
                rb.linearVelocity = new Vector2((isFacingRight ? 1f : -1f) * math.max(dashSpeed, math.abs(rb.linearVelocity.x)), rb.linearVelocity.y);
                dashSteps++;
            }
            if (dashSteps == maxDashSteps)
            {
                dashSteps++;
                isDashing = false;
                isRunning = true;
            }
        }
    }
    private void HandleDash()
    {
        if (Input.GetKeyDown(KeyCode.C) && canDash)
        {
            isDashing = true;
            dashSteps = 0;
        }
        if (Input.GetKeyUp(KeyCode.C) && dashSteps >= maxDashSteps)
        {
            canDash = false;
            StopDashAndRun();
        }
        if (isGrounded)
        {
            canDash = true;
        }
    }

    private void HandleRunning()
    {
        if (isRunning)
        {
            if (Input.GetKey(KeyCode.C))
            {
                movementSpeed = 6f;
            }
            if (Input.GetKeyUp(KeyCode.C) || !(Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow)))
            {
                isRunning = false;
                movementSpeed = 4f;
            }
        }
    }

    protected virtual void HandleCollision()
    {
        Vector2 xRayOriginOne = (Vector2)transform.position + new Vector2(xRayOffsetLeft, 0);
        Vector2 xRayOriginTwo = (Vector2)transform.position + new Vector2(xRayOffsetRight, 0);
        isGrounded = (Physics2D.Raycast(xRayOriginOne, Vector2.down, groundCheckDisance, whatIsGround) || Physics2D.Raycast(xRayOriginTwo, Vector2.down, groundCheckDisance, whatIsGround) || Physics2D.Raycast(transform.position, Vector2.down, groundCheckDisance, whatIsGround));
        Vector2 yRayOriginOne = (Vector2)transform.position + new Vector2(0, yRayOffsetUpOne);
        Vector2 yRayOriginTwo = (Vector2)transform.position + new Vector2(0, yRayOffsetUpTwo);
        Vector2 yRayOriginThree = (Vector2)transform.position + new Vector2(0, yRayOffsetDownOne);
        Vector2 yRayOriginFour = (Vector2)transform.position + new Vector2(0, yRayOffsetDownTwo);
        Vector2 yRayOriginFive = (Vector2)transform.position + new Vector2(0, -0.1f);
        Vector2 wallDirection = isFacingRight ? Vector2.right : Vector2.left;
        Vector2[] wallRayOrigins =
        {
            yRayOriginFive,
            yRayOriginOne,
            yRayOriginTwo,
            yRayOriginThree,
            yRayOriginFour
        };

        isOnWall = false;

        foreach (Vector2 origin in wallRayOrigins)
        {
            RaycastHit2D[] rayHits = Physics2D.RaycastAll(origin, wallDirection, wallCheckDistance);

            foreach (RaycastHit2D rayHit in rayHits)
            {
                if (rayHit.collider != null &&
                    rayHit.collider != col &&
                    rayHit.collider.CompareTag(whatIsWall))
                {
                    isOnWall = true;
                    break;
                }
            }

            if (isOnWall)
                break;
        }
        if (isOnWall && !(Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow)))
        {
            isOnWall = false;
        }

    }

    private void HandleInput()
    {
        yInput = Input.GetAxisRaw("Vertical");
        xInput = Input.GetAxisRaw("Horizontal");
    }

    private void HandleMovement()
    {
        if (wallJumpLockTimer == 0)
        {
            if (canMove)
            {
                rb.linearVelocity = new Vector2(direction * math.max(math.abs(rb.linearVelocity.x), math.abs(xInput * movementSpeed)), rb.linearVelocity.y);
            }
            if (isFacingRight != wasFacingRight)
            {
                Vector2 vel = rb.linearVelocity;
                wasFacingRight = isFacingRight;
                vel.x = Mathf.Lerp(vel.x, 0f, turnDamping * Time.fixedDeltaTime);
                rb.linearVelocity = vel;
            }
        }
    }

    private void HandleFlip()
    {
        bool leftPressed = Input.GetKey(KeyCode.LeftArrow);
        bool rightPressed = Input.GetKey(KeyCode.RightArrow);
        if (leftPressed && rightPressed && !isDashing)
        {
            direction = 0;
        }
        else if (leftPressed && !isDashing)
        {
            direction = -1;
        }
        else if (rightPressed && !isDashing)
        {
            direction = 1;
        }
        else if (!isDashing)
        {
            direction = 0;
        }

        if (direction < 0 && isFacingRight && !isDashing)
        {
            flip();
        }
        else if (direction > 0 && !isFacingRight && !isDashing)
        {
            flip();
        }
    }
    private void HandleJump()
    {
        if (isGrounded)
        {
            canJump = true;
            coyoteTimer = coyoteTime;
        }
        else if (!isGrounded && coyoteTimer > 0)
        {
            coyoteTimer -= Time.deltaTime;
        }
        if (Input.GetKeyDown(KeyCode.Z) && !isGrounded)
        {
            AirJumpAndWallJump();
        }
        if (Input.GetKeyDown(KeyCode.Z) && coyoteTimer > 0f)
        {
            isJumping = true;
            jumpSteps = 0;
            coyoteTimer = 0f;
        }

        if (Input.GetKeyUp(KeyCode.Z) && isJumping)
        {
            CancelJump();
        }
    }
    private void HandleJumping()
    {
        if (isJumping)
        {
            if (jumpSteps < maxJumpSteps)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, groundJumpForce);
                jumpSteps++;
            }
            else if (jumpSteps == maxJumpSteps)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y - groundJumpForce * 0.5f);
                jumpSteps++;
            }
            else
            {
                isJumping = false;
            }
        }

        if (rb.linearVelocity.y < -maxFallSpeed)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, -maxFallSpeed);
        }
    }

    private void HandleAnimations()
    {
        anim.SetFloat("direction", direction);
        anim.SetFloat("yVelocity", rb.linearVelocity.y);
        anim.SetBool("isGrounded", isGrounded);
        anim.SetBool("isDashing", isDashing);
        anim.SetBool("isRunning", isRunning);
        anim.SetBool("isWallSliding", isWallSliding);
    }

    private void StopDashAndRun()
    {
        isDashing = false;
        isRunning = false;
    }

    private void AirJumpAndWallJump()
    {
        if (canJump && !isDashing && !isOnWall)
        {
            CancelJump();
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, math.max(rb.linearVelocity.y, airJumpForce));
            canJump = false;
        }
        else if (!isDashing && isOnWall)
        {
            didWallJump = true;
            wallJumpLockTimer = 0.2f;
            float awayFromWall = isFacingRight ? -1f : 1f;
            flip();
            CancelJump();
            rb.linearVelocity = new Vector2(awayFromWall * xWallJumpForce, yWallJumpForce);
            canJump = true;
            canDash = true;
        }
    }


    private void flip()
    {
        transform.Rotate(0, 180, 0);
        isFacingRight = !isFacingRight;
    }


    private void CancelJump()
    {
        isJumping = false;
        if (rb.linearVelocity.y > 0f)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
        }
    }

    private bool TryStartMantle()
    {
        if (mantleLedgeCheckPoint == null || mantleHeadLevelCheckPoint == null)
            return false;
        Vector2 direction = isFacingRight ? Vector2.right : Vector2.left;

        if (Physics2D.OverlapCircle(
                mantleLedgeCheckPoint.position,
                mantleSearchRadius,
                mantleZoneLayer) == null)
        {
            mantleZoneUsed = false;
            return false;
        }

        Vector2 downRayOrigin =
            (Vector2)mantleLedgeCheckPoint.position
            + direction * mantleCheckDistance
            + Vector2.up * mantleRayStartHeight;

        RaycastHit2D surfaceHit = Physics2D.Raycast(
            downRayOrigin,
            Vector2.down,
            mantleRayLength + mantleRayStartHeight,
            whatIsGround);

        float colliderBottomOffset = col.bounds.min.y - transform.position.y;
        Vector2 landingPoint = surfaceHit.collider != null
            ? surfaceHit.point
            : (Vector2)mantleLedgeCheckPoint.position + direction * mantleCheckDistance;
        if (landingPoint.y > mantleHeadLevelCheckPoint.position.y)
            return false;

        mantleTarget = new Vector2(
            landingPoint.x,
            Mathf.Max(
                landingPoint.y - colliderBottomOffset,
                mantleLedgeCheckPoint.position.y - colliderBottomOffset));

        mantleStart = rb.position;
        BeginMantlePhase(
            MantlePhase.Rising,
            mantleStart,
            new Vector2(mantleStart.x, mantleStart.y + mantleMaxRise));
        mantleOriginalGravity = rb.gravityScale;
        mantleOriginalInterpolation = rb.interpolation;
        mantleJumpCancelRequested = false;

        rb.gravityScale = 0f;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.linearVelocity = Vector2.zero;
        isMantling = true;
        return true;
    }

    private void AdvanceMantle()
    {
        if (mantleJumpCancelRequested)
        {
            mantleJumpCancelRequested = false;
            FinishMantle();
            rb.linearVelocity = new Vector2(0f, mantleJumpForce);
            isJumping = false;
            jumpSteps = 0;
            return;
        }

        switch (mantlePhase)
        {
            case MantlePhase.Rising:
            {
                float t = AdvanceMantlePhaseTime();
                float nextY = Mathf.Lerp(
                    mantlePhaseStart.y,
                    mantlePhaseTarget.y,
                    EvaluateMantleCurve(t));
                bool pathClear = IsMantleForwardPathClear(nextY);

                if (!pathClear || nextY < mantleTarget.y)
                {
                    rb.MovePosition(new Vector2(rb.position.x, nextY));
                    if (t >= 1f)
                    {
                        FinishMantle();
                        return;
                    }
                    return;
                }

                rb.MovePosition(new Vector2(rb.position.x, nextY));
                BeginMantlePhase(
                    MantlePhase.MovingForward,
                    new Vector2(rb.position.x, nextY),
                    new Vector2(mantleTarget.x, nextY));
                return;
            }
            case MantlePhase.MovingForward:
            {
                float t = AdvanceMantlePhaseTime();
                float nextX = Mathf.Lerp(
                    mantlePhaseStart.x,
                    mantlePhaseTarget.x,
                    EvaluateMantleCurve(t));
                rb.MovePosition(new Vector2(nextX, rb.position.y));

                if (t >= 1f)
                {
                    BeginMantlePhase(
                        MantlePhase.Descending,
                        new Vector2(nextX, rb.position.y),
                        mantleTarget);
                }
                return;
            }
            case MantlePhase.Descending:
            {
                float t = AdvanceMantlePhaseTime();
                float nextY = Mathf.Lerp(
                    mantlePhaseStart.y,
                    mantlePhaseTarget.y,
                    EvaluateMantleCurve(t));
                rb.MovePosition(new Vector2(rb.position.x, nextY));

                if (t >= 1f)
                {
                    rb.MovePosition(mantleTarget);
                    FinishMantle();
                }
                return;
            }
        }
    }

    private void BeginMantlePhase(MantlePhase phase, Vector2 start, Vector2 target)
    {
        mantlePhase = phase;
        mantlePhaseStart = start;
        mantlePhaseTarget = target;
        mantlePhaseElapsed = 0f;
        mantlePhaseDuration = Mathf.Max(
            Vector2.Distance(start, target) / Mathf.Max(mantleMoveSpeed, 0.01f),
            Time.fixedDeltaTime);
    }

    private float AdvanceMantlePhaseTime()
    {
        mantlePhaseElapsed += Time.fixedDeltaTime;
        return Mathf.Clamp01(mantlePhaseElapsed / mantlePhaseDuration);
    }

    private float EvaluateMantleCurve(float t)
    {
        return mantleMotionCurve == null
            ? t
            : Mathf.Clamp01(mantleMotionCurve.Evaluate(t));
    }

    private bool IsMantleForwardPathClear(float atHeight)
    {
        float distance = Mathf.Abs(mantleTarget.x - rb.position.x);
        if (distance <= 0.01f)
            return true;

        Vector2 direction = mantleTarget.x > rb.position.x
            ? Vector2.right
            : Vector2.left;
        float verticalOffset = atHeight - rb.position.y;
        Bounds bounds = col.bounds;
        float originX = direction.x > 0f
            ? bounds.max.x + 0.01f
            : bounds.min.x - 0.01f;
        float castDistance = distance + 0.02f;
        int blockingLayers = mantleObstacleLayers | whatIsGround;

        for (int i = 0; i < 3; i++)
        {
            float y = Mathf.Lerp(bounds.min.y + 0.03f, bounds.max.y - 0.03f, i * 0.5f)
                + verticalOffset;
            if (Physics2D.Raycast(
                    new Vector2(originX, y),
                    direction,
                    castDistance,
                    blockingLayers))
            {
                return false;
            }
        }

        return true;
    }

    private void FinishMantle()
    {
        isMantling = false;
        rb.gravityScale = mantleOriginalGravity;
        rb.interpolation = mantleOriginalInterpolation;
        rb.linearVelocity = Vector2.zero;
    }

    private void OnDrawGizmos()
    {
        if (mantleHeadLevelCheckPoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(mantleHeadLevelCheckPoint.position, 0.05f);
        }

        Vector2 rayOriginOne = (Vector2)transform.position + new Vector2(xRayOffsetLeft, 0);
        Vector2 rayOriginTwo = (Vector2)transform.position + new Vector2(xRayOffsetRight, 0);
        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position, transform.position + new Vector3(0, -groundCheckDisance));
        Gizmos.DrawLine(rayOriginOne, transform.position + new Vector3(xRayOffsetLeft, -groundCheckDisance));
        Gizmos.DrawLine(rayOriginTwo, transform.position + new Vector3(xRayOffsetRight, -groundCheckDisance));

        Gizmos.color = Color.red;
        Vector2 wallRayOrigin = transform.position;
        Vector2 wallRayOriginUpOne = wallRayOrigin + new Vector2(0, yRayOffsetUpOne);
        Vector2 wallRayOriginUpTwo = wallRayOrigin + new Vector2(0, yRayOffsetUpTwo);
        Vector2 wallRayOriginDownOne = wallRayOrigin + new Vector2(0, yRayOffsetDownOne);
        Vector2 wallRayOriginDownTwo = wallRayOrigin + new Vector2(0, yRayOffsetDownTwo);
        Vector2 wallRayOriginFive = wallRayOrigin + new Vector2(0, -0.1f);
        Vector2 wallRayEndOffset = (isFacingRight ? Vector2.right : Vector2.left) * wallCheckDistance;
        Gizmos.DrawLine(wallRayOriginFive, wallRayOriginFive + wallRayEndOffset);
        Gizmos.DrawLine(wallRayOriginUpOne, wallRayOriginUpOne + wallRayEndOffset);
        Gizmos.DrawLine(wallRayOriginUpTwo, wallRayOriginUpTwo + wallRayEndOffset);
        Gizmos.DrawLine(wallRayOriginDownOne, wallRayOriginDownOne + wallRayEndOffset);
        Gizmos.DrawLine(wallRayOriginDownTwo, wallRayOriginDownTwo + wallRayEndOffset);
        Vector2 mantleDirection = isFacingRight ? Vector2.right : Vector2.left;

        if (mantleWallCheckPoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(mantleWallCheckPoint.position, mantleSearchRadius);

            // This check is currently a raycast, so draw the ray too.
            Gizmos.DrawLine(
                mantleWallCheckPoint.position,
                mantleWallCheckPoint.position + (Vector3)(mantleDirection * mantleCheckDistance));
        }

        if (mantleLedgeCheckPoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(mantleLedgeCheckPoint.position, mantleSearchRadius);

            Gizmos.DrawLine(
                mantleLedgeCheckPoint.position,
                mantleLedgeCheckPoint.position + (Vector3)(mantleDirection * mantleCheckDistance));

            Vector2 downRayOrigin =
                (Vector2)mantleLedgeCheckPoint.position
                + mantleDirection * mantleCheckDistance
                + Vector2.up * mantleRayStartHeight;

            Gizmos.DrawLine(
                downRayOrigin,
                downRayOrigin + Vector2.down * (mantleRayLength + mantleRayStartHeight));
        }
    }
}