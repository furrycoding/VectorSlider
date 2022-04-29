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
    public partial class Form1 : Form
    {
        private CarGame game;

        public Form1()
        {
            game = new CarGame();
            Controls.Add(game);

            InitializeComponent();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            game.Size = Size;
        }
    }
}
