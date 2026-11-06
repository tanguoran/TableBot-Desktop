using System;
using System.Linq;
using UnityEngine;

namespace WhaleDesk
{
    [DisallowMultipleComponent]
    public sealed class DesktopLampInteraction : DesktopObjectBehaviour
    {
        [Tooltip("Explicit lamp lights. Empty discovers only this object's child lights, never room lighting.")]
        public Light[] lights = Array.Empty<Light>();
        Light[] Targets() => (lights.Length > 0 ? lights : GetComponentsInChildren<Light>(true))
            .Where(l => l && l.gameObject.activeInHierarchy).ToArray();
        public override ObjectValue[] Properties() => new[] { ObjectValue.Text("device", "lamp"), ObjectValue.Text("control_scope", "scene_lights") };
        public override ObjectValue[] State()
        {
            var targets = Targets();
            int on = targets.Count(l => l.enabled);
            return new[] { ObjectValue.Bool("is_on", on > 0), ObjectValue.Bool("mixed", on > 0 && on < targets.Length),
                ObjectValue.Number("light_count", targets.Length), ObjectValue.Number("enabled_light_count", on) };
        }
        public override ObjectInteraction[] Interactions()
        {
            bool ready = Targets().Length > 0;
            return new[] { Action("turn_on", "Switch the actual lamp lights on", ready), Action("turn_off", "Switch the actual lamp lights off", ready) };
        }
        static ObjectInteraction Action(string id, string description, bool ready) => new ObjectInteraction {
            operation = id, description = description, available = ready, unavailable_reason = ready ? "" : "No active lamp lights bound" };
        public override bool Execute(string operation, out string error)
        {
            var targets = Targets();
            if ((operation != "turn_on" && operation != "turn_off") || targets.Length == 0)
            { error = "Lamp operation unavailable"; return false; }
            foreach (var light in targets) light.enabled = operation == "turn_on";
            error = ""; return true;
        }
    }
}
