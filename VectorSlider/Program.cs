using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

using NAudio;
using NAudio.Wave;


namespace VectorSlider
{
    static class Program
    {
        private static void CarGameTest()
        {
            var test = new World();
            test.AddComponent<CarGame>();
            var phys = test.GetComponent<CarPhysics>();

            System.Diagnostics.Debug.WriteLine(phys.position);

            for (var i = 0; i < 100; i++)
                test.Update(0.01f);

            System.Diagnostics.Debug.WriteLine(phys.position);

            phys.turn = 0.4f;
            phys.gas = 0.8f;
            for (var i = 0; i < 40; i++)
                test.Update(0.01f);

            System.Diagnostics.Debug.WriteLine(phys.position);

            phys.turn = 0.1f;
            phys.gas = 0.0f;
            for (var i = 0; i < 50; i++)
                test.Update(0.01f);

            System.Diagnostics.Debug.WriteLine(phys.position);
        }

        private static WaveOutEvent a;

        private static void TestAudio()
        {
            var driverOut = new WaveOutEvent();
            driverOut.DesiredLatency = 80;

            var sampleRate = 44100;

            //var midiPlayer = new OldMIDIPlayer(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1));
            var midiPlayer = new AudioSystem.MIDIPlayer(sampleRate, (player, instr) =>
            {
                Console.WriteLine($"Creating new voice for instrument {instr}");

                AudioSystem.IVoice v;

                if (instr == 32)
                    v = new AudioSystem.MultiVoice(new[]
                    {
                        ((AudioSystem.IVoice)new AudioSystem.SineTone(), 1f, 1f / 2),
                        (new AudioSystem.SineTone(), 2f, 1f / 8),
                    });
                else
                    v = new AudioSystem.MultiVoice(new[]
                    {
                        ((AudioSystem.IVoice)new AudioSystem.SineTone(), 1f, 1f),
                        (new AudioSystem.SineTone(), 2f, 1f / 4),
                        (new AudioSystem.SineTone(), 4f, 1f / 6)
                    });

                //v = new AudioSystem.TriangleTone();

                var lpf = new AudioSystem.LowPassFilter(v);
                lpf.SetCutoff(2000);

                var envelope = new AudioSystem.ADSREnvelope(lpf);
                envelope.SetupADSR(sampleRate, 0.009f, 1.1f, 0.025f, 0.9f, 0.09f, 0.01f);
                //envelope.SetupADSR(sampleRate, 0.008f, 1.0f, 10.9f, 0.0f, 0.3f, 0.001f);
                return envelope;
            });
            midiPlayer.Gain = 0.05f;

            var f = new MidiParser.MidiFile("../../../Aria Math D.mid");

            var sfx = new AudioSystem.SFXPlayer(sampleRate, (player, id) =>
            {
                var ret = new AudioSystem.ADSREnvelope(new AudioSystem.TriangleTone());
                ret.SetupADSR(sampleRate, 0.0001f, 1.2f, 1.0f, 0.005f, 0.1f, 0.01f);
                return ret;
            });

            var wg = new NAudio.Wave.SampleProviders.MixingSampleProvider(new[] { (ISampleProvider)midiPlayer, sfx });
            driverOut.Init(wg);

            midiPlayer.SetSong(f);
            driverOut.Play();
            a = driverOut;
        }


        // https://stackoverflow.com/a/6362414
        private static System.Reflection.Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            var dllName = args.Name;

            var idx = dllName.IndexOf(',');
            dllName = (idx != -1) ? dllName.Substring(0, idx) : dllName.Replace(".dll", "");
            dllName = dllName.Replace(".", "_");

            if (dllName.EndsWith("_resources"))
                return null;

            var rm = new System.Resources.ResourceManager(
                "VectorSlider.Properties.Resources",
                System.Reflection.Assembly.GetExecutingAssembly()
            );

            var bytes = (byte[])rm.GetObject(dllName);

            return System.Reflection.Assembly.Load(bytes);
        }

        internal class DebugString : Component, IRenderable
        {
            public static string S = "none";

            public void Render(Graphics g)
            {
                g.ScaleTransform(0.03f, 0.03f);
                g.DrawString(S, SystemFonts.DefaultFont, Brushes.White, 0f, 0f);
            }
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(CurrentDomain_AssemblyResolve);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var worldForm = new WorldForm();
            worldForm.Content.AddComponent<CarGame>();
            worldForm.Content.AddComponent<DebugString>();

            TestAudio();
            Application.Run(worldForm);
            a.Stop();
        }
    }
}
