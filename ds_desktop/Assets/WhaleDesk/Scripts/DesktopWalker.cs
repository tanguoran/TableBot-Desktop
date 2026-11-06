using System;
using UnityEngine;
using UnityEngine.AI;

namespace WhaleDesk
{
    public enum WalkState { Idle, Moving, Turning, Arrived, Failed }

    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class DesktopWalker : MonoBehaviour
    {
        public DesktopNavigation navigation;
        public Transform visual;
        public Animator animator;
        [Tooltip("World-space walking speed that matches the Walk clip at playback speed 1.")]
        [Min(.01f)] public float walkAnimationReferenceSpeed = .34f;
        [Min(.01f)] public float speed = .34f;
        public float turnSpeed = 540;
        public WalkState State { get; private set; }
        public string LastError { get; private set; } = "";
        public Vector3 Destination { get; private set; }
        public Vector3 RequestedDestination { get; private set; }
        public bool DestinationAdjusted { get; private set; }
        public bool IsMoving => State == WalkState.Moving || State == WalkState.Turning;
        public bool ActionAnimationActive { get; set; }
        public bool IsReady => agent && agent.enabled && agent.isOnNavMesh;
        public event Action<WalkState> StateChanged;
        NavMeshAgent agent;
        NavMeshPath candidate;
        Vector3 lastProgressPosition;
        float stalledTime;
        float? finalHeading;
        static readonly int IsWalkingParameter = Animator.StringToHash("IsWalking");
        static readonly int WalkSpeedParameter = Animator.StringToHash("WalkSpeed");
        public float ActualSpeed => IsReady ? new Vector2(agent.velocity.x, agent.velocity.z).magnitude : 0;

        void Awake()
        {
            candidate = new NavMeshPath();
            agent = GetComponent<NavMeshAgent>();
            agent.enabled = false;
            if (!animator && visual) animator = visual.GetComponent<Animator>();
            if (animator) animator.applyRootMotion = false;
        }

        void Start()
        {
            if (!navigation || !navigation.TrySample(transform.position, .15f, out var start))
            { Fail("角色起点不在可行走的桌面上"); return; }
            transform.position = start;
            agent.agentTypeID = 0;
            agent.radius = navigation.agentRadius;
            agent.height = navigation.agentHeight;
            agent.baseOffset = 0;
            agent.speed = speed;
            agent.acceleration = 1.8f;
            agent.angularSpeed = turnSpeed;
            agent.stoppingDistance = .018f;
            agent.autoBraking = true;
            agent.autoRepath = true;
            agent.updateRotation = false;
            // One character uses the radius-eroded, carved NavMesh. Local obstacle steering can
            // fight the corridor path at desk edges and stall even when a complete path exists.
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            agent.enabled = true;
            agent.Warp(start);
        }

        /// <summary>World-space destination. Invalid requests preserve an existing movement.</summary>
        public bool CanReach(Vector3 worldPosition)
        {
            return TryResolveDestination(worldPosition, out _, out _);
        }

        public bool TryResolveDestination(Vector3 requested, out Vector3 target, out bool adjusted)
        {
            target = default; adjusted = false;
            if (!IsReady || !float.IsFinite(requested.x) || !float.IsFinite(requested.y) || !float.IsFinite(requested.z)) return false;
            if (navigation.ContainsXZ(requested))
                return navigation.TrySample(requested, .06f, out target) && agent.CalculatePath(target, candidate) &&
                    candidate.status == NavMeshPathStatus.PathComplete;
            adjusted = true;
            return navigation.TryNearestReachable(requested, transform.position, candidate, out target);
        }

        public bool TryMoveTo(Vector3 worldPosition, float? headingDegrees = null)
        {
            LastError = "";
            if (!IsReady) { LastError = "寻路尚未就绪"; return false; }
            if (headingDegrees.HasValue && !float.IsFinite(headingDegrees.Value))
            { LastError = "朝向必须是有限角度"; return false; }
            if (!TryResolveDestination(worldPosition, out var target, out var adjusted))
            { LastError = "目标无效、在障碍物内，或没有可到达的桌面位置"; return false; }
            if (!agent.SetPath(candidate)) { LastError = "无法设置路径"; return false; }
            agent.isStopped = false;
            Destination = target;
            RequestedDestination = worldPosition; DestinationAdjusted = adjusted;
            finalHeading = headingDegrees;
            stalledTime = 0; lastProgressPosition = transform.position;
            SetState(WalkState.Moving);
            return true;
        }

        public bool TryFace(float headingDegrees)
        {
            if (!IsReady || !float.IsFinite(headingDegrees)) return false;
            agent.ResetPath(); agent.velocity = Vector3.zero;
            Destination = transform.position; finalHeading = headingDegrees;
            RequestedDestination = Destination; DestinationAdjusted = false;
            SetState(WalkState.Turning);
            return true;
        }

        public void CancelMove()
        {
            if (IsReady) { agent.ResetPath(); agent.velocity = Vector3.zero; }
            finalHeading = null;
            if (HasAnimationController) animator.SetBool(IsWalkingParameter, false);
            SetState(WalkState.Idle);
        }

        public Vector3[] GetPathCorners() => IsReady && agent.hasPath ? agent.path.corners : Array.Empty<Vector3>();

        void Update()
        {
            if (State == WalkState.Moving && IsReady)
            {
                if (!agent.pathPending)
                {
                    if (agent.isPathStale || agent.pathStatus != NavMeshPathStatus.PathComplete)
                        Fail("路径被障碍物阻断");
                    else if (Vector3.Distance(transform.position, Destination) <= agent.stoppingDistance + .012f)
                    {
                        agent.ResetPath(); agent.velocity = Vector3.zero;
                        SetState(finalHeading.HasValue ? WalkState.Turning : WalkState.Arrived);
                    }
                    else
                    {
                        if (Vector3.Distance(transform.position, lastProgressPosition) > .015f)
                        { lastProgressPosition = transform.position; stalledTime = 0; }
                        else stalledTime += Time.deltaTime;
                        if (stalledTime > 4) Fail("前方受阻，已停止移动");
                    }
                }
                var direction = agent.velocity; direction.y = 0;
                if (direction.sqrMagnitude > .0001f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation,
                        Quaternion.LookRotation(direction), turnSpeed * Time.deltaTime);
            }
            if (State == WalkState.Turning && finalHeading.HasValue)
            {
                var target = Quaternion.Euler(0, finalHeading.Value, 0);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
                if (Quaternion.Angle(transform.rotation, target) <= .5f)
                { transform.rotation = target; finalHeading = null; SetState(WalkState.Arrived); }
            }
            UpdateAnimation();
        }

        bool HasAnimationController => animator && animator.isActiveAndEnabled && animator.runtimeAnimatorController;

        void UpdateAnimation()
        {
            if (!HasAnimationController || ActionAnimationActive) return;
            float actualSpeed = ActualSpeed;
            bool walking = IsMoving && actualSpeed > .012f;
            animator.SetBool(IsWalkingParameter, walking);
            // Match cadence to actual agent velocity, including its acceleration and braking.
            float playback = walking ? Mathf.Clamp(actualSpeed / walkAnimationReferenceSpeed, .2f, 1.6f) : 1;
            animator.SetFloat(WalkSpeedParameter, playback, .08f, Time.deltaTime);
        }

        void Fail(string reason)
        {
            LastError = reason;
            if (IsReady) { agent.ResetPath(); agent.velocity = Vector3.zero; }
            SetState(WalkState.Failed);
        }

        void SetState(WalkState state) { if (State == state) return; State = state; StateChanged?.Invoke(state); }

        void OnDisable()
        {
            CancelMove();
            if (agent) agent.enabled = false;
        }
    }
}
