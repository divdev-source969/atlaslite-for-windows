using System;
using System.Windows.Forms;

namespace AtlasLite
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                MessageBox.Show("AtlasLite hit an unexpected error:\n\n" + e.Exception.Message, "AtlasLite", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };

            string start = null;
            if(args.Length > 0) start = args[0];
            Application.Run(new MainForm(start));
        }
    }
}
