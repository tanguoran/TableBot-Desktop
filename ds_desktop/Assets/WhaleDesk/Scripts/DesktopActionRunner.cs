using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace WhaleDesk
{
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(DesktopWalker))]
    public sealed class DesktopActionRunner : MonoBehaviour
    {
        public DesktopWalker walker;
        public AnimationClip sitEnter, sitIdle, sitExit, jumpStart, jumpLoop, jumpLand;
        public bool IsBusy { get; private set; }
        public string Posture { get; private set; } = "standing";
        public string Phase { get; private set; } = "idle";
        public string CurrentAction => active?.name ?? "none";
        public int ManualControlRevision { get; private set; }
        public float VerticalOffset { get; private set; }
        ActionCommand active;
        Action<ActionEvent> completion;
        Coroutine routine;
        Vector3 visualRest;
        DesktopGazeController gaze;
        DesktopWorldObject gazeTarget;

        void Awake()
        {
            if (!walker) walker = GetComponent<DesktopWalker>();
            visualRest = walker.visual.localPosition;
            gaze = GetComponent<DesktopGazeController>();
            if (!gaze) gaze = gameObject.AddComponent<DesktopGazeController>();
            if (!GetComponent<DesktopExpressionController>()) gameObject.AddComponent<DesktopExpressionController>();
        }

        public bool TryStart(ActionCommand command, Action<ActionEvent> completed)
        {
            if (IsBusy || walker.IsMoving || !walker.IsReady) return false;
            active = command; completion = completed; IsBusy = true;
            routine = StartCoroutine(Perform(command));
            return true;
        }

        DesktopWorldObject FindObject(string id) => DesktopWorldObject.Perceived.FirstOrDefault(o =>
            o.objectId == id && o.gameObject.scene == gameObject.scene && !o.GetComponentInParent<DesktopCharacterState>());

        IEnumerator Perform(ActionCommand command)
        {
            yield return null;
            float? heading = command.has_heading ? command.heading_degrees : (float?)null;
            if (command.name == "interact_object")
            {
                var item = FindObject(command.object_id);
                string error = "Object is not exposed";
                bool ok = item && item.Interact(command.operation, out error);
                Finish(ok ? "succeeded" : "failed", error, item);
                yield break;
            }
            if (command.name == "move_to" || command.name == "approach_object")
            {
                yield return Stand();
                Vector3 target;
                if (command.name == "move_to") target = new Vector3(command.x, walker.navigation.SurfaceHeight, command.z);
                else
                {
                    var item = FindObject(command.object_id);
                    var point = item ? item.GetApproaches(walker).Where(p => p.reachable &&
                        (command.side == "nearest" || p.side == "nearest" || p.side == command.side)).OrderBy(p => p.path_length).FirstOrDefault() : null;
                    if (point == null) { Finish("failed", "Object/approach is unavailable or unreachable"); yield break; }
                    target = point.requested_position;
                    if (!heading.HasValue)
                    {
                        var direction = item.WorldBounds().center - point.position;
                        heading = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                    }
                }
                if (!walker.TryMoveTo(target, heading)) { Finish("failed", walker.LastError); yield break; }
                Phase = "moving";
                var expected = walker.Destination;
                while (walker.IsMoving)
                {
                    if (command.name == "approach_object" && !FindObject(command.object_id))
                    { walker.CancelMove(); Finish("failed", "Target was removed or is no longer displayed"); yield break; }
                    if (Vector3.Distance(walker.Destination, expected) > .001f)
                    { Cancel("Replaced by local movement", false); yield break; }
                    Phase = walker.State == WalkState.Turning ? "turning" : "moving";
                    yield return null;
                }
                Finish(walker.State == WalkState.Arrived ? "arrived" : walker.State == WalkState.Failed ? "failed" : "cancelled", walker.LastError);
                yield break;
            }
            if (command.name == "stand_up") { yield return Stand(); Finish("succeeded"); yield break; }
            if (command.name == "sit_down")
            {
                if (!sitEnter || !sitIdle || !sitExit) { Finish("failed", "Sitting clips are not installed"); yield break; }
                if (heading.HasValue) yield return Face(heading.Value);
                if (Posture != "sitting")
                {
                    walker.ActionAnimationActive = true; Phase = "sitting_down";
                    Play("SitEnter"); yield return new WaitForSeconds(sitEnter.length / 1.5f);
                    Posture = "sitting"; Play("SitIdle");
                }
                Finish("succeeded"); yield break;
            }
            if (command.name == "look_at" || command.name == "look_at_camera")
            {
                string type = command.name == "look_at_camera" ? "camera" : command.target_type;
                var item = type == "object" ? FindObject(command.object_id) : null;
                var point = new Vector3(command.x, command.y, command.z);
                if (!gaze.TryResolveTarget(type, item, point, out var target))
                { Finish("failed", "Look target is unavailable, invalid or coincides with the head"); yield break; }
                gaze.Focus(type, item, point);
                bool turnBody = command.name == "look_at_camera" || command.turn_body;
                if (turnBody)
                {
                    var direction = target - transform.position; direction.y = 0;
                    if (direction.sqrMagnitude > .00001f) yield return Face(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg);
                }
                walker.ActionAnimationActive = true; Phase = "looking_at";
                Play(Posture == "sitting" ? "SitIdle" : "Idle");
                float until = Time.time + Mathf.Clamp(command.duration_seconds, 1, 20);
                while (Time.time < until)
                {
                    if (!gaze.TryResolveTarget(type, item, point, out target))
                    { Finish("failed", "Look target became unavailable"); yield break; }
                    // Keep following a moving target with the body only when explicitly allowed.
                    var direction = target - transform.position; direction.y = 0;
                    if (turnBody && direction.sqrMagnitude > .00001f && !walker.IsMoving &&
                        Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg)) > 1)
                        walker.TryFace(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg);
                    yield return null;
                }
                while (turnBody && walker.IsMoving)
                {
                    if (!gaze.TryResolveTarget(type, item, point, out _))
                    { walker.CancelMove(); Finish("failed", "Look target became unavailable"); yield break; }
                    yield return null;
                }
                Finish("succeeded"); yield break;
            }
            if (command.name == "observe")
            {
                gazeTarget = string.IsNullOrEmpty(command.object_id) ? null : FindObject(command.object_id);
                if (!string.IsNullOrEmpty(command.object_id) && !gazeTarget)
                { Finish("failed", "Observed object is not exposed"); yield break; }
                if (!heading.HasValue && gazeTarget)
                {
                    var direction = gazeTarget.WorldBounds().center - transform.position;
                    heading = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                }
                if (heading.HasValue) yield return Face(heading.Value);
                walker.ActionAnimationActive = true; Phase = "observing";
                Play(Posture == "sitting" ? "SitIdle" : "Idle");
                gaze.Observe(gazeTarget);
                float until = Time.time + Mathf.Clamp(command.duration_seconds, 1, 20);
                while (Time.time < until)
                {
                    if (!string.IsNullOrEmpty(command.object_id) && (!gazeTarget || !gazeTarget.isActiveAndEnabled || !gazeTarget.includeInState))
                    { Finish("failed", "Observed object became unavailable"); yield break; }
                    yield return null;
                }
                Finish("succeeded"); yield break;
            }
            if (command.name == "jump")
            {
                if (!jumpStart || !jumpLoop || !jumpLand) { Finish("failed", "Jump clips are not installed"); yield break; }
                yield return Stand();
                if (heading.HasValue) yield return Face(heading.Value);
                walker.ActionAnimationActive = true; Phase = "jump_start";
                Play("JumpStart"); yield return new WaitForSeconds(jumpStart.length / 2);
                Posture = "airborne"; Phase = "airborne"; Play("JumpLoop", .06f);
                float start = Time.time, flight = .65f, height = Mathf.Clamp(command.height, .05f, .18f);
                while (Time.time - start < flight)
                {
                    VerticalOffset = Mathf.Sin((Time.time - start) / flight * Mathf.PI) * height;
                    walker.visual.localPosition = visualRest + Vector3.up * VerticalOffset;
                    yield return null;
                }
                VerticalOffset = 0; walker.visual.localPosition = visualRest;
                Posture = "standing"; Phase = "landing"; Play("JumpLand", .06f);
                yield return new WaitForSeconds(jumpLand.length / 1.5f);
                Finish("succeeded"); yield break;
            }
            Finish("failed", "Unsupported action");
        }

        IEnumerator Face(float heading)
        {
            Phase = "turning";
            if (walker.TryFace(heading)) while (walker.IsMoving) yield return null;
        }

        IEnumerator Stand()
        {
            if (Posture != "sitting") yield break;
            Phase = "standing_up"; walker.ActionAnimationActive = true;
            Play("StandUp");
            if (sitExit) yield return new WaitForSeconds(sitExit.length / 1.5f);
            Posture = "standing"; walker.ActionAnimationActive = false; Play("Idle");
        }

        void Play(string state, float fade = .12f)
        {
            if (!walker.animator || !walker.animator.isActiveAndEnabled || !walker.animator.runtimeAnimatorController) return;
            walker.animator.SetBool("IsWalking", false);
            walker.animator.CrossFadeInFixedTime(state, fade);
        }

        void Finish(string state, string error = "", DesktopWorldObject item = null)
        {
            var command = active; var callback = completion;
            var finalGaze = gaze.Capture();
            active = null; completion = null; routine = null; IsBusy = false; gazeTarget = null;
            gaze.ClearOverride();
            VerticalOffset = 0; walker.visual.localPosition = visualRest;
            walker.ActionAnimationActive = Posture == "sitting";
            Phase = Posture == "sitting" ? "seated" : "idle";
            Play(Posture == "sitting" ? "SitIdle" : "Idle");
            if (command != null) callback?.Invoke(new ActionEvent {
                action_id = command.action_id, state = state, error = error,
                object_state = item ? item.Capture(walker) : null,
                position = transform.position, destination = walker.Destination,
                heading_degrees = transform.eulerAngles.y, posture = Posture, gaze = finalGaze,
                requested_destination = command.name == "move_to" || command.name == "approach_object" ? walker.RequestedDestination : transform.position,
                destination_adjusted = (command.name == "move_to" || command.name == "approach_object") && walker.DestinationAdjusted
            });
        }

        public void Cancel(string reason = "Action cancelled", bool stopMovement = true, string terminalState = "cancelled")
        {
            if (routine != null) StopCoroutine(routine);
            if (stopMovement) walker.CancelMove();
            Posture = "standing";
            if (active != null) Finish(terminalState, reason);
            else
            {
                gaze.ClearOverride(); VerticalOffset = 0; walker.visual.localPosition = visualRest;
                walker.ActionAnimationActive = false; Phase = "idle"; Play("Idle");
            }
        }

        public void NotifyManualInput(bool preserveSeated = false)
        {
            bool seated = preserveSeated && Posture == "sitting";
            ManualControlRevision++; Cancel("Manual user control");
            if (seated)
            {
                Posture = "sitting"; Phase = "seated";
                walker.ActionAnimationActive = true; Play("SitIdle");
            }
        }
        void OnDisable() { if (walker && walker.visual) Cancel("Character disabled"); }
    }
}
