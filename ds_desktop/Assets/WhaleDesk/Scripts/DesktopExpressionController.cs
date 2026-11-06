using System;
using UniVRM10;
using UnityEngine;

namespace WhaleDesk
{
    [DefaultExecutionOrder(10001), DisallowMultipleComponent]
    public sealed class DesktopExpressionController : MonoBehaviour
    {
        static readonly string[] Names = { "happy", "angry", "sad", "relaxed" };
        static readonly ExpressionKey[] Keys = { ExpressionKey.Happy, ExpressionKey.Angry, ExpressionKey.Sad, ExpressionKey.Relaxed };
        readonly float[] weights = new float[Keys.Length];
        Vrm10Instance avatar;
        Vrm10RuntimeExpression face;
        int selected = -1;
        float strength, until, nextBlink, blinkWeight;

        void OnEnable()
        {
            avatar = GetComponentInChildren<Vrm10Instance>();
            nextBlink = Time.time + UnityEngine.Random.Range(2.5f, 5.5f);
        }

        public bool TrySet(string emotion, float intensity, float duration)
        {
            if (!isActiveAndEnabled || !avatar || !avatar.isActiveAndEnabled ||
                !float.IsFinite(intensity) || intensity < 0 || intensity > 1 ||
                !float.IsFinite(duration) || duration < .5f || duration > 20) return false;
            int index = Array.IndexOf(Names, emotion);
            if (index < 0 && emotion != "neutral") return false;
            face = avatar.Runtime.Expression;
            selected = intensity > 0 ? index : -1;
            strength = intensity;
            until = Time.time + duration;
            return true;
        }

        void LateUpdate()
        {
            if (!avatar || !avatar.isActiveAndEnabled) return;
            face = avatar.Runtime.Expression;
            if (Time.time >= until) selected = -1;
            for (int i = 0; i < Keys.Length; i++)
            {
                weights[i] = Mathf.MoveTowards(weights[i], i == selected ? strength : 0f, Time.deltaTime / .25f);
                face.SetWeight(Keys[i], weights[i]);
            }
            float t = Time.time - nextBlink;
            blinkWeight = t < 0 ? 0 : t < .07f ? Mathf.SmoothStep(0, 1, t / .07f)
                : Mathf.SmoothStep(1, 0, (t - .07f) / .12f);
            if (t >= .19f) nextBlink = Time.time + UnityEngine.Random.Range(2.5f, 5.5f);
            face.SetWeight(ExpressionKey.Blink, blinkWeight);
        }

        public ExpressionSnapshot Capture() => new ExpressionSnapshot {
            emotion = selected < 0 ? "neutral" : Names[selected], remaining_seconds = selected < 0 ? 0 : Mathf.Max(0, until - Time.time),
            happy = weights[0], angry = weights[1], sad = weights[2], relaxed = weights[3], blink = blinkWeight
        };

        void OnDisable()
        {
            if (face != null && avatar)
            {
                foreach (var key in Keys) face.SetWeight(key, 0);
                face.SetWeight(ExpressionKey.Blink, 0);
            }
            face = null;
            Array.Clear(weights, 0, weights.Length);
            selected = -1;
            blinkWeight = 0;
        }
    }

    [Serializable] public sealed class ExpressionSnapshot
    {
        public string emotion;
        public float remaining_seconds, happy, angry, sad, relaxed, blink;
    }
}
