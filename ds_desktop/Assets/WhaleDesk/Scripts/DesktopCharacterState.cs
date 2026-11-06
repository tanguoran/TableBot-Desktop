using System;
using UnityEngine;

namespace WhaleDesk
{
    // The companion is deliberately NOT a DesktopWorldObject. Object filters never remove her state.
    [DisallowMultipleComponent]
    public sealed class DesktopCharacterState : MonoBehaviour
    {
        public string characterId = "whale_girl";
        public string displayName = "大肥鱼";
        public DesktopWalker walker;
        public CharacterTraits traits = new CharacterTraits();
        float energy = .8f, varietyNeed = .3f, idleSeconds;
        bool airborne, restSuggested;
        string lastInspectedObject;
        DesktopActionRunner actions;
        DesktopGazeController gaze;

        void Start()
        {
            actions = GetComponent<DesktopActionRunner>();
            gaze = GetComponent<DesktopGazeController>();
        }

        // Session-local simulation, advanced by game time rather than by model polling.
        void Update()
        {
            if (!walker || !actions) return;
            float dt = Time.deltaTime;
            idleSeconds = actions.IsBusy || walker.IsMoving ? 0 : idleSeconds + dt;
            float distance = walker.ActualSpeed * dt;
            bool jumping = actions.Posture == "airborne";
            float recovery = actions.Posture == "sitting" ? .003f : walker.IsMoving || jumping ? 0 : .0005f;
            energy = Mathf.Clamp01(energy + recovery * dt - distance * .022f - (jumping && !airborne ? .035f : 0));
            airborne = jumping;
            if (energy <= traits.rest_threshold_01) restSuggested = true;
            if (energy >= traits.rest_threshold_01 + .2f) restSuggested = false;

            float novelty = 0;
            string subject = gaze ? gaze.TrackedObjectId : "";
            if (!string.IsNullOrEmpty(subject) && subject != lastInspectedObject)
            {
                novelty = .1f;
                lastInspectedObject = subject;
            }
            varietyNeed = Mathf.Clamp01(varietyNeed + (idleSeconds > 0 ? dt * (.0007f + traits.curiosity_01 * .0015f) : 0)
                - distance * .09f - novelty);
        }

        public CharacterSnapshot Capture(string actionId = "", string actionName = "")
        {
            var animator = walker.animator;
            var actions = this.actions ? this.actions : walker.GetComponent<DesktopActionRunner>();
            string animation = "unavailable";
            if (animator && animator.isActiveAndEnabled && animator.runtimeAnimatorController)
            {
                var state = animator.GetCurrentAnimatorStateInfo(0);
                animation = "transition";
                foreach (var entry in new[] { ("Idle", "idle"), ("Walk", "walk"), ("SitEnter", "sit_enter"),
                    ("SitIdle", "sit_idle"), ("StandUp", "stand_up"), ("JumpStart", "jump_start"),
                    ("JumpLoop", "jump_loop"), ("JumpLand", "jump_land") })
                    if (state.IsName(entry.Item1)) { animation = entry.Item2; break; }
            }
            return new CharacterSnapshot {
                character_id = characterId, display_name = displayName,
                traits = traits, idle_seconds = idleSeconds,
                body_action_ready = walker.IsReady && !walker.IsMoving && actions && !actions.IsBusy,
                simulated_needs = new CharacterNeeds { energy_01 = energy, variety_need_01 = varietyNeed, rest_suggested = restSuggested },
                position = walker.transform.position, heading_degrees = walker.transform.eulerAngles.y,
                speed = walker.ActualSpeed, radius = walker.navigation.agentRadius, height = walker.navigation.agentHeight,
                navigation_ready = walker.IsReady, movement_state = walker.State.ToString().ToLowerInvariant(),
                animation_state = animation, action_id = actionId,
                current_action = !string.IsNullOrEmpty(actionName) ? actionName : actions && actions.IsBusy ? "manual_action" : walker.IsMoving ? "manual_move" : "none",
                has_target = walker.IsMoving, target = walker.IsMoving ? walker.Destination : walker.transform.position,
                requested_target = walker.RequestedDestination, target_adjusted = walker.DestinationAdjusted,
                posture = actions ? actions.Posture : "standing", action_phase = actions ? actions.Phase : "idle",
                vertical_offset = actions ? actions.VerticalOffset : 0, visual_position = walker.visual.position,
                control_revision = actions ? actions.ManualControlRevision : 0,
                gaze = walker.GetComponent<DesktopGazeController>()?.Capture(),
                expression = walker.GetComponent<DesktopExpressionController>()?.Capture(),
                last_error = walker.LastError
            };
        }
    }

    [Serializable] public sealed class CharacterTraits
    {
        [Range(0, 1)] public float curiosity_01 = .65f;
        [Range(.1f, .6f)] public float rest_threshold_01 = .30f;
    }

    [Serializable] public sealed class CharacterNeeds
    {
        public float energy_01, variety_need_01;
        public bool rest_suggested;
    }

    [Serializable] public sealed class CharacterSnapshot
    {
        public string character_id, display_name, movement_state, animation_state, action_id, current_action, last_error;
        public CharacterTraits traits;
        public CharacterNeeds simulated_needs;
        public float idle_seconds;
        public bool body_action_ready;
        public Vector3 position, target, requested_target;
        public float heading_degrees, speed, radius, height;
        public bool navigation_ready, has_target, target_adjusted;
        public string posture, action_phase;
        public float vertical_offset;
        public Vector3 visual_position;
        public int control_revision;
        public GazeSnapshot gaze;
        public ExpressionSnapshot expression;
    }
}
