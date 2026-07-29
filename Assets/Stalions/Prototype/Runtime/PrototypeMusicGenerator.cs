using UnityEngine;

namespace Stalions.Prototype
{
    public static class PrototypeMusicGenerator
    {
        public const int BeatsPerLoop = 8;
        public const int ChannelCount = 2;

        private static readonly float[] BassNotes =
        {
            73.416f,
            73.416f,
            87.307f,
            77.782f,
            65.406f,
            65.406f,
            58.270f,
            65.406f
        };

        private static readonly float[] ArpeggioNotes =
        {
            293.665f,
            440.000f,
            349.228f,
            440.000f,
            293.665f,
            440.000f,
            349.228f,
            523.251f,
            233.082f,
            349.228f,
            293.665f,
            349.228f,
            261.626f,
            391.995f,
            329.628f,
            391.995f
        };

        public static float LoopDuration(float beatInterval)
        {
            return Mathf.Max(0.01f, beatInterval) * BeatsPerLoop;
        }

        public static AudioClip CreateLoop(
            string clipName,
            int sampleRate,
            float beatInterval)
        {
            var samples = GenerateStereoLoop(
                sampleRate,
                beatInterval);
            var clip = AudioClip.Create(
                clipName,
                samples.Length / ChannelCount,
                ChannelCount,
                sampleRate,
                false);
            clip.SetData(samples, 0);
            return clip;
        }

        public static float[] GenerateStereoLoop(
            int sampleRate,
            float beatInterval)
        {
            sampleRate = Mathf.Max(8000, sampleRate);
            beatInterval = Mathf.Max(0.1f, beatInterval);
            var duration = LoopDuration(beatInterval);
            var frameCount = Mathf.Max(
                1,
                Mathf.RoundToInt(sampleRate * duration));
            var output = new float[frameCount * ChannelCount];

            for (var frame = 0; frame < frameCount; frame++)
            {
                var time = frame / (float)sampleRate;
                var beatPosition = time / beatInterval;
                var beatIndex =
                    Mathf.FloorToInt(beatPosition) % BeatsPerLoop;
                var timeInBeat =
                    time - Mathf.Floor(beatPosition) * beatInterval;
                var halfBeat = beatInterval * 0.5f;
                var halfStepIndex =
                    Mathf.FloorToInt(time / halfBeat) %
                    ArpeggioNotes.Length;
                var timeInHalfStep =
                    time - Mathf.Floor(time / halfBeat) * halfBeat;

                var kick = Kick(timeInBeat, beatIndex);
                var snare = beatIndex % 2 == 1
                    ? Snare(frame, timeInBeat, sampleRate)
                    : 0f;
                var hat = Hat(
                    frame,
                    timeInHalfStep,
                    halfStepIndex);
                var sidechain =
                    0.58f +
                    0.42f *
                    (1f - Mathf.Exp(-9f * timeInBeat));
                var bass = Bass(
                    timeInBeat,
                    beatInterval,
                    BassNotes[beatIndex]) *
                    sidechain;
                var arpeggio = Arpeggio(
                    timeInHalfStep,
                    halfBeat,
                    ArpeggioNotes[halfStepIndex]) *
                    sidechain;
                var pad = Pad(
                    time,
                    duration,
                    beatPosition) *
                    sidechain;

                var hatPan = halfStepIndex % 2 == 0
                    ? -0.16f
                    : 0.16f;
                var arpeggioPan = halfStepIndex % 4 < 2
                    ? 0.2f
                    : -0.2f;
                var center =
                    kick * 0.72f +
                    snare * 0.32f +
                    bass * 0.34f +
                    pad * 0.17f;
                var left =
                    center +
                    hat * (0.15f - hatPan) +
                    arpeggio * (0.18f - arpeggioPan);
                var right =
                    center +
                    hat * (0.15f + hatPan) +
                    arpeggio * (0.18f + arpeggioPan);

                var loopFade = LoopEdgeFade(time, duration);
                output[frame * ChannelCount] =
                    SoftLimit(left * loopFade);
                output[frame * ChannelCount + 1] =
                    SoftLimit(right * loopFade);
            }

            return output;
        }

        private static float Kick(float time, int beatIndex)
        {
            var envelope = Mathf.Exp(-13f * time);
            var frequency =
                47f +
                74f * Mathf.Exp(-18f * time);
            var phase =
                2f *
                Mathf.PI *
                frequency *
                time;
            var accent = beatIndex == 0 || beatIndex == 4
                ? 1f
                : 0.78f;
            var body = Mathf.Sin(phase);
            var click =
                Mathf.Sin(2f * Mathf.PI * 1350f * time) *
                Mathf.Exp(-75f * time) *
                0.16f;
            return (body * envelope + click) * accent;
        }

        private static float Snare(
            int frame,
            float time,
            int sampleRate)
        {
            var noise =
                HashNoise(frame) -
                HashNoise(Mathf.Max(0, frame - 1)) * 0.68f;
            var noiseEnvelope = Mathf.Exp(-15f * time);
            var body =
                Mathf.Sin(2f * Mathf.PI * 185f * time) *
                Mathf.Exp(-22f * time);
            return noise * noiseEnvelope * 0.72f +
                   body * 0.32f;
        }

        private static float Hat(
            int frame,
            float time,
            int halfStepIndex)
        {
            var noise =
                HashNoise(frame * 3 + 17) -
                HashNoise(frame * 3 + 16);
            var envelope = Mathf.Exp(-42f * time);
            var accent = halfStepIndex % 2 == 0
                ? 0.72f
                : 0.42f;
            return noise * envelope * accent;
        }

        private static float Bass(
            float time,
            float beatInterval,
            float frequency)
        {
            var attack = Mathf.Clamp01(time / 0.018f);
            var release = Mathf.Clamp01(
                (beatInterval - time) / 0.08f);
            var envelope =
                attack *
                release *
                Mathf.Exp(-0.8f * time);
            var phase = 2f * Mathf.PI * frequency * time;
            return (
                    Mathf.Sin(phase) +
                    Mathf.Sin(phase * 2f) * 0.22f +
                    Mathf.Sin(phase * 3f) * 0.08f) *
                envelope;
        }

        private static float Arpeggio(
            float time,
            float stepDuration,
            float frequency)
        {
            var attack = Mathf.Clamp01(time / 0.012f);
            var release = Mathf.Clamp01(
                (stepDuration - time) / 0.055f);
            var envelope =
                attack *
                release *
                Mathf.Exp(-5.5f * time);
            var phase = 2f * Mathf.PI * frequency * time;
            return (
                    Mathf.Sin(phase) +
                    Mathf.Sin(phase * 2.01f) * 0.18f) *
                envelope;
        }

        private static float Pad(
            float time,
            float duration,
            float beatPosition)
        {
            float root;
            float third;
            float fifth;
            if (beatPosition < 4f)
            {
                root = 146.832f;
                third = 174.614f;
                fifth = 220f;
            }
            else if (beatPosition < 6f)
            {
                root = 116.541f;
                third = 146.832f;
                fifth = 174.614f;
            }
            else
            {
                root = 130.813f;
                third = 164.814f;
                fifth = 195.998f;
            }

            var slowPulse =
                0.78f +
                Mathf.Sin(2f * Mathf.PI * time / duration) *
                0.08f;
            return (
                    Mathf.Sin(2f * Mathf.PI * root * time) +
                    Mathf.Sin(2f * Mathf.PI * third * time) * 0.72f +
                    Mathf.Sin(2f * Mathf.PI * fifth * time) * 0.56f) /
                2.28f *
                slowPulse;
        }

        private static float LoopEdgeFade(
            float time,
            float duration)
        {
            const float fadeDuration = 0.018f;
            var fadeIn = Mathf.Clamp01(time / fadeDuration);
            var fadeOut = Mathf.Clamp01(
                (duration - time) / fadeDuration);
            return Mathf.Min(fadeIn, fadeOut);
        }

        private static float HashNoise(int index)
        {
            unchecked
            {
                var value = (uint)index;
                value ^= value << 13;
                value ^= value >> 17;
                value ^= value << 5;
                return value / (float)uint.MaxValue * 2f - 1f;
            }
        }

        private static float SoftLimit(float value)
        {
            return Mathf.Clamp(
                value / (1f + Mathf.Abs(value)) * 1.16f,
                -0.98f,
                0.98f);
        }
    }
}
