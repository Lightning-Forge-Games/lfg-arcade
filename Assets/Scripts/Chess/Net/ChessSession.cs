using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;

namespace LightningForge.Chess.Net
{
    /// <summary>
    /// Starts or joins a Fusion Shared Mode session named after a short match code, and
    /// spawns the link that relays moves.
    ///
    /// Shared Mode is used rather than Host or Server because neither player should need
    /// to be authoritative: the rules run identically on both clients, so Photon only has
    /// to keep the two of them in the same room.
    /// </summary>
    public class ChessSession : MonoBehaviour
    {
        [SerializeField] NetworkObject linkPrefab;

        [Tooltip("Query string key used to carry the match code in a shared link.")]
        [SerializeField] string urlParameter = "match";

        NetworkRunner runner;

        public string MatchCode { get; private set; }
        public bool IsConnecting { get; private set; }
        public bool IsConnected => runner != null && runner.IsRunning;
        public string LastError { get; private set; }

        public event Action Changed;

        /// <summary>The code carried in the page URL, if this client opened an invite link.</summary>
        public string CodeFromUrl => ReadCodeFromUrl(urlParameter);

        /// <summary>Full invite URL for the current match, or empty when not in one.</summary>
        public string InviteUrl
        {
            get
            {
                if (string.IsNullOrEmpty(MatchCode)) return string.Empty;
                string baseUrl = StripQuery(Application.absoluteURL);
                if (string.IsNullOrEmpty(baseUrl)) return MatchCode;
                return baseUrl + "?" + urlParameter + "=" + MatchCode;
            }
        }

        /// <summary>Hosts a new match under a freshly generated code.</summary>
        public async void CreateMatch()
        {
            await Connect(GenerateCode());
        }

        /// <summary>Joins an existing match by code. Case insensitive.</summary>
        public async void JoinMatch(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return;
            await Connect(code.Trim().ToUpperInvariant());
        }

        public async void Leave()
        {
            // Detach before awaiting. Shutdown takes a moment, IsConnected goes false as
            // soon as it starts, and the lobby offers Play Online again the instant it
            // does. A runner cannot be started twice, so anything still holding this one
            // would fail on the next match.
            NetworkRunner leaving = runner;
            runner = null;
            MatchCode = null;
            LastError = null;
            Raise();

            if (leaving != null) await leaving.Shutdown();
        }

        async Task Connect(string code)
        {
            if (IsConnecting) return;

            IsConnecting = true;
            LastError = null;
            MatchCode = code;
            Raise();

            try
            {
                if (runner == null)
                {
                    // Fusion destroys the runner's GameObject when the session ends, and it
                    // does that on an unexpected disconnect as well as on Leave. Sharing this
                    // object would take the lobby UI and this component down with it, leaving
                    // no way back into a match, so the runner gets one of its own. It is left
                    // at the root because Fusion marks it DontDestroyOnLoad, which only
                    // applies to root objects.
                    runner = new GameObject("Chess Network Runner").AddComponent<NetworkRunner>();
                }
                runner.ProvideInput = false;

                // No SceneManager is supplied on purpose. The board already exists in the
                // loaded scene, and letting Fusion manage scenes would reload and rebuild it.
                var args = new StartGameArgs
                {
                    GameMode = GameMode.Shared,
                    SessionName = code,
                    PlayerCount = 2
                };

                StartGameResult result = await runner.StartGame(args);
                if (!result.Ok)
                {
                    LastError = result.ShutdownReason.ToString();
                    Debug.LogError("Fusion failed to start: " + LastError);
                    MatchCode = null;
                    return;
                }

                // Exactly one client should create the link. In Shared Mode the master
                // client is the natural choice and is well defined for both players.
                if (runner.IsSharedModeMasterClient)
                {
                    runner.Spawn(linkPrefab, Vector3.zero, Quaternion.identity, runner.LocalPlayer);
                }
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogError("Fusion connect threw: " + e);
                MatchCode = null;
            }
            finally
            {
                IsConnecting = false;
                Raise();
            }
        }

        void Raise()
        {
            Action handler = Changed;
            if (handler != null) handler();
        }

        /// <summary>
        /// Four characters from an alphabet with look-alikes removed, so a code can be read
        /// aloud without confusing O for 0 or I for 1.
        /// </summary>
        static string GenerateCode()
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var sb = new StringBuilder(4);
            for (int i = 0; i < 4; i++) sb.Append(alphabet[UnityEngine.Random.Range(0, alphabet.Length)]);
            return sb.ToString();
        }

        static string StripQuery(string url)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            int q = url.IndexOf('?');
            return q >= 0 ? url.Substring(0, q) : url;
        }

        /// <summary>
        /// Reads ?match=CODE from the page URL. Returns empty off the web, where
        /// absoluteURL is a file path with no query string.
        /// </summary>
        static string ReadCodeFromUrl(string key)
        {
            string url = Application.absoluteURL;
            if (string.IsNullOrEmpty(url)) return string.Empty;

            int q = url.IndexOf('?');
            if (q < 0 || q == url.Length - 1) return string.Empty;

            string query = url.Substring(q + 1);
            string[] pairs = query.Split('&');
            foreach (string pair in pairs)
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                if (!string.Equals(pair.Substring(0, eq), key, StringComparison.OrdinalIgnoreCase)) continue;

                string value = pair.Substring(eq + 1);
                return Uri.UnescapeDataString(value).ToUpperInvariant();
            }
            return string.Empty;
        }
    }
}
