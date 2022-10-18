﻿using System;
using System.Collections;
using UnityEngine;
using Cinemachine;

public class Player : MonoBehaviour
{
    #region Serialized Components
    [SerializeField] Camera playerCamera;
    [SerializeField] Transform playerCameraTarget;
    [SerializeField] CinemachineFreeLook playerCineCamera;
    [SerializeField] GameObject visualModel;
    [SerializeField] PlayerParameters playPrm;
    [SerializeField] LayerMask gndCollisionFlags;
    #endregion

    #region Public Variables
    public Vector3 Velocity { get { return _playController.velocity; } }
    public bool IsOnGround;
    public bool IsJumping;
    #endregion

    #region Private Variables
    bool _canJump, _isOnSlope, _isOnSlopeSteep;
    float _dt;
    float coyoteTimer, requestMoveTimer;
    Rigidbody _playController;
    CapsuleCollider _playCollider;
    Vector3 _input;
    Vector3 _gndNormal;
    RaycastHit _gndHit;
    float currentFloorFriction;
    Quaternion _onJmpRotation;
    #endregion


    #region Built-Ins
    void Start()
    {
        _canJump = true;
        _playController = GetComponent<Rigidbody>();
        _playCollider = GetComponent<CapsuleCollider>();
    }

    void Update()
    {
        PollInput();
        TickTimers();
        DoPlayerMovement();
    }
    #endregion

    #region Collision
    void GetGrounded()
    {
        if (IsJumping) {
            IsOnGround = false;
            return;
        }
        Vector3 direction;
        if (_isOnSlopeSteep)
            direction = Vector3.zero;
        else
            direction = (Vector3.Scale(playerCamera.transform.TransformDirection(_input), new Vector3(1, 0, 1)) * _dt * playPrm.Move_RunSpeed);
        Vector3 p1 = transform.position + _playCollider.center + Vector3.down * (_playCollider.height * 0.5f - _playCollider.radius - 0.05f) + direction;
        Vector3 p2 = p1 + Vector3.up * (_playCollider.height - _playCollider.radius * 2f - 0.05f);
        
        IsOnGround = Physics.CapsuleCast(p1, p2,
            _playCollider.radius * 0.99f, 
            Vector3.down + direction, out _gndHit, 
            playPrm.Gnd_RayLength + 0.05f, 
            gndCollisionFlags);
    }
    void GetSlopeNormal()
    {
        if (IsJumping || !IsOnGround)
        {
            _gndNormal = Vector3.up;
            _isOnSlope = false;
            _isOnSlopeSteep = false;
            return;
        }
        _gndNormal = _gndHit.normal;
        float ang = Vector3.Angle(Vector3.up, _gndNormal);
        _isOnSlope = ang <= playPrm.Gnd_SlopeLimit && ang != 0f;
        _isOnSlopeSteep = ang > playPrm.Gnd_SlopeLimit;
    }
    #endregion

    #region Methods
    void TickTimers()
    {
        _dt = Time.deltaTime;
        if (coyoteTimer > 0)
            coyoteTimer = Mathf.Max(coyoteTimer - _dt, 0);

        if (_input.magnitude > 0.05f && Math.Abs(playerCineCamera.m_XAxis.m_InputAxisValue) <= 0.05f && !UnityEngine.Input.GetButton("Attack"))
            requestMoveTimer += _dt;
        else
            requestMoveTimer = 0f;

    }

    void PollInput()
    {
        _input = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
    }

    void DoPlayerMovement()
    {
        GetGrounded();
        GetSlopeNormal();

        float speed = IsOnGround ? playPrm.Move_RunSpeed : playPrm.Move_AirSpeed;
        float accel = IsOnGround ? playPrm.Move_RunAccel : playPrm.Move_AirAccel;
        float friction = IsOnGround ? playPrm.Move_RunFriction : playPrm.Move_AirFriction;

        if (IsOnGround)
        {
            if (_onJmpRotation != Quaternion.identity)
            {
                visualModel.transform.rotation = Quaternion.Euler(0, _onJmpRotation.eulerAngles.y, 0);
                _onJmpRotation = Quaternion.identity;
            }
            
            if (IsJumping)
                IsJumping = false;
        }
        else if (_onJmpRotation == Quaternion.identity)
        {
            _onJmpRotation = Quaternion.Euler(0, visualModel.transform.rotation.eulerAngles.y, 0);
        }

        if (Input.GetKeyDown(KeyCode.Space) && (IsOnGround || coyoteTimer > 0) && !IsJumping)
        {
            DoJump();
        }
        if (Input.GetKeyUp(KeyCode.Space) && Velocity.y > playPrm.Jump_Shortening && IsJumping && !IsOnGround)
        {
            DoJumpShortening();
        }
        
        bool attacking = UnityEngine.Input.GetButton("Attack");
        if (_input.magnitude > 0f)
        {
            if (!attacking)
                DoCameraAutoTurn();
            if (!_isOnSlopeSteep)
                DoMove(speed, accel);
        }
        DoPlayerModelRotation(speed, attacking);
        if (_isOnSlopeSteep)
        {
            //slide away from the slope
            DoSlopeSliding(accel);
        }

        if (Velocity.y < 0f)
        {
            if (IsJumping)
                IsJumping = false;
        }
        if (!IsOnGround)
        {
            if (Velocity.y < -playPrm.Move_TerminalGravity)
                _playController.velocity = new Vector3(Velocity.x, -playPrm.Move_TerminalGravity, Velocity.z);
        }
    }

    void DoCameraAutoTurn()
    {
        if (requestMoveTimer > playPrm.Camera_AutoTurnTime)
        {
            float intensity = Mathf.Clamp01(requestMoveTimer - playPrm.Camera_AutoTurnTime);
            playerCineCamera.m_XAxis.Value += _input.x * intensity * playPrm.Camera_AutoTurnSpeed * _dt;
            // playerCameraTarget.Rotate(0, _input.x * playPrm.Camera_AutoTurnSpeed * _dt * intensity, 0);
        }
    }

    void DoSlopeSliding(float accel)
    {
        Vector3 slopeDown = Vector3.up - _gndNormal * Vector3.Dot(Vector3.up, _gndNormal);
        Vector3 fInp = Vector3.Scale(playerCamera.transform.TransformDirection(_input), new Vector3(1, 0, 1));
        fInp = Vector3.ProjectOnPlane(fInp.normalized, _gndNormal);

        _playController.AddForce(slopeDown * -playPrm.Move_SlideSpeed + fInp * accel, ForceMode.Acceleration);
    }

    void DoPlayerModelRotation(float speed, bool faceCam = false)
    {
        if (faceCam)
        {
            Quaternion targetRot = Quaternion.Euler(0, playerCamera.transform.rotation.eulerAngles.y, 0);
            if (IsOnGround)
            {
                visualModel.transform.rotation = Quaternion.Slerp(visualModel.transform.rotation, targetRot, 90f * _dt);
            }
            else
            {
                _onJmpRotation = targetRot;
                targetRot = TiltRotationTowardsVelocity(_onJmpRotation, Vector3.up, Velocity, speed * 10f);
                visualModel.transform.rotation = Quaternion.Slerp(visualModel.transform.rotation, targetRot, 90f * _dt);
            }
        }
        else
        {
            if (_input.magnitude <= 0f) return;
            Vector3 fInp = Vector3.Scale(playerCamera.transform.TransformDirection(_input), new Vector3(1, 0, 1));
            if (IsOnGround)
            {
                // ease to new direction
                visualModel.transform.forward = Vector3.Slerp(visualModel.transform.forward, fInp, 15f * _dt);
            }
            else
            {
                Quaternion targetRot = TiltRotationTowardsVelocity(_onJmpRotation, Vector3.up, Velocity, speed * 10f);
                visualModel.transform.rotation = Quaternion.Slerp(visualModel.transform.rotation, targetRot, 8f * _dt);
            }
        }
    }

    void DoMove(float speed, float accel)
    {
        Vector3 moveDir = playerCamera.transform.TransformDirection(_input);
        moveDir.y = 0f;

        if (_isOnSlope && !IsJumping)
        {
            moveDir = Vector3.ProjectOnPlane(moveDir, _gndNormal);
        }
        _playController.AddForce(moveDir.normalized * accel, ForceMode.Force);

        DoSpeedCap(speed);
    }

    void DoSpeedCap(float speed) {
        if (_isOnSlope && !IsJumping)
        {
            if (Velocity.magnitude > speed)
                _playController.velocity = Velocity.normalized * speed;
        }
        else
        {
            Vector3 fVel = new Vector3(Velocity.x, 0, Velocity.z);
            if (fVel.magnitude > speed)
            {
                fVel = fVel.normalized * speed;
                _playController.velocity = new Vector3(fVel.x, Velocity.y, fVel.z);
            }
        }
    }
    void DoJump()
    {
        IsJumping = true;
        coyoteTimer = 0f;
        _playController.velocity = new Vector3(Velocity.x, 0, Velocity.z);
        _playController.AddForce(_gndNormal * playPrm.Jump_Velocity, ForceMode.Impulse);
        IsOnGround = false;
        _onJmpRotation = Quaternion.Euler(0, visualModel.transform.rotation.eulerAngles.y, 0);
        GetSlopeNormal();
    }

    void DoJumpShortening()
    {
        IsJumping = false;
        coyoteTimer = 0f;
        _playController.velocity = new Vector3(Velocity.x, playPrm.Jump_Shortening, Velocity.z);
    }
    #endregion

    #region Helpers
        // http://answers.unity.com/answers/1498260/view.html
        public static Quaternion TiltRotationTowardsVelocity( Quaternion cleanRotation, Vector3 referenceUp, Vector3 vel, float velMagFor45Degree )
        {
            Vector3 rotAxis = Vector3.Cross( referenceUp, vel );
            float tiltAngle = Mathf.Atan( vel.magnitude / velMagFor45Degree) * Mathf.Rad2Deg;
            return Quaternion.AngleAxis( tiltAngle, rotAxis ) * cleanRotation;    //order matters
        }
    #endregion

    #region Parameter Classes
    [Serializable] public class PlayerParameters
    {
        [Header("Movement")]
        public float Move_RunSpeed = 5.0f;
        public float Move_RunAccel = 100.0f;
        public float Move_RunFriction = 1f;
        public float Move_AirSpeed = 5.0f;
        public float Move_AirAccel = 100.0f;
        public float Move_AirFriction = 0f;
        public float Move_SlideSpeed = 1f;
        public float Move_SlideFriction = 0.3f;
        public float Move_SlopeSnap = 80f;
        public float Move_Gravity = 9.81f;
        public float Move_TerminalGravity = 20f;

        [Header("Jumping")]
        public float Jump_Velocity = 5.0f;
        public float Jump_CoyoteTime = 0.2f;
        public float Jump_Shortening = 1f;
        

        [Header("Misc")]
        public float Camera_AutoTurnTime = 2f;
        public float Camera_AutoTurnSpeed = 45f;
        public float Gnd_RayLength = 0.2f;
        public float Gnd_SlopeLimit = 40f;
    }
    #endregion
}
