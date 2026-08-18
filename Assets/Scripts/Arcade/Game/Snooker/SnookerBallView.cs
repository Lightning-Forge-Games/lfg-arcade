using System;
using LightningForge.Arcade.Core.Snooker;
using UnityEngine;

namespace LightningForge.Arcade.Game.Snooker
{
    /// <summary>
    /// One ball on the table.
    ///
    /// Reports its own collisions and pottings rather than the table polling for them,
    /// because the rules care about the order things happened in: which ball the cue ball
    /// touched first decides whether the shot was a foul, and that is only knowable at the
    /// moment of contact.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public class SnookerBallView : MonoBehaviour
    {
        public SnookerBall Ball;
        public Vector3 SpotPosition;
        public bool IsPotted { get; private set; }

        /// <summary>Raised when the cue ball touches an object ball. Sender, then struck.</summary>
        public event Action<SnookerBallView, SnookerBallView> Contacted;

        /// <summary>Raised when this ball drops into a pocket.</summary>
        public event Action<SnookerBallView> Potted;

        Rigidbody body;

        public Rigidbody Body => body;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
        }

        public bool IsAtRest =>
            body == null || body.IsSleeping()
            || (body.linearVelocity.sqrMagnitude < 0.0015f
                && body.angularVelocity.sqrMagnitude < 0.05f);

        public void Strike(Vector3 impulse)
        {
            body.WakeUp();
            body.AddForce(impulse, ForceMode.Impulse);
        }

        public void Halt()
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        public void PlaceAt(Vector3 position)
        {
            Halt();
            transform.position = position;
            IsPotted = false;
            gameObject.SetActive(true);
        }

        public void Pocket()
        {
            if (IsPotted) return;
            IsPotted = true;
            Halt();
            gameObject.SetActive(false);
            Potted?.Invoke(this);
        }

        void OnCollisionEnter(Collision collision)
        {
            // Only the cue ball's first contact matters, so only it reports.
            if (Ball != SnookerBall.Cue) return;

            var other = collision.collider.GetComponent<SnookerBallView>();
            if (other != null) Contacted?.Invoke(this, other);
        }
    }

    /// <summary>Marks a pocket's trigger volume.</summary>
    public class SnookerPocket : MonoBehaviour
    {
        void OnTriggerEnter(Collider other)
        {
            var ball = other.GetComponent<SnookerBallView>();
            if (ball != null) ball.Pocket();
        }
    }
}
