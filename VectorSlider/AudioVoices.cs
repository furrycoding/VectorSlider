using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace AudioSystem
{
    public interface IVoice
    {
        // Reset all internal variables, and use the given sample rate until the next call
        void Initialize(int sampleRate);

        // Fill the given buffer with the generated samples
        void Read(float[] samples, int offset, int count);

        // Start playing a note with the given frequency and volume(gain)
        void NoteOn(float frequency, float gain);

        // Stop playing the current note
        // This does not have to instantly stop the current note, there may be some falloff
        void NoteOff();

        // How many more samples does this voice have to generate,
        // until it's contribution becomes insignificant
        // The return value of this function should only change
        // after calls to Initialize, NoteOn or NoteOff
        int RemainingSamples();
    }


    /// ====== Raw tone generators ======
    public abstract class ToneGeneratorVoice : IVoice
    {
        protected int sampleRate;

        public void Initialize(int sampleRate)
        {
            this.sampleRate = sampleRate;
            Reset();
        }

        protected abstract void Reset();

        public abstract void NoteOn(float frequency, float gain);

        public abstract void Read(float[] samples, int offset, int count);

        public void NoteOff()
        {
        }
        
        public int RemainingSamples()
        {
            return 99999999;
        }
    }

    public class SineTone : ToneGeneratorVoice
    {
        private float currentGain;

        // Goertzel's algorithm IIR filter
        private float y0, y1;
        private float y0Mul, y1Mul;

        public override void NoteOn(float frequency, float gain)
        {
            var w = 2 * Math.PI * frequency / sampleRate;
            var freqMul = 2 * (float)Math.Cos(w);

            y0 = (float)Math.Sin(-w);
            y1 = 0;

            currentGain = gain;
            y0Mul = -1;
            y1Mul = freqMul;
        }

        public override void Read(float[] samples, int offset, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var y = y0Mul * y0 + y1Mul * y1;
                y0 = y1;
                y1 = y;

                samples[offset + i] = y * currentGain;
            }
        }

       protected override void Reset()
        {
            y0 = y1 = 0;
            y0Mul = y1Mul = 0;
            currentGain = 0;
        }
    }

    public class SquareTone : ToneGeneratorVoice
    {
        private float currentGain;

        private int samplesPerCycle;
        private int samplesPerHalfCycle;
        private int counter;

        public override void NoteOn(float frequency, float gain)
        {
            var cycleLength = sampleRate / frequency;
            cycleLength = Math.Max(1, cycleLength);
            samplesPerCycle = (int)cycleLength;
            samplesPerHalfCycle = (int)(cycleLength / 2);
            currentGain = gain;
        }

        public override void Read(float[] samples, int offset, int count)
        {
            for (var i = 0; i < count; i++)
            {
                counter = (counter + 1) % samplesPerCycle;
                var y = (counter >= samplesPerHalfCycle) ? 1 : -1;
                samples[offset + i] = y * currentGain;
            }
        }

        protected override void Reset()
        {
            samplesPerCycle = samplesPerHalfCycle = 1;
            currentGain = 0;
        }
    }

    public class TriangleTone : ToneGeneratorVoice
    {
        private float currentGain;

        private float samplesPerQuarterCycle;
        private float step;
        private float counter;

        public override void NoteOn(float frequency, float gain)
        {
            var cycleLength = sampleRate / frequency;
            samplesPerQuarterCycle = cycleLength / 4;
            step = 1f / samplesPerQuarterCycle;
            currentGain = gain;
        }

        public override void Read(float[] samples, int offset, int count)
        {
            for (var i = 0; i < count; i++)
            {
                counter += step;
                if (Math.Abs(counter) >= 1)
                {
                    var sign = Math.Sign(counter);
                    step = -sign / samplesPerQuarterCycle;
                    counter = 2 * sign - counter;
                }

                samples[offset + i] = counter * currentGain;
            }
        }

        protected override void Reset()
        {
            samplesPerQuarterCycle = 1;
            step = 0;
            currentGain = 0;
        }
    }


    /// ====== Filters/envelopes ======
    public class PassthroughFilter : IVoice
    {
        private IVoice voice;

        public PassthroughFilter(IVoice wrappedVoice)
        {
            voice = wrappedVoice;
        }

        public void Initialize(int sampleRate)
        {
            voice.Initialize(sampleRate);
        }

        public void Read(float[] samples, int offset, int count)
        {
            voice.Read(samples, offset, count);
        }

        public void NoteOn(float frequency, float gain)
        {
            voice.NoteOn(frequency, gain);
        }

        public void NoteOff()
        {
            voice.NoteOff();
        }

        public int RemainingSamples()
        {
            return voice.RemainingSamples();
        }
    }

    public class LowPassFilter : IVoice
    {
        private int sampleRate;
        private IVoice voice;

        private float currentValue;
        private float coefficientA = 0, coefficientB = 1;

        public LowPassFilter(IVoice wrappedVoice)
        {
            voice = wrappedVoice;
        }

        public void SetCutoff(float frequency)
        {
            var w = 2 * Math.PI * frequency / sampleRate;
            var alpha = w / (w + 1);
            coefficientA = (float)(1 - alpha);
            coefficientB = (float)alpha;
        }

        public void Initialize(int sampleRate)
        {
            voice.Initialize(sampleRate);
            
            this.sampleRate = sampleRate;
            coefficientA = 0;
            coefficientB = 1;
            currentValue = 0;
        }

        public void Read(float[] samples, int offset, int count)
        {
            voice.Read(samples, offset, count);

            for (var i = 0; i < count; i++)
            {
                var y = samples[offset + i];

                currentValue = coefficientB * y + coefficientA * currentValue;

                samples[offset + i] = currentValue;
            }
        }

        public void NoteOn(float frequency, float gain)
        {
            voice.NoteOn(frequency, gain);
        }

        public void NoteOff()
        {
            voice.NoteOff();
        }

        public int RemainingSamples()
        {
            return voice.RemainingSamples();
        }
    }

    public class ADSREnvelope : IVoice
    {
        private IVoice voice;

        // -1 - inactive, 0 - attack, 1 - decay, 2 - sustain, 3 - release
        private int state = 0;
        private float currentGain = 0;

        // The amount that's added to the gain every sample of the attack phase
        private float attackSpeed;
        // The value the gain has to excede to transition into the decay phase
        private float attackGain;

        // The amount that the gain is multiplied by every sample of the decay phase
        private float decayMultiplier;
        // The gain has to be smaller than this value to transition into the sustain phase
        private float sustainGain;

        // The amount that the gain is multiplied by every sample of the release phase
        private float releaseMultiplier;
        // When the gain is smaller than this value, it's assumed to be zero
        private float minGain;

        // NOTE:
        // Both multipliers are actually stored as multiplier-1
        // And the multiplication is performed like this:
        // value += value*(multiplier-1)
        // This is because floats have more precision around zero,
        // while multipliers tend to be around 1

        public ADSREnvelope(IVoice wrappedVoice)
        {
            voice = wrappedVoice;
        }

        public void SetupADSR(
            int sampleRate,
            float attackTime, float attackGain, float decayTime,
            float sustainGain, float releaseTime, float minimumGain)
        {
            attackSpeed = attackGain / (attackTime * sampleRate);
            this.attackGain = attackGain;

            var fullMul = sustainGain / attackGain;
            var perSampleMul = Math.Pow(fullMul, 1.0 / (decayTime * sampleRate));
            decayMultiplier = (float)(perSampleMul - 1);
            this.sustainGain = sustainGain;

            perSampleMul = Math.Pow(0.5, 1.0 / (releaseTime * sampleRate));
            releaseMultiplier = (float)(perSampleMul - 1);
            
            minGain = minimumGain;
        }

        public void Initialize(int sampleRate)
        {
            voice.Initialize(sampleRate);
            state = -1;
            currentGain = 0;
        }

        public void Read(float[] samples, int offset, int count)
        {
            if ((state > 0) && (currentGain < minGain))
                state = -1;

            if (state == -1)
            {
                Array.Clear(samples, offset, count);
                return;
            }


            voice.Read(samples, offset, count);
            for (var i = 0; i < count; i++)
            {
                switch (state)
                {
                    case 0: // Attack
                        currentGain += attackSpeed;
                        if (currentGain > attackGain)
                            state++;
                        break;
                    case 1: // Decay
                        currentGain += currentGain * decayMultiplier;
                        if (currentGain < sustainGain)
                            state++;
                        break;
                    case 2: // Sustain
                        break;
                    case 3: // Release
                        currentGain += currentGain * releaseMultiplier;
                        break;
                }

                samples[offset + i] *= currentGain;
            }
        }

        public void NoteOn(float frequency, float gain)
        {
            voice.NoteOn(frequency, gain);
            state = 0;
            currentGain = 0;
        }

        public void NoteOff()
        {
            voice.NoteOff();
            state = 3;
        }

        public int RemainingSamples()
        {
            if (state < 0)
                return 0;

            var rem = voice.RemainingSamples();
            if (state != 3)
                return rem;

            // cur * m^samples = min
            // m^samples = min / cur
            // samples = log_m(min/cur)
            var samples = Math.Log(minGain / currentGain, releaseMultiplier+1);
            return Math.Min((int)samples, rem);
        }
    }

    public class ConstantEnvelope : IVoice
    {
        private IVoice voice;

        private bool active = false;

        public ConstantEnvelope(IVoice wrappedVoice)
        {
            voice = wrappedVoice;
        }

        public void Initialize(int sampleRate)
        {
            voice.Initialize(sampleRate);
            active = false;
        }

        public void Read(float[] samples, int offset, int count)
        {
            if (!active)
            {
                Array.Clear(samples, offset, count);
                return;
            }

            voice.Read(samples, offset, count);
        }

        public void NoteOn(float frequency, float gain)
        {
            voice.NoteOn(frequency, gain);
            active = true;
        }

        public void NoteOff()
        {
            voice.NoteOff();
            active = false;
        }

        public int RemainingSamples()
        {
            if (!active)
                return 0;

            return voice.RemainingSamples();
        }
    }

    /// ====== Utility voices ======
    public class MultiVoice : IVoice
    {
        private (IVoice v, float freqMul, float gainMul)[] voices;

        private float[] voiceBuffer = new float[8192];

        public MultiVoice(IEnumerable<(IVoice v, float freqMul, float gainMul)> wrappedVoices)
        {
            voices = wrappedVoices.ToArray();
        }

        public void Initialize(int sampleRate)
        {
            foreach (var voice in voices)
                voice.v.Initialize(sampleRate);
        }

        public void Read(float[] samples, int offset, int count)
        {
            Array.Clear(samples, offset, count);
            // TODO: Track which voices return RemainingSamples() == 0, and don't ask them to generate more samples
            foreach (var voice in voices)
                AddVoice(voice.v, samples, offset, count, ref voiceBuffer);
        }

        public void NoteOn(float frequency, float gain)
        {
            foreach (var voice in voices)
                voice.v.NoteOn(frequency * voice.freqMul, gain * voice.gainMul);
        }

        public void NoteOff()
        {
            foreach (var voice in voices)
                voice.v.NoteOff();
        }

        public int RemainingSamples()
        {
            var rem = 0;
            foreach (var voice in voices)
                rem = Math.Max(rem, voice.v.RemainingSamples());

            return rem;
        }


        // Utility function to synthesize and add voice's samples into a sample buffer
        // Requires a scratch "voiceBuffer" array, which is automatically reallocated to fit the requested amount of samples
        // Upon return, that "voiceBuffer" contains the samples the given voice generated
        public static void AddVoice(IVoice v, float[] samples, int offset, int count, ref float[] voiceBuffer)
        {
            if (voiceBuffer.Length < count)
            {
                var newLen = Math.Max(2 * voiceBuffer.Length, count);
                voiceBuffer = new float[newLen];
            }

            v.Read(voiceBuffer, 0, count);
            for (var i = 0; i < count; i++)
                samples[offset + i] += voiceBuffer[i];
        }
    }
}
