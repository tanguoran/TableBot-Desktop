using System;
using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace WhaleDesk
{
    public sealed class DesktopChatUI : MonoBehaviour
    {
        const string Endpoint = "http://127.0.0.1:18766";
        static readonly Color Ink = new Color(.105f, .14f, .20f);
        static DesktopChatUI active;
        GameObject uiObject;
        PanelSettings panel;
        Font font;
        VisualElement root, bar, bubble, tip;
        TextField input;
        Button send;
        Label hint, answer;
        ScrollView scroll;
        Transform head;
        bool posting, busy, online, showedBubble;
        string lastReply, ownRequest, submittedText, session, errorText;

        [Serializable] sealed class Message { public string request_id, text; }
        [Serializable] sealed class Snapshot
        {
            public string session, request_id, status, error, reply_id, reply;
        }

        void OnEnable()
        {
            active = this;
            posting = busy = online = showedBubble = false;
            session = lastReply = ownRequest = errorText = null;
            uiObject = new GameObject("Whale Chat Panel");
            uiObject.transform.SetParent(transform, false);
            uiObject.SetActive(false);
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("StudyChatTheme");
            panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            panel.sortingOrder = 20;
            var doc = uiObject.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            uiObject.SetActive(true);
            root = doc.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.style.flexGrow = 1;
            BuildControls();
            StartCoroutine(Poll());
        }

        void BuildControls()
        {
            bar = new VisualElement();
            root.Add(bar);
            bar.style.position = Position.Absolute;
            bar.style.bottom = 10;
            bar.style.backgroundColor = new Color(.09f,.12f,.17f,.95f);
            Decorate(bar);
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            bar.Add(row);
            input = new TextField { maxLength = 4000 };
            input.textEdition.placeholder = "和大肥鱼说句话…";
            input.style.flexGrow = 1;
            input.style.minWidth = 0;
            input.style.fontSize = 15;
            row.Add(input);
            input.RegisterCallback<KeyDownEvent>(e => {
                if (e.ctrlKey && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
                { e.StopPropagation(); Submit(); }
            }, TrickleDown.TrickleDown);
            send = new Button(Submit) { text = "发送", tooltip = "Ctrl + Enter" };
            send.style.minWidth = 54;
            send.style.height = 32;
            row.Add(send);
            hint = new Label();
            hint.style.fontSize = 12;
            hint.style.color = new Color(.84f,.87f,.91f);
            bar.Add(hint);

            bubble = new VisualElement();
            root.Add(bubble);
            bubble.style.position = Position.Absolute;
            bubble.style.backgroundColor = new Color(1,.97f,.91f,.98f);
            Decorate(bubble);
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            bubble.Add(header);
            var name = new Label("大肥鱼");
            name.style.flexGrow = 1;
            name.style.color = new Color(.21f,.35f,.52f);
            header.Add(name);
            header.Add(new Button(() => { showedBubble = false; bubble.style.display = DisplayStyle.None; }) { text = "×" });
            scroll = new ScrollView(ScrollViewMode.Vertical);
            bubble.Add(scroll);
            answer = new Label { enableRichText = false };
            answer.style.whiteSpace = WhiteSpace.Normal;
            answer.style.fontSize = 16;
            answer.style.color = Ink;
            scroll.Add(answer);
            tip = new VisualElement { pickingMode = PickingMode.Ignore };
            tip.style.position = Position.Absolute;
            tip.style.width = tip.style.height = 9;
            tip.style.bottom = -4;
            tip.style.rotate = new Rotate(new Angle(45));
            tip.style.backgroundColor = bubble.style.backgroundColor;
            bubble.Add(tip);
            bubble.style.display = DisplayStyle.None;

            var installed = Font.GetOSInstalledFontNames();
            var family = new[] { "Microsoft YaHei UI","Microsoft YaHei","Yu Gothic","Arial" }.FirstOrDefault(installed.Contains);
            if (family != null)
            {
                font = Font.CreateDynamicFontFromOSFont(family, 18);
                root.Query<TextElement>().ForEach(text => text.style.unityFontDefinition = FontDefinition.FromFont(font));
            }
            input.RegisterValueChangedCallback(_ => RefreshStatus());
            RefreshStatus();
        }

        static void Decorate(VisualElement box)
        {
            box.style.paddingLeft = box.style.paddingRight = 10;
            box.style.paddingTop = box.style.paddingBottom = 6;
            box.style.borderTopLeftRadius = box.style.borderTopRightRadius = 10;
            box.style.borderBottomLeftRadius = box.style.borderBottomRightRadius = 10;
        }

        void RefreshStatus()
        {
            send.SetEnabled(online && !posting && !busy && !string.IsNullOrWhiteSpace(input.value));
            hint.text = !online ? "等待连接 JARVIS · 请启动 wsl.ps1" : errorText ??
                (busy || posting ? "大肥鱼正在回复…" : "Ctrl + Enter 发送");
        }

        void Submit()
        {
            if (!online || busy || posting || string.IsNullOrWhiteSpace(input.value)) return;
            posting = true;
            errorText = null;
            ownRequest = Guid.NewGuid().ToString("N");
            submittedText = input.value;
            RefreshStatus();
            StartCoroutine(Post(new Message { request_id = ownRequest, text = submittedText.Trim() }));
        }

        IEnumerator Post(Message message)
        {
            using (var req = new UnityWebRequest(Endpoint + "/message", "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(message)));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type","application/json");
                req.timeout = 5;
                yield return req.SendWebRequest();
                posting = false;
                if (req.result == UnityWebRequest.Result.Success)
                {
                    busy = true;
                    if (input.value == submittedText) input.value = "";
                }
                else
                {
                    errorText = "发送失败，请重试。";
                    if (req.result == UnityWebRequest.Result.ProtocolError)
                        errorText = Decode(req.downloadHandler.text)?.error ?? errorText;
                }
                RefreshStatus();
            }
        }

        IEnumerator Poll()
        {
            while (enabled)
            {
                using (var req = UnityWebRequest.Get(Endpoint + "/state"))
                {
                    req.timeout = 3;
                    yield return req.SendWebRequest();
                    var s = req.result == UnityWebRequest.Result.Success ? Decode(req.downloadHandler.text) : null;
                    online = s != null && !string.IsNullOrEmpty(s.session);
                    if (online)
                    {
                        if (session != null && session != s.session)
                        {
                            showedBubble = false;
                            bubble.style.display = DisplayStyle.None;
                            if (busy && ownRequest != null)
                            {
                                errorText = "JARVIS 已重启，请重新发送。";
                                if (string.IsNullOrEmpty(input.value)) input.value = submittedText;
                            }
                            ownRequest = null;
                        }
                        session = s.session;
                        busy = s.status == "pending";
                        if (ownRequest == s.request_id && s.status == "failed")
                        {
                            errorText = s.error;
                            if (string.IsNullOrEmpty(input.value)) input.value = submittedText;
                            ownRequest = null;
                        }
                        if (!string.IsNullOrEmpty(s.reply_id) && s.reply_id != lastReply)
                        {
                            lastReply = s.reply_id;
                            answer.text = s.reply;
                            showedBubble = true;
                            scroll.scrollOffset = Vector2.zero;
                        }
                    }
                    RefreshStatus();
                }
                yield return new WaitForSecondsRealtime(online ? .5f : 2);
            }
        }

        static Snapshot Decode(string json)
        {
            try { return JsonUtility.FromJson<Snapshot>(json); }
            catch (ArgumentException) { return null; }
        }

        void LateUpdate()
        {
            float width = root.layout.width, height = root.layout.height;
            if (!(width > 0 && height > 0)) return;
            float barWidth = Mathf.Min(490, width - 16);
            bar.style.width = barWidth; bar.style.left = (width - barWidth) / 2;
            if (!showedBubble) return;
            var world = GetComponent<DesktopWorldState>();
            var camera = Camera.main;
            if (!world || !world.character || !camera) return;
            var walker = world.character.walker;
            if (!head && walker.animator && walker.animator.isHuman)
                head = walker.animator.GetBoneTransform(HumanBodyBones.Head);
            var anchor = head ? head.position + Vector3.up * .15f : walker.transform.position + Vector3.up * walker.navigation.agentHeight;
            var p = camera.WorldToScreenPoint(anchor);
            bubble.style.display = p.z > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (p.z <= 0) return;
            float bubbleWidth = Mathf.Min(360, width - 16);
            bubble.style.width = bubbleWidth;
            scroll.style.maxHeight = Mathf.Max(50, height * .28f);
            var point = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(p.x, Screen.height-p.y));
            float left = Mathf.Clamp(point.x - bubbleWidth/2, 8, width - bubbleWidth - 8);
            bubble.style.left = left;
            tip.style.left = Mathf.Clamp(point.x - left - 4, 10, bubbleWidth - 19);
            float bottom = Mathf.Clamp(height - point.y, bar.layout.height + 18, Mathf.Max(bar.layout.height + 18, height - bubble.layout.height - 8));
            if (float.IsFinite(bottom)) bubble.style.bottom = bottom;
        }

        public static bool BlocksPointer(Vector2 point)
        {
            if (!active || active.root?.panel == null) return false;
            var ui = RuntimePanelUtils.ScreenToPanel(active.root.panel, new Vector2(point.x, Screen.height-point.y));
            return active.bar.worldBound.Contains(ui) ||
                (active.showedBubble && active.bubble.resolvedStyle.display != DisplayStyle.None && active.bubble.worldBound.Contains(ui));
        }

        public static Rect AboveChat(Rect label)
        {
            if (!active || active.root?.panel == null || !(active.root.layout.height > 0)) return label;
            var factor = Screen.height / active.root.layout.height;
            var barBounds = active.bar.worldBound;
            var overlay = new Rect(barBounds.position * factor, barBounds.size * factor);
            if (overlay.Overlaps(label)) label.y = Mathf.Max(0, overlay.y - label.height - 4);
            return label;
        }

        public static bool IsTyping => active && active.input?.panel?.focusController.focusedElement is VisualElement focus &&
            (focus == active.input || active.input.Contains(focus));

        void OnDisable()
        {
            StopAllCoroutines();
            if (active == this) active = null;
            if (uiObject) Destroy(uiObject);
            if (panel) Destroy(panel);
            if (font) Destroy(font);
        }
    }
}
