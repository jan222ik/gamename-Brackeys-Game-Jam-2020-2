﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

// ReSharper disable once CheckNamespace
public class CharacterController2D : MonoBehaviour, IPusher
{
    private const float InitialJumpForce = 50f;

    [Header("Dependencies")] [SerializeField]
    private Transform m_GroundCheck; // A position marking where to check if the player is grounded.

    [SerializeField] private Transform m_CeilingCheck; // A position marking where to check for ceilings
    [SerializeField] private Transform m_StuckInEnvironment;

    [Header("General")] [Space] [Range(0, .3f)] [SerializeField]
    private float m_MovementSmoothing = .05f; // How much to smooth out the movement

    [SerializeField] private bool m_AirControl; // Whether or not a player can steer while jumping;

    private GroundProvider m_WhatIsGroundProvider; // A mask determining what is ground to the character

    const float k_GroundedRadius = .2f; // Radius of the overlap circle to determine if grounded
    private bool m_Grounded; // Whether or not the player is grounded.
    const float k_CeilingRadius = .2f; // Radius of the overlap circle to determine if the player can stand up

    private Rigidbody2D m_Rigidbody2D;
    private bool m_FacingRight = true; // For determining which way the player is currently facing.
    private Vector3 m_Velocity = Vector3.zero;
    private Transform m_transform;


    private bool hasCrouchDisableCollider;

    [Header("Move: Jump")] [Space] [SerializeField]
    private float m_JumpForce = InitialJumpForce; // Amount of force added when the player jumps.

    [Header("Move: Double Jump")] [Space] [SerializeField]
    private bool doubleJumpEnabled = true;

    [SerializeField] private float m_doubleJumpForce = InitialJumpForce;
    private bool usedDoubleJump;

    [Header("Move: Crouch")] [Space] [Range(0, 1)] [SerializeField]
    private float m_CrouchSpeed = .36f; // Amount of maxSpeed applied to crouching movement. 1 = 100%

    [SerializeField] private Collider2D m_CrouchDisableCollider; // A collider that will be disabled when crouching

    [Header("Move: Slam")] [Space] [SerializeField]
    private bool slamEnabled = true;

    [SerializeField] private float m_slamForce = -2 * InitialJumpForce;

    [Header("Move: Position Rewind")] [Space] [SerializeField]
    private bool positionRewindEnabled = true;

    [SerializeField] private GameObject startPositionPrefab;
    private GameObject instantiatedStartPositionObj;

    [SerializeField] private float maxRecordSeconds;

    private Stack<RewindPoint> playerRewindPositions;
    private bool isRewinding;
    private bool isRecordingRewind;


    [Header("Move: Push")] [Space] [Range(0, 100f)] [SerializeField]
    private float pushForce = 20f;

    [Header("Events")] [Space] public UnityEvent OnLandEvent;

    public UnityEvent OnJumpEvent;

    [Serializable]
    public class BoolEvent : UnityEvent<bool>
    {
    }

    public BoolEvent OnCrouchEvent;
    private bool m_wasCrouching;

    private void Awake()
    {
        m_Rigidbody2D = GetComponent<Rigidbody2D>();
        m_transform = transform;
        hasCrouchDisableCollider = m_CrouchDisableCollider != null;

        playerRewindPositions = new Stack<RewindPoint>();

        if (OnLandEvent == null)
            OnLandEvent = new UnityEvent();

        if (OnCrouchEvent == null)
            OnCrouchEvent = new BoolEvent();

        if (OnJumpEvent == null)
        {
            OnJumpEvent = new UnityEvent();
        }

        OnLandEvent.AddListener(() => { usedDoubleJump = false; });
    }

    private void Start()
    {
        m_WhatIsGroundProvider = GameController.Instance;
    }

    private void FixedUpdate()
    {
        if (positionRewindEnabled && isRewinding)
        {
            if (playerRewindPositions.Count < 1)
            {
                stopRewind();
                return;
            }

            var lastPoint = playerRewindPositions.Pop();
            m_transform.position = lastPoint.position;
            m_transform.rotation = lastPoint.rotation;
            m_Rigidbody2D = lastPoint.rigidbody2D;
            if (playerRewindPositions.Count < 1)
            {
                stopRewind();
            }
        }
        else
        {
            bool wasGrounded = m_Grounded;
            m_Grounded = false;

            // The player is grounded if a circlecast to the groundcheck position hits anything designated as ground
            // This can be done using layers instead but Sample Assets will not overwrite your project settings.
            var colliders = Physics2D.OverlapCircleAll(m_GroundCheck.position, k_GroundedRadius,
                m_WhatIsGroundProvider.getGroundLayer());
            foreach (var col in colliders)
            {
                if (!col.CompareTag(GameTags.PLAYER))
                {
                    m_Grounded = true;
                    if (!wasGrounded)
                        OnLandEvent.Invoke();
                }
            }

            if (!m_Grounded)
            {
                colliders = Physics2D.OverlapCircleAll(m_GroundCheck.position, k_GroundedRadius,
                    LayerMask.GetMask("BothLayers"));
                foreach (var col in colliders)
                {
                    if (!col.CompareTag(GameTags.PLAYER))
                    {
                        m_Grounded = true;
                        if (!wasGrounded)
                            OnLandEvent.Invoke();
                    }
                }
            }

            colliders = Physics2D.OverlapPointAll(m_StuckInEnvironment.position);
            colliders.ToList().ForEach(c =>
            {
                if (!c.gameObject.CompareTag("Player") && !c.isTrigger)
                {
                    GetComponent<PlayerHealthController>().applyDamage(50 * Time.fixedDeltaTime);
                }
            });
            if (isRecordingRewind)
            {
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (Time.timeScale == 0) return;

                if (playerRewindPositions.Count < Mathf.RoundToInt(maxRecordSeconds / Time.fixedDeltaTime))
                {
                    playerRewindPositions.Push(new RewindPoint(m_transform, m_Rigidbody2D));
                }
                else
                {
                    timeExceededRewind();
                }
            }
        }
    }

    public void invokeRewind()
    {
        if (isRecordingRewind)
        {
            startRewind();
        }
        else
        {
            playerRewindPositions.Push(new RewindPoint(m_transform, m_Rigidbody2D));
            instantiatedStartPositionObj = Instantiate(startPositionPrefab, m_transform.position, Quaternion.identity);
            isRecordingRewind = true;
        }
    }

    public void startRewind()
    {
        isRewinding = true;
        m_Rigidbody2D.isKinematic = true;
    }

    public void stopRewind()
    {
        Destroy(instantiatedStartPositionObj);
        playerRewindPositions.Clear();
        isRewinding = false;
        isRecordingRewind = false;
        m_Rigidbody2D.isKinematic = false;
    }

    private void timeExceededRewind()
    {
        // TODO Play sound or sth
        stopRewind();
    }


    public void Move(float move, bool crouch, bool jump)
    {
        if (slamEnabled) checkSlamGroundMove(crouch);

        // If crouching, check to see if the character can stand up
        if (!crouch)
        {
            // If the character has a ceiling preventing them from standing up, keep them crouching
            if (Physics2D.OverlapCircle(m_CeilingCheck.position, k_CeilingRadius,
                m_WhatIsGroundProvider.getGroundLayer()))
            {
                crouch = true;
            }
        }


        //only control the player if grounded or airControl is turned on
        if (m_Grounded || m_AirControl)
        {
            // If crouching
            if (crouch)
            {
                if (!m_wasCrouching)
                {
                    m_wasCrouching = true;
                    OnCrouchEvent.Invoke(true);
                }

                // Reduce the speed by the crouchSpeed multiplier
                move *= m_CrouchSpeed;

                // Disable one of the colliders when crouching
                if (hasCrouchDisableCollider)
                    m_CrouchDisableCollider.enabled = false;

                applyPush(pushForce);
            }
            else
            {
                // Enable the collider when not crouching
                if (hasCrouchDisableCollider)
                    m_CrouchDisableCollider.enabled = true;

                if (m_wasCrouching)
                {
                    m_wasCrouching = false;
                    OnCrouchEvent.Invoke(false);
                }
            }

            // Move the character by finding the target velocity
            var rbVelocity = m_Rigidbody2D.velocity;
            Vector3 targetVelocity = new Vector2(move * 10f, rbVelocity.y);
            // And then smoothing it out and applying it to the character
            m_Rigidbody2D.velocity = Vector3.SmoothDamp(rbVelocity, targetVelocity, ref m_Velocity,
                m_MovementSmoothing);

            // If the input is moving the player right and the player is facing left...
            if (move > 0 && !m_FacingRight)
            {
                // ... flip the player.
                Flip();
            }
            // Otherwise if the input is moving the player left and the player is facing right...
            else if (move < 0 && m_FacingRight)
            {
                // ... flip the player.
                Flip();
            }
        }

        // Double Jump
        if (doubleJumpEnabled) checkDoubleJumpMove(jump);

        // Jump
        checkJumpMove(jump);
    }

    private void checkSlamGroundMove(bool crouch)
    {
        if (crouch && !m_Grounded && !m_wasCrouching)
        {
            m_Rigidbody2D.AddForce(new Vector2(0, m_slamForce));
        }
    }

    private const float JUMP_THRESHOLD = 5f;
    private void checkDoubleJumpMove(bool jump)
    {
        if (!m_Grounded && jump && !usedDoubleJump && m_Rigidbody2D.velocity.y <= JUMP_THRESHOLD)
        {
            var resetDownForce = (m_Rigidbody2D.velocity.y < 0) ? -1 * m_Rigidbody2D.velocity.y : 0;
            m_Rigidbody2D.AddForce(new Vector2(0f, m_doubleJumpForce + resetDownForce));
            usedDoubleJump = true;
        }
    }

    private void checkJumpMove(bool jump)
    {
        if (m_Grounded && jump)
        {
            // Add a vertical force to the player.
            m_Grounded = false;
            m_Rigidbody2D.AddForce(new Vector2(0f, m_JumpForce));
            OnJumpEvent.Invoke();
        }
    }


    private void Flip()
    {
        // Switch the way the player is labelled as facing.
        m_FacingRight = !m_FacingRight;

        // Multiply the player's x local scale by -1.
        Vector3 theScale = m_transform.localScale;
        theScale.x *= -1;
        m_transform.localScale = theScale;
    }

    private Pushable activePushable = null;

    public void applyPush(float force)
    {
        if (activePushable != null)
        {
            var heading = activePushable.transform.position - transform.position;
            if (Mathf.Abs(heading.x) > Mathf.Abs(heading.y))
            {
                // Push X
                heading = (heading.x >= 0) ? Vector2.right : Vector2.left;
            }
            else
            {
                // Push y
                heading = (heading.y >= 0) ? Vector2.up : Vector2.down;
            }

            if (activePushable.usesRB)
            {
                activePushable.gameObject.GetComponent<Rigidbody2D>().AddForce(heading.normalized * (force * 200f));
            }
            else
            {
                activePushable.transform.position += heading.normalized * (0.01F * force);
            }
        }
    }

    public void pushStarted(float collectiveWeight, Pushable target)
    {
        activePushable = target;
    }

    public void pushStopped(Pushable target)
    {
        activePushable = null;
    }

    public void enableMove(MoveTypes typeToAward)
    {
        switch (typeToAward)
        {
            case MoveTypes.SLAM:
                slamEnabled = true;
                break;
            case MoveTypes.DOUBLE_JUMP:
                doubleJumpEnabled = true;
                break;
            case MoveTypes.LAYER_SWITCH:
                GameController.playerLayerSwitchEnabled = true;
                break;
            case MoveTypes.POS_REWIND:
                positionRewindEnabled = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(typeToAward), typeToAward, null);
        }
    }
}

public class RewindPoint
{
    public Vector2 position;
    public Quaternion rotation;
    public Rigidbody2D rigidbody2D;

    public RewindPoint(Vector2 position, Quaternion rotation, Rigidbody2D rigidbody2D)
    {
        this.position = position;
        this.rotation = rotation;
        this.rigidbody2D = rigidbody2D;
    }

    public RewindPoint(Transform transform, Rigidbody2D rigidbody2D) : this(transform.position, transform.rotation,
        rigidbody2D)
    {
        /* Ignored */
    }
}

public enum MoveTypes
{
    SLAM, DOUBLE_JUMP, LAYER_SWITCH, POS_REWIND
}