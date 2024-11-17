using Gtk;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Flurl;
using Newtonsoft.Json;
using Flurl.Http;
namespace BioGTK
{
    public class Updater : Dialog
    {
        #region Properties
        private Builder _builder;
#pragma warning disable 649
        [Builder.Object]
        private Gtk.TextView textBox;
        [Builder.Object]
        private Gtk.Button upBut;
#pragma warning restore 649

        #endregion

        public class GitHubFile
        {
            public string Name { get; set; }
            public string Path { get; set; }
            public string Sha { get; set; }
            public int Size { get; set; }
            public string Url { get; set; }
            public string HtmlUrl { get; set; }
            public string GitUrl { get; set; }
            public string DownloadUrl { get; set; }
            public string Type { get; set; }
            public Links _Links { get; set; }

            // Nested class for the _links object
            public class Links
            {
                [JsonProperty("self")]
                public string Self { get; set; }

                [JsonProperty("git")]
                public string Git { get; set; }

                [JsonProperty("html")]
                public string Html { get; set; }
            }
        }

        #region Constructors / Destructors
        string selection;
        Enum ens;
        /// It creates a new Updater object, which is a Gtk.Window, and returns it
        /// 
        /// @return A new instance of the Updater class.
        public static Updater Create()
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/Updater.glade", FileMode.Open));
            return new Updater(builder, builder.GetObject("updater").Handle);
        }

        /* The constructor for the Updater class. */
        protected Updater(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            this.DeleteEvent += Updater_DeleteEvent;
            upBut.ButtonPressEvent += UpButton_ButtonPressEvent;
            string st = BioLib.Settings.GetSettings("UpdateSites");
            if(st!="")
            {
                string[] ups = st.Split(',');
                for(int i = 0; i < ups.Length; i++)
                {
                    if (i < ups.Length - 1)
                        textBox.Buffer.Text += ups[i] + Environment.NewLine;
                    else
                        textBox.Buffer.Text += ups[i];
                }
            }
            App.ApplyStyles(this);
        }

        private void UpButton_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            string[] sts;
            if (textBox.Buffer.Text.Contains('\n'))
                sts = textBox.Buffer.Text.Split('\n');
            else
                sts = new string[] { textBox.Buffer.Text };
            foreach (string s in sts)
            {
                string st = s;
                if (!s.EndsWith("/contents/"))
                    st += "/contents/";
                if (!s.Contains("api"))
                    st = st.Replace("github.com","api.github.com/repos");
                string j = GetHtmlContentAsync(st).Result;
                var gitHubFile = JsonConvert.DeserializeObject<GitHubFile[]>(j);
                foreach (var item in gitHubFile)
                {
                    if(item.Name.EndsWith(".dll"))
                    {
                        string url = s.Replace("github.com", "raw.githubusercontent.com") + "/main/" + item.Name;
                        DownloadFileAsync(url, System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/Plugins/" + item.Name).Wait();
                    }
                }
            }
        }

        private void Updater_DeleteEvent(object o, DeleteEventArgs args)
        {
            string[] sts = textBox.Buffer.Text.Split(Environment.NewLine);
            string ups = "";
            for (int i = 0; i < sts.Length; i++)
            {
                if(i<sts.Length-1)
                    ups += sts[i] + ",";
                else
                    ups += sts[i];
            }
            BioLib.Settings.AddSettings("UpdateSites", ups);
            BioLib.Settings.Save();
        }
        public static async Task DownloadFileAsync(string fileUrl, string savePath)
        {
            try
            {
                // Download the file data as byte array
                byte[] fileBytes = await fileUrl.WithHeader("User-Agent", "BioGTK").GetBytesAsync().ConfigureAwait(false);

                // Write the byte array to a file
                await File.WriteAllBytesAsync(savePath, fileBytes);
                Console.WriteLine($"File downloaded successfully and saved to {savePath}");
            }
            catch (FlurlHttpException e)
            {
                // Handle HTTP errors here (e.g., 404 or 403 errors)
                Console.WriteLine($"Failed to download the file. HTTP Error: {e.Message}");
            }
            catch (Exception e)
            {
                // Handle other errors here
                Console.WriteLine($"An error occurred: {e.Message}");
            }
        }

        public static async Task<string> GetHtmlContentAsync(string url)
        {
            try
            {
                // Fetch the HTML content of the page
                string htmlContent = await url.WithHeader("User-Agent", "BioGTK").GetStringAsync().ConfigureAwait(false);
                return htmlContent;
            }
            catch (FlurlHttpException e)
            {
                // Handle HTTP errors (e.g., 404, 500)
                Console.WriteLine($"HTTP Request Failed: {e.Message}");
                return null;
            }
        }
        #endregion

    }
}
