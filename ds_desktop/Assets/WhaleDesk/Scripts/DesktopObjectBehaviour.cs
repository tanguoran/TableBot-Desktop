using System;
using UnityEngine;

namespace WhaleDesk
{
    // Explicit adapters own real functionality. No reflection or arbitrary property writes from the model.
    public abstract class DesktopObjectBehaviour : MonoBehaviour
    {
        public virtual ObjectValue[] Properties() => Array.Empty<ObjectValue>();
        public virtual ObjectValue[] State() => Array.Empty<ObjectValue>();
        public abstract ObjectInteraction[] Interactions();
        public abstract bool Execute(string operation, out string error);
    }

    [Serializable] public sealed class ObjectValue
    {
        public string key, description;
        public string type = "string";
        public string text;
        public bool boolean;
        public float number;
        public string unit;
        public static ObjectValue Bool(string key, bool value) => new ObjectValue { key = key, type = "boolean", boolean = value };
        public static ObjectValue Text(string key, string value) => new ObjectValue { key = key, type = "string", text = value };
        public static ObjectValue Number(string key, float value, string unit = "") => new ObjectValue { key = key, type = "number", number = value, unit = unit };
    }

    [Serializable] public sealed class ObjectInteraction
    {
        public string operation, description;
        public bool available = true;
        public bool allow_autonomous = true;
        public string unavailable_reason = "";
    }
}
