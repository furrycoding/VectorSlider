using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using NAudio.Wave;


namespace AudioSystem
{
    public class CacheWithKey<K, T>
    {
        // Maps the instrument number to a stack of unused voices of that same instrument
        private Dictionary<K, Stack<T>> storage = new Dictionary<K, Stack<T>>();

        private ValueFactory factory;

        private int count;

        public CacheWithKey(ValueFactory factory)
        {
            this.factory = factory;
        }
        
        public int Count => count;

        public T Get(K key)
        {
            var s = ValuesForKey(key);
            if (s.Count > 0)
            {
                count--;
                return s.Pop();
            }

            return factory(key);
        }

        public void Return(T value, K key)
        {
            var s = ValuesForKey(key);
            count++;
            s.Push(value);
        }

        public void Clear()
        {
            count = 0;
            foreach (var s in storage.Values)
                s.Clear();
        }

        private Stack<T> ValuesForKey(K key)
        {
            Stack<T> ret;
            if (!storage.TryGetValue(key, out ret))
            {
                ret = new Stack<T>();
                storage[key] = ret;
            }
            return ret;
        }

        public delegate T ValueFactory(K instrumentID);
    }

    public class MIDIPlayer : ISampleProvider
    {
        public WaveFormat WaveFormat { get; private set; }

        public float Gain = 1.0f;

        // Time measured in samples
        // Using unsigned ints here
        // would mean the song can last at most 13 hours (assuming 44100Hz sample rate)
        private long songTime, nextNoteTime;

        // Look ahead for the next note
        private bool haveNextEvent;
        private int nextEventTrack;
        private MidiParser.MidiEvent nextEvent;
        private IEnumerator<(int track, MidiParser.MidiEvent evt)> currentSong;
        private long ticksPerSecond;

        private float[] voiceBuffer = new float[8192];

        // Voices that are currently playing
        // TODO: Make it sorted by increasing end time maybe?
        private LinkedList<(long endTime, int instrument, IVoice v)> activeVoices = new LinkedList<(long, int, IVoice)>();
        
        // Which voice is associated with a particular channel on a particular track
        // That is, it's the voice that has started playing due to the last NoteOn on this note&channel&track
        private Dictionary<(int track, int channel, int note), IVoice> channelVoices = new Dictionary<(int, int, int), IVoice>();

        // Stores the current instrument for every channel&track pair
        private Dictionary<(int track, int channel), int> channelInstruments = new Dictionary<(int, int), int>();

        private CacheWithKey<int, IVoice> voiceCache;

        public MIDIPlayer(int sampleRate, InstrumentFactory instrumentFactory)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
            voiceCache = new CacheWithKey<int, IVoice>(inst => instrumentFactory(this, inst));
        }

        public void SetSong(MidiParser.MidiFile file)
        {
            activeVoices.Clear();
            channelVoices.Clear();
            channelInstruments.Clear();
            voiceCache.Clear();

            var events = file.Tracks
                .SelectMany(t => t.MidiEvents.Select(evt => (t.Index, evt)))
                .OrderBy(item => item.evt.Time);

            // Not sure that's correct, but it seems to be
            ticksPerSecond = 3 * file.TicksPerQuarterNote / 2;

            songTime = 0;
            haveNextEvent = false;
            nextNoteTime = 0;
            currentSong = events.GetEnumerator();
            UpdateNote();
        }

        public int Read(float[] buffer, int offset, int count)
        {
            for (var i = 0; i < count;)
            {
                var samplesToNextNote = (int)(nextNoteTime - songTime);

                var cnt = Math.Min(count - i, samplesToNextNote);

                Array.Clear(buffer, offset + i, cnt);
                foreach (var voice in activeVoices)
                    MultiVoice.AddVoice(voice.v, buffer, offset + i, cnt, ref voiceBuffer);

                i += cnt;
                songTime += cnt;

                while (songTime >= nextNoteTime)
                    UpdateNote();

                // Deactivate voices that have expired
                for (var node = activeVoices.First; node != null; node = node.Next)
                {
                    var item = node.Value;
                    if (item.endTime > songTime)
                        continue;

                    activeVoices.Remove(node);
                    voiceCache.Return(item.v, item.instrument);
                }
            }

            return count;
        }

        private void UpdateNote()
        {
            if (haveNextEvent)
            {
                ProcessNextEvent();
                haveNextEvent = false;
            }

            if (currentSong == null)
            {
                nextNoteTime = int.MaxValue;
                return;
            }

            while (!haveNextEvent)
            {
                if (!currentSong.MoveNext())
                {
                    currentSong = null;
                    return;
                }

                var value = currentSong.Current;
                switch (value.evt.MidiEventType)
                {
                    case MidiParser.MidiEventType.ProgramChange:
                        var track = value.track;
                        var channel = value.evt.Channel;
                        var program = value.evt.Arg2;
                        channelInstruments[(track, channel)] = program;
                        break;

                    case MidiParser.MidiEventType.NoteOn:
                    case MidiParser.MidiEventType.NoteOff:
                        nextEvent = value.evt;
                        nextEventTrack = value.track;
                        haveNextEvent = true;
                        break;
                }
            }

            // Calculate this using longs to avoid overflows
            // TODO: Figure out what the time is measured in
            nextNoteTime = (long)WaveFormat.SampleRate * nextEvent.Time / ticksPerSecond;

            // TODO: Don't have to do this for every voice
            // endTime should only change for the voices that the last event affected
            for (var node = activeVoices.First; node != null; node = node.Next)
            {
                var item = node.Value;
                var newEndTime = songTime + item.v.RemainingSamples();
                node.Value = (newEndTime, item.instrument, item.v);
            }
        }

        private void ProcessNextEvent()
        {
            if (!haveNextEvent)
                return;

            var key = (nextEventTrack, nextEvent.Channel, nextEvent.Note);
            IVoice currentVoice;
            if (!channelVoices.TryGetValue(key, out currentVoice))
                currentVoice = null;

            switch (nextEvent.MidiEventType)
            {
                case MidiParser.MidiEventType.NoteOn:
                    currentVoice?.NoteOff();

                    var key2 = (nextEventTrack, nextEvent.Channel);
                    int instrument;
                    if (!channelInstruments.TryGetValue(key2, out instrument))
                        instrument = 0;

                    currentVoice = voiceCache.Get(instrument);
                    currentVoice.Initialize(WaveFormat.SampleRate);
                    PlayNoteOnVoice(currentVoice, nextEvent, Gain);

                    activeVoices.AddLast((songTime+100, instrument, currentVoice));
                    channelVoices[key] = currentVoice;
                    break;

                case MidiParser.MidiEventType.NoteOff:
                    if (currentVoice != null)
                    {
                        currentVoice.NoteOff();
                        channelVoices.Remove(key);
                    }
                    break;
            }


            var vel = nextEvent.Velocity / 127f;
            var note = nextEvent.Note - 69f;
            var freq = 440f * (float)Math.Pow(2, note / 12);
            var gain = 0.2f + 0.8f * vel;
            Console.WriteLine($"{nextEvent.Time:000000} {activeVoices.Count} {voiceCache.Count} {key} {freq:0000.00} {gain:0.000}");
        }

        private static void PlayNoteOnVoice(IVoice target, MidiParser.MidiEvent evt, float globalGain)
        {
            var vel = evt.Velocity / 127f;
            var note = evt.Note - 69f;

            var freq = 440f * (float)Math.Pow(2, note / 12);
            var gain = 0.2f + 0.8f * vel;
            target.NoteOn(freq, gain * globalGain);
        }

        public delegate IVoice InstrumentFactory(MIDIPlayer player, int instrumentID);
    }

    public class SFXPlayer : ISampleProvider
    {
        public WaveFormat WaveFormat { get; private set; }

        public float Gain = 1.0f;

        private ulong sampleCounter = 0;

        private float[] voiceBuffer = new float[8192];


        // Voices that are currently playing
        // TODO: Make it sorted by increasing end time maybe?
        private LinkedList<(ulong endTime, int sfxID, IVoice v)> activeVoices = new LinkedList<(ulong, int, IVoice)>();

        private CacheWithKey<int, IVoice> voiceCache;

        private object activeVoicesLock = new object();


        public SFXPlayer(int sampleRate, SFXFactory sfxFactory)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
            voiceCache = new CacheWithKey<int, IVoice>(sfxID => sfxFactory(this, sfxID));
        }

        public void PlaySFX(int sfxID, float frequency, float gain)
        {
            var newVoice = voiceCache.Get(sfxID);
            newVoice.Initialize(WaveFormat.SampleRate);
            newVoice.NoteOn(frequency, gain);

            var endTime = sampleCounter + (ulong)newVoice.RemainingSamples();
            lock (activeVoicesLock)
                activeVoices.AddLast((endTime, sfxID, newVoice));
        }

        public int Read(float[] buffer, int offset, int count)
        {
            lock (activeVoicesLock)
            {
                Array.Clear(buffer, offset, count);
                foreach (var voice in activeVoices)
                    MultiVoice.AddVoice(voice.v, buffer, offset, count, ref voiceBuffer);

                sampleCounter += (ulong)count;

                // Deactivate voices that have expired
                for (var node = activeVoices.First; node != null; node = node.Next)
                {
                    var item = node.Value;
                    if (item.endTime > sampleCounter)
                        continue;

                    activeVoices.Remove(node);
                    voiceCache.Return(item.v, item.sfxID);
                }

                return count;
            }
        }

        public delegate IVoice SFXFactory(SFXPlayer player, int sfxID);
    }

}
