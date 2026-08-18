using System.Collections;
using LightningForge.Chess.Core;
using UnityEngine;

namespace LightningForge.Chess.Game
{
    /// <summary>
    /// Places the camera behind whichever side the local player is on, so your own pieces
    /// are nearest and move away from you. Playing from the opponent's viewpoint is
    /// genuinely disorienting, so this matters as soon as the game goes online.
    ///
    /// The board's square to world mapping is camera independent, so orbiting the view
    /// affects nothing else: picking, highlighting and piece placement are unchanged.
    /// </summary>
    public class BoardCameraRig : MonoBehaviour
    {
        [SerializeField] Camera target;

        [Header("Framing")]
        [SerializeField] float height = 8.5f;
        [SerializeField] float distance = 8.5f;
        [SerializeField] float pitch = 45f;

        [Tooltip("Seconds to swing between viewpoints. Zero snaps.")]
        [SerializeField] float transitionSeconds = 0.5f;

        PieceColor viewpoint = PieceColor.White;
        Coroutine transition;

        public PieceColor Viewpoint => viewpoint;

        void Awake()
        {
            if (target == null) target = Camera.main;
        }

        void Start()
        {
            Apply(viewpoint, instant: true);
        }

        /// <summary>Moves the camera behind <paramref name="side"/>.</summary>
        public void SetViewpoint(PieceColor side)
        {
            if (viewpoint == side && transition == null) return;
            viewpoint = side;
            Apply(side, instant: !Application.isPlaying || transitionSeconds <= 0f);
        }

        void Apply(PieceColor side, bool instant)
        {
            if (target == null) target = Camera.main;
            if (target == null) return;

            GetPose(side, out Vector3 position, out Quaternion rotation);

            if (transition != null)
            {
                StopCoroutine(transition);
                transition = null;
            }

            if (instant)
            {
                target.transform.SetPositionAndRotation(position, rotation);
                return;
            }

            transition = StartCoroutine(Swing(position, rotation));
        }

        void GetPose(PieceColor side, out Vector3 position, out Quaternion rotation)
        {
            // White sits at negative Z looking up the board; Black is the mirror image.
            float sign = side == PieceColor.White ? -1f : 1f;
            position = new Vector3(0f, height, distance * sign);
            rotation = Quaternion.Euler(pitch, side == PieceColor.White ? 0f : 180f, 0f);
        }

        IEnumerator Swing(Vector3 toPosition, Quaternion toRotation)
        {
            Vector3 fromPosition = target.transform.position;
            Quaternion fromRotation = target.transform.rotation;
            float elapsed = 0f;

            while (elapsed < transitionSeconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / transitionSeconds);
                float eased = t * t * (3f - 2f * t);
                target.transform.SetPositionAndRotation(
                    Vector3.Slerp(fromPosition, toPosition, eased),
                    Quaternion.Slerp(fromRotation, toRotation, eased));
                yield return null;
            }

            target.transform.SetPositionAndRotation(toPosition, toRotation);
            transition = null;
        }
    }
}
