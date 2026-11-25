using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Gtk;

namespace BioGTK
{
    internal static class ImageJCommands
    {
        private static readonly SemaphoreSlim externalRuns = new SemaphoreSlim(1);

        internal static bool UsesExternalImageJ(BioImage image) =>
            FijiApp.Executable.Length > 0 && image?.isPyramidal == false;

        internal static Task<BioImage> RunAsync(BioImage image, string macro, bool headless,
            bool onTab, bool useBioformats, bool newTab, string argument = "")
        {
            if (image == null)
                throw new InvalidOperationException("Open an image before running an ImageJ macro.");

            return UsesExternalImageJ(image)
                ? Task.Run(() => RunExternalAsync(image, macro, headless, useBioformats, argument))
                : Fiji.RunOnImage(image, macro, headless, onTab, useBioformats, newTab);
        }

        internal static async Task RunFromToolAsync(BioImage image, string macro, bool headless,
            bool onTab, bool useBioformats, bool newTab)
        {
            bool presentResult = UsesExternalImageJ(image);
            try
            {
                BioImage result = await RunAsync(image, macro, headless, onTab, useBioformats, newTab);
                if (presentResult && App.tabsView != null)
                    await App.tabsView.PresentCommandResultAsync(image, result);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                Application.Invoke((_, _) =>
                {
                    using var dialog = new MessageDialog(App.tabsView, DialogFlags.Modal,
                        MessageType.Error, ButtonsType.Ok, "{0}", ex.Message);
                    dialog.Title = "ImageJ Command Failed";
                    dialog.Run();
                    dialog.Destroy();
                });
            }
        }

        private static async Task<BioImage> RunExternalAsync(BioImage image, string macro,
            bool headless, bool useBioformats, string argument)
        {
            await externalRuns.WaitAsync().ConfigureAwait(false);
            DirectoryInfo workspace = null;
            try
            {
                workspace = TemporaryZarrStorage.CreateFijiWorkspace();
                // Plain ImageJ and headless runs can exchange ImageJ TIFF stacks directly.
                bool ome = useBioformats && !headless && FijiApp.IsFijiInstallation;
                string extension = ome ? ".ome.tif" : ".tif";
                string input = Path.Combine(workspace.FullName, "input" + extension);
                string output = Path.Combine(workspace.FullName, workspace.Name + extension);
                if (ome)
                    BioImage.SaveOME(image, input);
                else
                    BioImage.SaveFile(input, image.ID);

                string open = ome
                    ? $"run(\"Bio-Formats Importer\", \"open=[{Escape(input)}] view=Hyperstack stack_order=XYCZT\");"
                    : $"open(\"{Escape(input)}\");";
                string save = ome
                    ? $"run(\"Bio-Formats Exporter\", \"save=[{Escape(output)}] export compression=Uncompressed\");"
                    : $"saveAs(\"Tiff\", \"{Escape(output)}\");";
                // Results can be read lazily, so preserve their workspace until application exit.
                return await Fiji.RunStringFiji(image, $"{open}\n{macro}\n{save}",
                    argument, headless, output).ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    workspace?.Delete(recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"Could not remove the failed Fiji workspace: {ex.Message}");
                }
                throw;
            }
            finally
            {
                externalRuns.Release();
            }
        }

        private static string Escape(string path) => path.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
