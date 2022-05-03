using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

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

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var worldForm = new WorldForm();
            worldForm.Content.AddComponent<CarGame>();

            Application.Run(worldForm);
        }
    }
}
