using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VectorSlider
{
    internal partial class WorldForm : Form
    {
        public readonly World Content;
        // public CarGame game;

        private DeltaTime deltaTime = new DeltaTime();
        private Timer timer = new Timer();

        public WorldForm()
        {
            DoubleBuffered = true;

            InitializeComponent();

            //game = new CarGame();
            //Controls.Add(game);

            Content = new World();
            Content.Input.AttachToControl(this);

            timer = new Timer();
            timer.Interval = 13;
            timer.Tick += (sender, args) => UpdateGame();
            timer.Start();
        }

        private void UpdateGame()
        {
            var dt = deltaTime.GetDeltaTime();

            Content.Update(dt);

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;

            g.FillRectangle(Brushes.Black, g.VisibleClipBounds);

            Content.Render(g);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            base.OnHandleDestroyed(e);

            timer.Stop();
        }
    }
}
