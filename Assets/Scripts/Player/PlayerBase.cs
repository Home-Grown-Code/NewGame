using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using JetBrains.Annotations;
using NUnit.Framework;
using Unity.Collections.Tests.CoreCLR.TestJobs;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEditor.Callbacks;
using UnityEditor.Experimental.GraphView;
using UnityEditor.Tilemaps;
using UnityEditor.U2D.Aseprite;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SearchService;

public class PlayerBase : MonoBehaviour
{
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
    [SerializeField] private float stickDuration;
    [SerializeField] private float wallSlideSpeed;
    [SerializeField] private float stickTimer;


    [Header("Collision details")]
    [SerializeField] private float groundCheckDisance;
    [SerializeField] private LayerMask whatIsGround;
    [SerializeField] private float xRayOffsetLeft = -0.14f;
    [SerializeField] private float xRayOffsetRight = 0.14f;
    [SerializeField] private float wallCheckDistance = 0.151f;
    [SerializeField] private string whatIsWall = "Wall";
    [SerializeField] private float yRayOffsetUpOne = 0.36f;
    [SerializeField] private float yRayOffsetUpTwo = 0.1f;
    [SerializeField] private float yRayOffsetDownOne = -0.425f;
    [SerializeField] private float yRayOffsetDownTwo = -0.6f;

    [Header("Clip Stoping")]
    [SerializeField] private LayerMask solidLayers;
    private readonly RaycastHit2D[] movementHits = new RaycastHit2D[8];
    private ContactFilter2D movementFilter;

    //States
    [Header("States")]
    [SerializeField] private bool isGrounded;
    [SerializeField] private bool isJumping;
    [SerializeField] private bool isFacingRight = true;
    [SerializeField] private bool isDashing;
    [SerializeField] private bool isRunning;
    [SerializeField] private bool isOnWall;
    [SerializeField] private bool isWallSliding;

    // Others
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

    // Permenent Commands
    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponentInChildren<Animator>();
        col = GetComponent<Collider2D>();
        sr = GetComponentInChildren<SpriteRenderer>();

        movementFilter = new ContactFilter2D();
        movementFilter.SetLayerMask(solidLayers);
        movementFilter.useTriggers = false;
    }
    void Update()
    {
        HandleDash();
        HandleRunning();
        HandleJump();
        HandleAnimations();
        HandleInput();
    }


    void FixedUpdate()
    {
        // Veriables
        velocityX = rb.linearVelocity.x;
        velocityY = rb.linearVelocity.y;

        // Commands
        HandleCollision();
        HandleFlip();
        HandleMovement();
        HandleRunning();
        HandleDashing();
        HandleJumping();
        HandleWallSlide();
        // PreventClipping();
    }

    // Spesific Commands
    private void HandleWallSlide()
    {
        if (isOnWall && !isGrounded)
        {
            if (!isWallSliding)
            {
                isWallSliding = true;
                stickTimer = stickDuration;
                rb.linearVelocity =  new Vector2 (0f, 0f);
            }

            if (stickTimer > 0)
            {
                stickTimer -= Time.fixedDeltaTime;
                rb.linearVelocity = new (0f, 0f);
            }
            else
            {
                float clapedY = MathF.Max(rb.linearVelocity.y, -wallSlideSpeed);
                rb.linearVelocity = new Vector2 (0f, clapedY);
            }
        }
        else
        {
            isWallSliding = false;
        }
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
        if (Input.GetKeyUp(KeyCode.C))
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
        if (canMove)
        {
            rb.linearVelocity = new Vector2(direction * math.max(math.abs(0), math.abs(xInput * movementSpeed)), rb.linearVelocity.y);
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
        else if(!isGrounded && coyoteTimer > 0)
        {
            coyoteTimer -= Time.deltaTime;
        }
        if (Input.GetKeyDown(KeyCode.Z) && !isGrounded)
        {
            AirJump();
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

    private void AirJump()
    {
        if (canJump)
        {
            CancelJump();
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, math.max(rb.linearVelocity.y, airJumpForce));
            canJump = false;
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
    private void PreventClipping()
    {
        Vector2 velocity = rb.linearVelocity;
        float distance = velocity.magnitude * Time.fixedDeltaTime;

        if (distance <= 0f)
            return;

        int count = col.Cast(
            velocity.normalized, movementFilter, movementHits, distance);

        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = movementHits[i];

            if (hit.collider != null &&
                Vector2.Dot(velocity, hit.normal) < 0f)
            {
                rb.linearVelocity = velocity - Vector2.Dot(velocity, hit.normal) * hit.normal;
                break;
            }
        }
    }

    private void OnDrawGizmos()
    {
        Vector2 rayOriginOne = (Vector2)transform.position + new Vector2(xRayOffsetLeft, 0);
        Vector2 rayOriginTwo = (Vector2)transform.position + new Vector2(xRayOffsetRight, 0);
        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position, transform.position + new Vector3(0, -groundCheckDisance));
        Gizmos.DrawLine(rayOriginOne, transform.position + new Vector3(xRayOffsetLeft, -groundCheckDisance));
        Gizmos.DrawLine(rayOriginTwo, transform.position + new Vector3(xRayOffsetRight, -groundCheckDisance));

        Gizmos.color = Color.darkRed;
        Vector2 wallRayOrigin = transform.position;
        Vector2 wallRayOriginUpOne = wallRayOrigin + new Vector2(0, yRayOffsetUpOne);
        Vector2 wallRayOriginUpTwo = wallRayOrigin + new Vector2(0, yRayOffsetUpTwo);
        Vector2 wallRayOriginDownOne = wallRayOrigin + new Vector2(0, yRayOffsetDownOne);
        Vector2 wallRayOriginDownTwo = wallRayOrigin + new Vector2(0, yRayOffsetDownTwo);
        Vector2 wallRayOriginFive = wallRayOrigin + new Vector2(0, -0.15f);
        Vector2 wallRayEndOffset = (isFacingRight ? Vector2.right : Vector2.left) * wallCheckDistance;
        Gizmos.DrawLine(wallRayOriginFive, wallRayOriginFive + wallRayEndOffset);
        Gizmos.DrawLine(wallRayOriginUpOne, wallRayOriginUpOne + wallRayEndOffset);
        Gizmos.DrawLine(wallRayOriginUpTwo, wallRayOriginUpTwo + wallRayEndOffset);
        Gizmos.DrawLine(wallRayOriginDownOne, wallRayOriginDownOne + wallRayEndOffset);
        Gizmos.DrawLine(wallRayOriginDownTwo, wallRayOriginDownTwo + wallRayEndOffset);
    }
}