using LightningForge.Arcade.Game;
using LightningForge.Arcade.Game.Chess;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightningForge.Arcade.Net
{
    /// <summary>
    /// Lobby controls: host a match, join by code, and share the invite link.
    ///
    /// Separate from the in-game HUD because that lives in the Game assembly, which must
    /// not depend on networking. This draws into its own UIDocument layered on top.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ArcadeOnlineHud : MonoBehaviour
    {
        [SerializeField] ArcadeSession session;
        [SerializeField] ArcadeShell shell;

        UIDocument document;
        VisualElement panel;
        Label statusLabel;
        TextField inviteField;
        TextField joinField;
        Button hostButton;
        Button joinButton;
        Button leaveButton;

        void Awake()
        {
            document = GetComponent<UIDocument>();
            if (session == null) session = GetComponent<ArcadeSession>();
            if (shell == null) shell = FindFirstObjectByType<ArcadeShell>();
        }

        void OnEnable()
        {
            Build();
            if (session != null) session.Changed += Refresh;
            if (shell != null)
            {
                shell.ModeChosen += OnModeChosen;
                shell.Quit += OnQuit;
            }
            Refresh();
        }

        /// <summary>
        /// An invite link should land the player in that match without making them pick a
        /// mode first.
        ///
        /// This waits for Start rather than acting in OnEnable. The arcade shell shows itself
        /// from its own OnEnable, and if that runs second it puts the menu straight back
        /// over a match that is already being joined. The player then has to press Play
        /// Online, which restarts the game underneath the live connection.
        /// </summary>
        void Start()
        {
            if (session == null) return;

            string code = session.CodeFromUrl;
            if (string.IsNullOrEmpty(code)) return;

            // Links made before the arcade existed carry no game, and those were all chess.
            if (!session.TryGetGameFromUrl(out ArcadeGameId id)) id = ArcadeGameId.Chess;

            session.Game = id;
            if (shell != null) shell.SkipToOnline(id);
            session.JoinMatch(code);
            Refresh();
        }

        void OnDisable()
        {
            if (session != null) session.Changed -= Refresh;
            if (shell != null)
            {
                shell.ModeChosen -= OnModeChosen;
                shell.Quit -= OnQuit;
            }
        }

        void OnModeChosen(GameMode mode) => Refresh();

        /// <summary>Leaving to the menu must also leave the Photon match.</summary>
        void OnQuit()
        {
            if (session != null && session.IsConnected) session.Leave();
        }

        /// <summary>The lobby is only meaningful once online play has been chosen.</summary>
        bool ShouldShow =>
            shell == null || shell.Mode == GameMode.Online;

        void Build()
        {
            VisualElement root = document.rootVisualElement;
            if (root == null) return;
            root.Clear();
            root.pickingMode = PickingMode.Ignore;

            panel = new VisualElement();
            panel.style.position = Position.Absolute;
            panel.style.left = 22f;
            panel.style.bottom = 22f;
            panel.style.minWidth = 250f;
            panel.style.backgroundColor = new Color(0.05f, 0.05f, 0.06f, 0.82f);
            panel.style.paddingLeft = 14f;
            panel.style.paddingRight = 14f;
            panel.style.paddingTop = 12f;
            panel.style.paddingBottom = 12f;
            Round(panel, 6f);
            Border(panel, new Color(0.38f, 0.30f, 0.20f, 1f), 1f);
            root.Add(panel);

            statusLabel = new Label("Offline. Both sides play on this device.");
            statusLabel.style.color = new Color(0.90f, 0.88f, 0.82f);
            statusLabel.style.fontSize = 13f;
            statusLabel.style.whiteSpace = WhiteSpace.Normal;
            statusLabel.style.marginBottom = 8f;
            panel.Add(statusLabel);

            hostButton = new Button(() =>
            {
                if (session == null) return;
                if (shell != null) session.Game = shell.CurrentGame;
                session.CreateMatch();
            });
            hostButton.text = "Play Online";
            Style(hostButton);
            panel.Add(hostButton);

            var joinRow = new VisualElement();
            joinRow.style.flexDirection = FlexDirection.Row;
            joinRow.style.marginTop = 6f;
            panel.Add(joinRow);

            joinField = new TextField();
            joinField.style.flexGrow = 1f;
            joinField.style.marginRight = 6f;
            joinField.value = string.Empty;
            joinRow.Add(joinField);

            joinButton = new Button(() =>
            {
                if (session == null) return;
                if (shell != null) session.Game = shell.CurrentGame;
                session.JoinMatch(joinField.value);
            });
            joinButton.text = "Join";
            Style(joinButton);
            joinButton.style.marginTop = 0f;
            joinRow.Add(joinButton);

            // Read only and selectable: WebGL has no reliable clipboard API from managed
            // code, so the player copies the link themselves.
            inviteField = new TextField("Invite");
            inviteField.isReadOnly = true;
            inviteField.style.marginTop = 8f;
            inviteField.style.display = DisplayStyle.None;
            panel.Add(inviteField);

            leaveButton = new Button(() => { if (session != null) session.Leave(); });
            leaveButton.text = "Leave Match";
            Style(leaveButton);
            leaveButton.style.display = DisplayStyle.None;
            panel.Add(leaveButton);
        }

        static void Style(Button b)
        {
            b.style.paddingLeft = 14f;
            b.style.paddingRight = 14f;
            b.style.paddingTop = 7f;
            b.style.paddingBottom = 7f;
            b.style.marginTop = 4f;
            b.style.fontSize = 13f;
            b.style.color = new Color(0.92f, 0.90f, 0.84f);
            b.style.backgroundColor = new Color(0.13f, 0.12f, 0.12f, 1f);
            Round(b, 4f);
            Border(b, new Color(0.34f, 0.27f, 0.19f, 1f), 1f);
            UiButtonFeedback.Apply(b);
        }

        static void Round(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r;
            e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r;
            e.style.borderBottomRightRadius = r;
        }

        static void Border(VisualElement e, Color c, float w)
        {
            e.style.borderTopColor = c; e.style.borderBottomColor = c;
            e.style.borderLeftColor = c; e.style.borderRightColor = c;
            e.style.borderTopWidth = w; e.style.borderBottomWidth = w;
            e.style.borderLeftWidth = w; e.style.borderRightWidth = w;
        }

        public void Refresh()
        {
            if (session == null || statusLabel == null) return;

            if (panel != null) panel.style.display = ShouldShow ? DisplayStyle.Flex : DisplayStyle.None;
            if (!ShouldShow) return;

            bool connected = session.IsConnected && !string.IsNullOrEmpty(session.MatchCode);

            if (session.IsConnecting)
            {
                statusLabel.text = "Connecting to match " + session.MatchCode + "...";
            }
            else if (connected)
            {
                ArcadeNetLink link = FindFirstObjectByType<ArcadeNetLink>();
                ArcadeGameInfo info = ArcadeCatalog.Get(session.Game);
                string side = link == null
                    ? "waiting for opponent"
                    : "you are " + (info == null
                        ? (link.IsFirstSeat ? "White" : "Black")
                        : (link.IsFirstSeat ? info.FirstSeat : info.SecondSeat));
                statusLabel.text = "Match " + session.MatchCode + " (" + side + ")";
            }
            else if (!string.IsNullOrEmpty(session.LastError))
            {
                statusLabel.text = "Connection failed: " + session.LastError;
            }
            else
            {
                statusLabel.text = "Offline. Both sides play on this device.";
            }

            bool showLobby = !connected && !session.IsConnecting;
            hostButton.style.display = showLobby ? DisplayStyle.Flex : DisplayStyle.None;
            joinField.parent.style.display = showLobby ? DisplayStyle.Flex : DisplayStyle.None;

            leaveButton.style.display = connected ? DisplayStyle.Flex : DisplayStyle.None;

            string invite = session.InviteUrl;
            bool showInvite = connected && !string.IsNullOrEmpty(invite);
            inviteField.style.display = showInvite ? DisplayStyle.Flex : DisplayStyle.None;
            if (showInvite) inviteField.value = invite;
        }

        void Update()
        {
            // The side label depends on the link object, which arrives a frame or two after
            // the connection completes, so keep the panel in step without polling hard.
            if (session != null && session.IsConnected && Time.frameCount % 30 == 0) Refresh();
        }
    }
}
