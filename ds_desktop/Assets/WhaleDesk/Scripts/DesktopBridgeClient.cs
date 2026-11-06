using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace WhaleDesk
{
    // All Unity operations stay on the main thread. The broker is loopback HTTP; MCP is hosted in Python.
    public sealed class DesktopBridgeClient : MonoBehaviour
    {
        public DesktopWorldState world;
        public string bridgeUrl = "http://127.0.0.1:18765";
        [Range(.1f, 2)] public float updateInterval = .2f;
        public bool Connected { get; private set; }
        public string LastConnectionError { get; private set; } = "";
        readonly List<ActionEvent> events = new List<ActionEvent>();
        readonly Dictionary<string, ActionEvent> completed = new Dictionary<string, ActionEvent>();
        ActionCommand active;
        float disconnectedSince = -1;
        Coroutine connection;
        DesktopWalker Walker => world.character.walker;
        DesktopActionRunner Runner => Walker.GetComponent<DesktopActionRunner>();

        void Awake()
        {
            Application.runInBackground = true;
            if (!GetComponent<DesktopChatUI>()) gameObject.AddComponent<DesktopChatUI>();
        }
        void OnEnable() { connection = StartCoroutine(ExchangeLoop()); }
        void OnDisable()
        {
            if (connection != null) StopCoroutine(connection);
            if (world && world.character) Runner.Cancel("Unity stopped");
            active = null; Connected = false;
        }

        void Update()
        {
            if (active == null) return;
            if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 > active.expires_at)
            { Runner.Cancel("Action deadline expired", true, "failed"); return; }
            if (disconnectedSince >= 0 && Time.realtimeSinceStartup - disconnectedSince > 3)
            { Runner.Cancel("Desktop bridge disconnected", true, "failed"); return; }
        }

        IEnumerator ExchangeLoop()
        {
            yield return null;
            if (!Uri.TryCreate(bridgeUrl, UriKind.Absolute, out var address) || address.Scheme != "http" ||
                !address.IsLoopback || !string.IsNullOrEmpty(address.UserInfo))
            { LastConnectionError = "Bridge URL must use loopback HTTP"; yield break; }
            while (enabled)
            {
                WorldSnapshot snapshot = null;
                    try { snapshot = world.Capture(active?.action_id ?? "", active?.name ?? ""); }
                catch (Exception error) { LastConnectionError = error.Message; }
                if (snapshot != null)
                {
                    var pending = events.ToArray();
                    var body = JsonUtility.ToJson(new ExchangeRequest { snapshot = snapshot, events = pending });
                    using (var request = new UnityWebRequest(bridgeUrl.TrimEnd('/') + "/unity/exchange", "POST"))
                    {
                        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                        request.downloadHandler = new DownloadHandlerBuffer();
                        request.SetRequestHeader("Content-Type", "application/json"); request.timeout = 2;
                        yield return request.SendWebRequest();
                        Connected = request.result == UnityWebRequest.Result.Success;
                        if (Connected)
                        {
                            disconnectedSince = -1; LastConnectionError = "";
                            events.RemoveRange(0, pending.Length);
                            ExchangeResponse response = null;
                            try { response = JsonUtility.FromJson<ExchangeResponse>(request.downloadHandler.text); }
                            catch (Exception error) { LastConnectionError = error.Message; }
                            foreach (var command in response?.commands ?? Array.Empty<ActionCommand>()) Execute(command);
                        }
                        else
                        {
                            LastConnectionError = request.error;
                            if (disconnectedSince < 0) disconnectedSince = Time.realtimeSinceStartup;
                        }
                    }
                }
                yield return new WaitForSecondsRealtime(Connected ? updateInterval : 1);
            }
        }

        void Execute(ActionCommand command)
        {
            if (command.scene_session != world.SessionId) return;
            if (completed.TryGetValue(command.action_id, out var previous)) { events.Add(previous); return; }
            if (active?.action_id == command.action_id) { Emit(command, "running"); return; }
            if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 > command.expires_at)
            { Emit(command, "failed", "Command expired before execution"); return; }
            if (command.name == "set_expression")
            {
                var face = Walker.GetComponent<DesktopExpressionController>();
                bool applied = face && face.TrySet(command.emotion, command.intensity, command.duration_seconds);
                Emit(command, applied ? "succeeded" : "failed", applied ? "" : "Expression unavailable or invalid");
                return;
            }
            if (command.name == "cancel_move")
            {
                Runner.Cancel("Cancelled by tool request");
                Emit(command, "succeeded"); return;
            }
            if (active != null || Walker.IsMoving || Runner.IsBusy) { Emit(command, "failed", "Character is busy"); return; }
            active = command;
            if (!Runner.TryStart(command, result => {
                if (active?.action_id == result.action_id) active = null;
                Record(result);
            })) { active = null; Emit(command, "failed", "Character is not ready"); return; }
            Emit(command, "running");
        }

        void Emit(ActionCommand command, string state, string error = "")
        {
            var result = new ActionEvent { action_id = command.action_id, state = state, error = error,
                position = Walker.transform.position, destination = Walker.Destination,
                heading_degrees = Walker.transform.eulerAngles.y, posture = Runner.Posture };
            Record(result);
        }

        void Record(ActionEvent result)
        {
            events.Add(result);
            if (result.state != "running")
            {
                if (completed.Count >= 256) completed.Remove(completed.Keys.First());
                completed[result.action_id] = result;
            }
        }
    }

    [Serializable] public sealed class ExchangeRequest { public WorldSnapshot snapshot; public ActionEvent[] events; }
    [Serializable] public sealed class ExchangeResponse { public ActionCommand[] commands; }
    [Serializable] public sealed class ActionCommand
    {
        public string action_id, scene_session, name, object_id, side, target_type;
        public float x, y, z, heading_degrees, duration_seconds, height;
        public bool has_heading, turn_body;
        public string emotion;
        public string operation;
        public float intensity;
        public double expires_at;
    }
    [Serializable] public sealed class ActionEvent
    {
        public string action_id, state, error;
        public Vector3 position, destination, requested_destination;
        public bool destination_adjusted;
        public float heading_degrees;
        public string posture;
        public GazeSnapshot gaze;
        public ObjectSnapshot object_state;
    }
}
