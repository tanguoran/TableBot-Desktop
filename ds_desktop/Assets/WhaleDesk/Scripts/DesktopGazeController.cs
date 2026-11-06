using System;
using UniVRM10;
using UnityEngine;

namespace WhaleDesk
{
    // Animator evaluates first; apply the head pose before VRM's eyes and spring bones (11000).
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DesktopWalker))]
    public sealed class DesktopGazeController : MonoBehaviour
    {
        public Camera targetCamera;
        public bool lookAtCameraWhenIdle = true;
        [Range(0, 60)] public float maxHeadYaw = 35;
        [Range(0, 40)] public float maxHeadPitch = 25;
        [Min(1)] public float followSpeed = 7;
        public string Mode { get; private set; } = "none";
        public bool IsTracking { get; private set; }
        public bool IsLimited { get; private set; }
        public string TrackedObjectId => Mode == "object" && IsTracking && observedObject ? observedObject.objectId : "";
        public Vector3 TargetPosition { get; private set; }
        public Vector2 HeadAngles { get; private set; }
        public bool HasEyes => vrm && vrm.enabled;
        DesktopWalker walker;
        Transform head, eyeTarget;
        Vrm10Instance vrm;
        Transform originalEyeTarget;
        VRM10ObjectLookAt.LookAtTargetTypes originalEyeType;
        DesktopWorldObject observedObject;
        string targetType = "";
        Vector3 worldTarget;
        bool appliedHead;
        float observeStarted;
        Quaternion animatedHeadRotation;

        void Awake()
        {
            walker = GetComponent<DesktopWalker>();
            if (!walker.animator || !walker.animator.isHuman) return;
            walker.animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            head = walker.animator.GetBoneTransform(HumanBodyBones.Head);
            vrm = walker.visual.GetComponentInChildren<Vrm10Instance>();
            if (vrm)
            {
                originalEyeTarget = vrm.LookAtTarget; originalEyeType = vrm.LookAtTargetType;
                eyeTarget = new GameObject("Gaze Target").transform;
                eyeTarget.SetParent(transform, false);
                eyeTarget.position = head ? head.position + transform.forward * 3 : transform.position + transform.forward * 3;
            }
        }

        public bool TryCamera(out Camera camera)
        {
            camera = targetCamera && targetCamera.isActiveAndEnabled ? targetCamera : Camera.main;
            return camera && camera.isActiveAndEnabled && camera.gameObject.scene == gameObject.scene;
        }

        public bool TryResolveTarget(string type, DesktopWorldObject item, Vector3 point, out Vector3 position)
        {
            position = Vector3.zero;
            if (!head) return false;
            if (type == "camera")
            {
                if (!TryCamera(out var camera)) return false;
                position = camera.orthographic ? head.position - camera.transform.forward * 3 : camera.transform.position;
            }
            else if (type == "object")
            {
                if (!item || !item.isActiveAndEnabled || !item.includeInState || item.gameObject.scene != gameObject.scene) return false;
                position = item.WorldBounds().center;
            }
            else if (type == "position") position = point;
            else if (type == "look_around") position = head.position +
                Quaternion.Euler(0, Mathf.Sin((Time.time - observeStarted) * .8f) * 20, 0) * transform.forward;
            else return false;
            return float.IsFinite(position.x) && float.IsFinite(position.y) && float.IsFinite(position.z) &&
                (position - head.position).sqrMagnitude > .000001f;
        }

        public bool Focus(string type, DesktopWorldObject item, Vector3 position)
        {
            if (!TryResolveTarget(type, item, position, out _)) return false;
            targetType = type; observedObject = item; worldTarget = position;
            return true;
        }
        public void Observe(DesktopWorldObject item)
        { observeStarted = Time.time; Focus(item ? "object" : "look_around", item, Vector3.zero); }
        public void ClearOverride() { targetType = ""; observedObject = null; }

        void Update()
        {
            // Remove our previous offset before Animator evaluates, including culled/static poses.
            if (appliedHead && head) head.localRotation = animatedHeadRotation;
            appliedHead = false;
        }

        void LateUpdate()
        {
            if (!head) { Mode = "unavailable"; return; }
            Mode = !string.IsNullOrEmpty(targetType) ? targetType : lookAtCameraWhenIdle ? "camera" : "none";
            bool hasTarget = TryResolveTarget(Mode, observedObject, worldTarget, out var target);
            if (!hasTarget) target = head.position + transform.forward;
            TargetPosition = target;
            var local = transform.InverseTransformDirection((target - head.position).normalized);
            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
            // Never twist around to the camera while walking away. Explicit camera focus turns the body first.
            IsTracking = hasTarget && Mathf.Abs(yaw) < 95;
            IsLimited = hasTarget && (Mathf.Abs(yaw) > maxHeadYaw || Mathf.Abs(pitch) > maxHeadPitch);
            float movingWeight = walker.IsMoving ? .3f : 1;
            Vector2 desired = IsTracking ? new Vector2(Mathf.Clamp(yaw, -maxHeadYaw, maxHeadYaw),
                Mathf.Clamp(pitch, -maxHeadPitch, maxHeadPitch)) * movingWeight : Vector2.zero;
            HeadAngles = Vector2.Lerp(HeadAngles, desired, 1 - Mathf.Exp(-followSpeed * Time.deltaTime));
            animatedHeadRotation = head.localRotation; appliedHead = true;
            var adjustment = transform.rotation * Quaternion.Euler(HeadAngles.y, HeadAngles.x, 0) * Quaternion.Inverse(transform.rotation);
            head.rotation = adjustment * head.rotation;
            if (HasEyes && eyeTarget)
            {
                // Moving the target towards neutral also makes eye tracking fade rather than snap.
                var neutral = head.position + transform.forward * 3;
                var eyePoint = IsTracking ? target : neutral;
                eyeTarget.position = Vector3.Lerp(eyeTarget.position, eyePoint, 1 - Mathf.Exp(-followSpeed * Time.deltaTime));
                vrm.LookAtTargetType = VRM10ObjectLookAt.LookAtTargetTypes.SpecifiedTransform;
                vrm.LookAtTarget = eyeTarget;
            }
        }

        public GazeSnapshot Capture() => new GazeSnapshot {
            mode = Mode, tracking = IsTracking, limited = IsLimited, eye_tracking_available = HasEyes,
            target_object_id = Mode == "object" && observedObject ? observedObject.objectId : "",
            target_position = TargetPosition, head_yaw_degrees = HeadAngles.x, head_pitch_degrees = HeadAngles.y
        };

        void OnDisable()
        {
            if (appliedHead && head) head.localRotation = animatedHeadRotation;
            appliedHead = false; ClearOverride(); HeadAngles = Vector2.zero;
            IsTracking = false; IsLimited = false; Mode = "none";
            if (vrm && vrm.LookAtTarget == eyeTarget)
            {
                vrm.LookAtTarget = originalEyeTarget; vrm.LookAtTargetType = originalEyeType;
                if (!originalEyeTarget && Application.isPlaying) vrm.Runtime.LookAt.SetYawPitchManually(0, 0);
            }
        }
        void OnDestroy() { if (eyeTarget) Destroy(eyeTarget.gameObject); }
    }

    [Serializable] public sealed class GazeSnapshot
    {
        public string mode, target_object_id;
        public bool tracking, limited, eye_tracking_available;
        public Vector3 target_position;
        public float head_yaw_degrees, head_pitch_degrees;
    }
}
