using System;
using System.IO;
using UnityEngine;

namespace ShadowingTutor
{
    /// <summary>
    /// Encodes float audio samples to 16-bit PCM WAV format.
    /// </summary>
    public static class WavEncoder
    {
        private const int BITS_PER_SAMPLE = 16;
        private const int CHANNELS = 1;  // Mono

        /// <summary>
        /// Encode float samples to WAV byte array
        /// </summary>
        /// <param name="samples">Audio samples (-1.0 to 1.0)</param>
        /// <param name="sampleRate">Sample rate in Hz</param>
        /// <returns>WAV file bytes</returns>
        public static byte[] Encode(float[] samples, int sampleRate)
        {
            if (samples == null || samples.Length == 0)
            {
                Debug.LogError("[WavEncoder] No samples to encode");
                return new byte[0];
            }

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                int byteRate = sampleRate * CHANNELS * (BITS_PER_SAMPLE / 8);
                int blockAlign = CHANNELS * (BITS_PER_SAMPLE / 8);
                int dataSize = samples.Length * (BITS_PER_SAMPLE / 8);

                // RIFF header
                writer.Write(new char[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataSize);  // File size - 8
                writer.Write(new char[] { 'W', 'A', 'V', 'E' });

                // fmt subchunk
                writer.Write(new char[] { 'f', 'm', 't', ' ' });
                writer.Write(16);  // Subchunk size (16 for PCM)
                writer.Write((short)1);  // Audio format (1 = PCM)
                writer.Write((short)CHANNELS);
                writer.Write(sampleRate);
                writer.Write(byteRate);
                writer.Write((short)blockAlign);
                writer.Write((short)BITS_PER_SAMPLE);

                // data subchunk
                writer.Write(new char[] { 'd', 'a', 't', 'a' });
                writer.Write(dataSize);

                // Convert float samples to 16-bit PCM
                foreach (float sample in samples)
                {
                    // Clamp to [-1, 1] and convert to 16-bit
                    float clamped = Mathf.Clamp(sample, -1f, 1f);
                    short pcm = (short)(clamped * 32767f);
                    writer.Write(pcm);
                }

                return stream.ToArray();
            }
        }

        /// <summary>
        /// Encode AudioClip to WAV byte array
        /// </summary>
        public static byte[] EncodeClip(AudioClip clip)
        {
            if (clip == null)
            {
                Debug.LogError("[WavEncoder] Null AudioClip");
                return new byte[0];
            }

            float[] samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);

            // If stereo, convert to mono by averaging channels
            if (clip.channels > 1)
            {
                float[] mono = new float[clip.samples];
                for (int i = 0; i < clip.samples; i++)
                {
                    float sum = 0f;
                    for (int c = 0; c < clip.channels; c++)
                    {
                        sum += samples[i * clip.channels + c];
                    }
                    mono[i] = sum / clip.channels;
                }
                samples = mono;
            }

            return Encode(samples, clip.frequency);
        }

        /// <summary>
        /// Get the duration of audio samples in seconds
        /// </summary>
        public static float GetDuration(float[] samples, int sampleRate)
        {
            if (samples == null || samples.Length == 0 || sampleRate <= 0)
                return 0f;
            return (float)samples.Length / sampleRate;
        }

        /// <summary>
        /// Trim silence from the beginning and end of samples
        /// </summary>
        /// <param name="samples">Audio samples</param>
        /// <param name="threshold">Silence threshold (0.01 = -40dB)</param>
        /// <returns>Trimmed samples</returns>
        public static float[] TrimSilence(float[] samples, float threshold = 0.01f)
        {
            if (samples == null || samples.Length == 0)
                return samples;

            int start = 0;
            int end = samples.Length - 1;

            // Find start (first sample above threshold)
            while (start < samples.Length && Mathf.Abs(samples[start]) < threshold)
            {
                start++;
            }

            // Find end (last sample above threshold)
            while (end > start && Mathf.Abs(samples[end]) < threshold)
            {
                end--;
            }

            // Add small padding
            int padding = 1600;  // ~100ms at 16kHz
            start = Mathf.Max(0, start - padding);
            end = Mathf.Min(samples.Length - 1, end + padding);

            // Extract trimmed samples
            int length = end - start + 1;
            float[] trimmed = new float[length];
            Array.Copy(samples, start, trimmed, 0, length);

            Debug.Log($"[WavEncoder] Trimmed: {samples.Length} -> {trimmed.Length} samples");
            return trimmed;
        }

        /// <summary>
        /// Normalize audio samples to target peak level
        /// </summary>
        public static float[] Normalize(float[] samples, float targetPeak = 0.9f)
        {
            if (samples == null || samples.Length == 0)
                return samples;

            // Find current peak
            float peak = 0f;
            foreach (float s in samples)
            {
                float abs = Mathf.Abs(s);
                if (abs > peak) peak = abs;
            }

            if (peak < 0.001f)  // Silence
                return samples;

            // Calculate gain
            float gain = targetPeak / peak;
            if (gain > 10f) gain = 10f;  // Limit gain to avoid amplifying noise

            // Apply gain
            float[] normalized = new float[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                normalized[i] = samples[i] * gain;
            }

            Debug.Log($"[WavEncoder] Normalized with gain: {gain:F2}");
            return normalized;
        }
    }
}
