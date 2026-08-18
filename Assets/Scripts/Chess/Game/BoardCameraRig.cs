using System;
using System.Collections;
using LightningForge.Chess.Core;
using UnityEngine;

namespace LightningForge.Chess.Game
{
    /// <summary>How steeply the board is viewed.</summary>
    public enum BoardViewStyle
    {
        /// <summary>Low, cinematic angle. Looks best, but foreshortens the far ranks.</summary>
        Angled,

        /// <summary>Steep and near overhead. Less dramatic, far easier to read.</summary>
        Overhead
    }

    /// <summary>
    /// Places the camera behind whichever side the local player is on, and offers a steeper
    /// view for when clarity matters more than looks.
    ///
    /// The low angle foreshortens squares near the opponent's back rank, which makes it
    /// genuinely hard to judge what is attacking what. The overhead style trades some of
    /// the drama for an even, readable board.
    ///
    /// The board's square to world mapping is camera independent, so moving the view
    /// affects nothing else: picking, highlighting and piece placement are unchanged.
    /// </summary>
    public class BoardCameraRig : MonoBehaviour
    {
        [SerializeField] Camera target;

        [Header("Angled view")]
        [SerializeField] float angledHeight = 8.5f;
        [SerializeField] float angledDistance = 8.5f;
        [SerializeField] float angledPitch = 45f;
        [SerializeField] float angledFov = 45f;

        [Header("Overhead view")]
        [SerializeField] float overheadHeight = 13.5f;
        [SerializeField] float overheadDistance = 4.6f;
        [SerializeField] float overheadPitch = 71f;
        [SerializeField] float overheadFov = 38f;

        [Tooltip("Seconds to swing between viewpoints. Zero snaps.")]
        [SerializeField] float transitionSeconds = 0.45f;

        // Serialised so the chosen view survives an editor domain reload rather than
        // silently snapping back to the default mid-session.
        [SerializeField, HideInInspector] PieceColor viewpoint = PieceColor.White;
        [SerializeField, HideInInspector] BoardViewStyle style = BoardViewStyle.Angled;

        Coroutine transition;

        public PieceColor Viewpoint => viewpoint;
        public BoardViewStyle Style => style;

        /// <summary>Raised whenever the view changes, so UI can keep its label in step.</summary>
        public event Action<BoardViewStyle> StyleChanged;

        /// <summary>Raised when the side being viewed from changes.</summary>
        public event Action<PieceColor> ViewpointChanged;

        void Awake()
        {
            if (target == null) target = Camera.main;
        }

        void Start()
        {
            Apply(true);
        }

        public void SetViewpoint(PieceColor side)
        {
            if (viewpoint == side && transition == null) return;
            viewpoint = side;
            Apply(!Application.isPlaying || transitionSeconds <= 0f);

            Action<PieceColor> handler = ViewpointChanged;
            if (handler != null) handler(viewpoint);
        }

        public void SetStyle(BoardViewStyle newStyle)
        {
            if (style == newStyle) return;
            style = newStyle;
            Apply(!Application.isPlaying || transitionSeconds <= 0f);

            Action<BoardViewStyle> handler = StyleChanged;
            if (handler != null) handler(style);
        }

        public void ToggleStyle()
        {
            SetStyle(style == BoardViewStyle.Angled ? BoardViewStyle.Overhead : BoardViewStyle.Angled);
        }

        void Apply(bool instant)
        {
            if (target == null) target = Camera.main;
            if (target == null) return;

            GetPose(out Vector3 position, out Quaternion rotation, out float fov);

            if (transition != null)
            {
                StopCoroutine(transition);
                transition = null;
            }

            if (instant)
            {
                target.transform.SetPositionAndRotation(position, rotation);
                target.fieldOfView = fov;
                return;
            }

            transition = StartCoroutine(Swing(position, rotation, fov));
        }

        void GetPose(out Vector3 position, out Quaternion rotation, out float fov)
        {
            bool angled = style == BoardViewStyle.Angled;
            float height = angled ? angledHeight : overheadHeight;
            float distance = angled ? angledDistance : overheadDistance;
            float pitch = angled ? angledPitch : overheadPitch;
            fov = angled ? angledFov : overheadFov;

            // White sits at negative Z looking up the board; Black is the mirror image.
            float sign = viewpoint == PieceColor.White ? -1f : 1f;
            position = new Vector3(0f, height, distance * sign);
            rotation = Quaternion.Euler(pitch, viewpoint == PieceColor.White ? 0f : 180f, 0f);
        }

        IEnumerator Swing(Vector3 toPosition, Quaternion toRotation, float toFov)
        {
            Vector3 fromPosition = target.transform.position;
            Quaternion fromRotation = target.transform.rotation;
            float fromFov = target.fieldOfView;
            float elapsed = 0f;

            while (elapsed < transitionSeconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / transitionSeconds);
                float eased = t * t * (3f - 2f * t);
                target.transform.SetPositionAndRotation(
                    Vector3.Slerp(fromPosition, toPosition, eased),
                    Quaternion.Slerp(fromRotation, toRotation, eased));
                target.fieldOfView = Mathf.Lerp(fromFov, toFov, eased);
                yield return null;
            }

            target.transform.SetPositionAndRotation(toPosition, toRotation);
            target.fieldOfView = toFov;
            transition = null;
        }
    }
}
