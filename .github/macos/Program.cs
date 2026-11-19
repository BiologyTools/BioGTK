using System;
using Gtk;

namespace BioGTKApp;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.Init();
        BioGTK.NodeView.Create(args).Show();
        Application.Run();
    }
}
