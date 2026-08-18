using System.Collections;
using UnityEngine;

namespace LightningForge.Arcade.Game.Backgammon
{
    /// <summary>
    /// A backgammon die: a cube beside the board that spins when you roll it and settles on
    /// its number.
    ///
    /// Unlike the Yahtzee dice these are not thrown. Backgammon rolls happen many times a
    /// game and the number is decided by the rules layer, so a physical throw would add a
    /// wait to every single turn and risk a die that will not settle. Spinning on the spot
    /// gives the same sense of a roll happening, in a fixed length of time, and always
    /// finishes on the number the game actually rolled.
    /// </summary>
    public class BackgammonDie : MonoBehaviour
    {
        public int Value { get; private set; } = 1;

        /// <summary>True while this die is still tumbling.</summary>
        public bool IsSpinning { get; private set; }

        Color faceColour;
        Color spentColour;
        MeshRenderer shell;

        public static BackgammonDie Create(Transform parent, Vector3 localPosition, float size,
            Color face, Color pip, Color spent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Die";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = Vector3.one * size;

            var die = go.AddComponent<BackgammonDie>();
            die.shell = go.GetComponent<MeshRenderer>();
            die.faceColour = face;
            die.spentColour = spent;
            die.shell.sharedMaterial = ArcadeMaterials.Get(face, 0.45f);

            PippedDie.BuildPips(go.transform, pip);
            die.Show(1);
            return die;
        }

        public void Show(int value)
        {
            Value = Mathf.Clamp(value, 1, 6);
            transform.localRotation = PippedDie.RotationShowing(Value);
        }

        /// <summary>
        /// Tumbles for a moment and stops showing <paramref name="finalValue"/>. The spin is
        /// staggered per die by the delay, so a pair does not turn in lockstep.
        /// </summary>
        public IEnumerator Roll(int finalValue, float delay, float duration = 0.55f)
        {
            IsSpinning = true;
            SetSpent(false);

            if (delay > 0f) yield return new WaitForSeconds(delay);

            Quaternion from = transform.localRotation;
            Vector3 axis = Random.onUnitSphere;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                // Fast at first and easing off, so it reads as a die losing momentum rather
                // than a model spinning at a constant rate.
                float speed = Mathf.Lerp(1500f, 90f, t * t);
                transform.localRotation = Quaternion.AngleAxis(speed * Time.deltaTime, axis)
                    * transform.localRotation;

                // Settle onto the answer over the last stretch, so it lands rather than snaps.
                if (t > 0.72f)
                {
                    float settle = Mathf.InverseLerp(0.72f, 1f, t);
                    transform.localRotation = Quaternion.Slerp(
                        transform.localRotation, PippedDie.RotationShowing(finalValue), settle * settle);
                }
                yield return null;
            }

            Show(finalValue);
            IsSpinning = false;
        }

        /// <summary>Dims a die whose number has been used, so what is left to play is obvious.</summary>
        public void SetSpent(bool spent)
        {
            shell.sharedMaterial = ArcadeMaterials.Get(spent ? spentColour : faceColour, 0.45f);
        }
    }
}
