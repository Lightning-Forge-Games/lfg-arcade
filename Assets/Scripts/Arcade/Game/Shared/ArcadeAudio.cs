using UnityEngine;

namespace LightningForge.Arcade.Game
{
    /// <summary>
    /// The arcade's sounds.
    ///
    /// Clips are looked for in Resources first, so a generated or recorded file simply
    /// replaces the built in one by being dropped in the right place. When none is found the
    /// sound is synthesised in code instead, which means the game is never silent waiting on
    /// an asset, and the fallback is good enough to design against.
    ///
    /// Synthesised sounds are built once and cached. Generating a couple of seconds of audio
    /// is cheap, but not cheap enough to do on the frame a die lands.
    /// </summary>
    public static class ArcadeAudio
    {
        const int SampleRate = 44100;

        static AudioClip rattle;
        static AudioClip knock;

        /// <summary>
        /// A continuous rattle of dice in a wooden cup, meant to be looped and have its
        /// volume and pitch driven by how hard the cup is being swirled.
        /// </summary>
        public static AudioClip Rattle()
        {
            if (rattle != null) return rattle;

            rattle = Resources.Load<AudioClip>("Audio/DiceRattle");
            if (rattle != null) return rattle;

            // A rattle is a crowd of little impacts, so that is what this builds: short
            // decaying bursts of noise at irregular intervals, filtered to take the hiss off
            // so it reads as wood rather than static.
            const float seconds = 1.6f;
            int length = Mathf.RoundToInt(SampleRate * seconds);
            var samples = new float[length];
            var random = new System.Random(4242);

            int next = 0;
            while (next < length)
            {
                // Irregular spacing. Evenly spaced impacts sound like a machine.
                next += random.Next(500, 2600);
                if (next >= length) break;

                float amplitude = 0.25f + (float)random.NextDouble() * 0.55f;
                int decay = random.Next(700, 2100);
                float tone = 0.35f + (float)random.NextDouble() * 0.45f;

                float low = 0f;
                for (int i = 0; i < decay && next + i < length; i++)
                {
                    float envelope = Mathf.Exp(-i / (float)decay * 6f);
                    float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                    // One pole low pass. Wood knocks are dull; raw noise is not.
                    low += (noise - low) * tone;
                    samples[next + i] += low * envelope * amplitude;
                }
            }

            Normalise(samples, 0.55f);
            FadeEnds(samples, 900);

            rattle = AudioClip.Create("DiceRattle", length, 1, SampleRate, false);
            rattle.SetData(samples, 0);
            return rattle;
        }

        /// <summary>A single knock, for a die landing or a piece being put down.</summary>
        public static AudioClip Knock()
        {
            if (knock != null) return knock;

            knock = Resources.Load<AudioClip>("Audio/Knock");
            if (knock != null) return knock;

            int length = Mathf.RoundToInt(SampleRate * 0.13f);
            var samples = new float[length];
            var random = new System.Random(99);

            float low = 0f;
            for (int i = 0; i < length; i++)
            {
                float envelope = Mathf.Exp(-i / (float)length * 11f);
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.4f;
                // A little tone under the noise gives it a body rather than a click.
                float body = Mathf.Sin(i / (float)SampleRate * 2f * Mathf.PI * 190f) * 0.4f;
                samples[i] = (low + body) * envelope;
            }

            Normalise(samples, 0.7f);
            knock = AudioClip.Create("Knock", length, 1, SampleRate, false);
            knock.SetData(samples, 0);
            return knock;
        }

        static void Normalise(float[] samples, float peak)
        {
            float loudest = 0f;
            foreach (float sample in samples) loudest = Mathf.Max(loudest, Mathf.Abs(sample));
            if (loudest < 0.0001f) return;

            float scale = peak / loudest;
            for (int i = 0; i < samples.Length; i++) samples[i] *= scale;
        }

        /// <summary>Silences the very start and end, so a looping clip does not click.</summary>
        static void FadeEnds(float[] samples, int fade)
        {
            fade = Mathf.Min(fade, samples.Length / 2);
            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;
                samples[i] *= t;
                samples[samples.Length - 1 - i] *= t;
            }
        }

        /// <summary>Attaches a source set up for arcade sounds: no 3D falloff, no autoplay.</summary>
        public static AudioSource AddSource(GameObject target, AudioClip clip, bool loop)
        {
            var source = target.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = loop;
            source.playOnAwake = false;
            // Flat 2D. The camera sits close and fixed, so distance falloff would only make
            // the same action quieter in some games than others for no reason.
            source.spatialBlend = 0f;
            source.volume = 0f;
            return source;
        }

        /// <summary>
        /// Makes sure something can be heard at all. A scene with no AudioListener plays
        /// nothing and says nothing about why.
        /// </summary>
        public static void EnsureListener()
        {
            if (Object.FindFirstObjectByType<AudioListener>() != null) return;

            Camera camera = Camera.main;
            if (camera != null) camera.gameObject.AddComponent<AudioListener>();
        }
    }
}
