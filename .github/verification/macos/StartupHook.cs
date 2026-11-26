using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

internal static class StartupHook
{
    public static void Initialize()
    {
        var gtk = Assembly.Load("GtkSharp").GetType("Gtk.Application", true)!;
        gtk.GetMethod("Init", Type.EmptyTypes)!.Invoke(null, null);
        new Thread(Probe).Start();
        gtk.GetMethod("Run", Type.EmptyTypes)!.Invoke(null, null);
    }

    private static void Probe()
    {
        try
        {
            if (!AppContext.TryGetSwitch("BioLib.EnableMacOSImageJDialogs", out bool enabled) || !enabled)
                throw new InvalidOperationException("The installed app did not enable the IKVM dialog runtime.");

            var bioLib = Assembly.Load("BioLib");
            var controller = bioLib.GetType("BioLib.Fiji", true)!;
            controller.GetMethod("InitializeCommandMetadata")!.Invoke(null, null);
            var commands = Assembly.Load("BioGTK").GetType("BioGTK.ImageJCommands", true)!;
            var run = commands.GetMethod("RunAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
            var java = Assembly.Load("IKVM.Java");
            var fiji = Assembly.Load("fiji");
            RunCommand(controller, run, fiji, java, "Gamma...", "Gamma", 64);
            RunCommand(controller, run, fiji, java, "Gaussian Blur...", "Gaussian Blur", 5);
            Environment.Exit(0);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            var inner = error.InnerException ?? error;
            inner.GetType().GetMethod("printStackTrace", Type.EmptyTypes)?.Invoke(inner, null);
            Environment.Exit(1);
        }
    }

    private static void RunCommand(Type controller, MethodInfo run, Assembly fiji, Assembly java,
        string command, string title, int expectedPixel)
    {
        var processorType = fiji.GetType("ij.process.ByteProcessor", true)!;
        var processor = Activator.CreateInstance(processorType, 9, 9)!;
        processorType.GetMethod("putPixel", new[] { typeof(int), typeof(int), typeof(int) })!
            .Invoke(processor, new object[] { 4, 4, 128 });
        var imageType = fiji.GetType("ij.ImagePlus", true)!;
        var image = Activator.CreateInstance(imageType, "Installer dialog verification", processor)!;
        var convert = controller.GetMethod("GetBioImage")!;
        var volume = Activator.CreateInstance(convert.GetParameters()[1].ParameterType)!;
        var source = convert.Invoke(null, new[] { image, volume, 1.0, 1.0, 1.0 })!;

        var configure = new Thread(() => AcceptDialog(fiji, java, title)) { IsBackground = true };
        configure.Start();
        var task = (Task)run.Invoke(null, new[] { source, $"run(\"{command}\");", false, false, false, false, "" })!;
        task.GetAwaiter().GetResult();
        if (!configure.Join(TimeSpan.FromSeconds(20)))
            throw new TimeoutException($"The {title} dialog did not complete.");

        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var getPixel = result.GetType().GetMethod("GetValue",
            new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(int) })!;
        int pixel = Convert.ToInt32(getPixel.Invoke(result, new object[] { 0, 0, 0, 4, 4 }));
        if (pixel != expectedPixel)
            throw new InvalidOperationException($"{title} produced {pixel}, expected {expectedPixel}.");
        Console.WriteLine($"{title} passed through BioGTK.ImageJCommands; center pixel changed from 128 to {pixel}.");
    }

    private static void AcceptDialog(Assembly fiji, Assembly java, string title)
    {
        try
        {
            var window = java.GetType("java.awt.Window", true)!;
            var dialogType = fiji.GetType("ij.gui.GenericDialog", true)!;
            var getWindows = window.GetMethod("getWindows", Type.EmptyTypes)!;
            var actionEventType = java.GetType("java.awt.event.ActionEvent", true)!;
            for (int attempt = 0; attempt < 300; attempt++)
            {
                foreach (object dialog in (Array)getWindows.Invoke(null, null)!)
                {
                    if (!dialogType.IsInstanceOfType(dialog) ||
                        !Equals(dialogType.GetMethod("isShowing")!.Invoke(dialog, null), true) ||
                        dialogType.GetMethod("getTitle")!.Invoke(dialog, null) is not string actualTitle ||
                        actualTitle.TrimEnd('.', '…') != title)
                        continue;

                    Console.WriteLine($"{title} ImageJ dialog opened with GTK running.");
                    var fields = dialogType.GetMethod("getNumericFields")!.Invoke(dialog, null)!;
                    var field = fields.GetType().GetMethod("elementAt")!.Invoke(fields, new object[] { 0 })!;
                    field.GetType().GetMethod("setText")!.Invoke(field, new object[] { "2" });
                    var buttons = (Array)dialogType.GetMethod("getButtons")!.Invoke(dialog, null)!;
                    object action = Activator.CreateInstance(actionEventType, buttons.GetValue(0), 1001, "OK")!;
                    dialogType.GetMethod("actionPerformed", new[] { actionEventType })!.Invoke(dialog, new[] { action });
                    return;
                }
                Thread.Sleep(100);
            }
            throw new TimeoutException($"No visible {title} dialog was found.");
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Environment.Exit(1);
        }
    }
}
