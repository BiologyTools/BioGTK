using AForge;
using Gtk;
using javax.swing.text;
using omero.gateway.model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
    public class Login : Dialog
    {
        #region Properties
        private Builder _builder;

#pragma warning disable 649
        [Builder.Object]
        private Gtk.Entry usernameBox;
        [Builder.Object]
        private Gtk.Entry hostBox;
        [Builder.Object]
        private Gtk.Entry passwordBox;
        [Builder.Object]
        private Gtk.SpinButton portBox;
        [Builder.Object]
        private Gtk.Button loginBut;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        public static int IconWidth;
        public static int IconHeight;
        public List<Image> images = new List<Image>();
        /// It creates a new Search object, which is a Gtk.Window, and returns it
        /// 
        /// @return A new instance of the Search class.
        public static Login Create()
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/login.glade", FileMode.Open));
            return new Login(builder, builder.GetObject("login").Handle);
        }

        /* The constructor for the TextInput class. */
        protected Login(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            App.ApplyStyles(this);
            loginBut.ButtonPressEvent += LoginButton_ButtonPressEvent;
            portBox.Adjustment = new Adjustment(4064,0,9999,1,1,1);
            BioLib.Settings.Load();
            BioLib.OMERO.host = BioLib.Settings.GetSettings("host");
            BioLib.OMERO.username = BioLib.Settings.GetSettings("username");
            hostBox.Buffer.Text = BioLib.OMERO.host;
            usernameBox.Buffer.Text = BioLib.OMERO.username;
            string p = BioLib.Settings.GetSettings("port");
            if (p != "")
            {
                BioLib.OMERO.port = int.Parse(p);
                portBox.Value = BioLib.OMERO.port;
            }
        }

        private void LoginButton_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            BioLib.OMERO.host = hostBox.Buffer.Text;
            BioLib.OMERO.port = (int)portBox.Value;
            BioLib.OMERO.username = usernameBox.Buffer.Text;
            BioLib.OMERO.password = passwordBox.Buffer.Text;
            try
            {
                BioLib.OMERO.Connect();
                BioLib.Settings.AddSettings("host", BioLib.OMERO.host);
                BioLib.Settings.AddSettings("username", BioLib.OMERO.username);
                BioLib.Settings.AddSettings("port", BioLib.OMERO.port.ToString());
                BioLib.Settings.Save();
                this.DefaultResponse = ResponseType.Ok;
                Respond(ResponseType.Ok);
                Hide();
                Destroy();
            }
            catch (Exception e)
            {
                var dialog = new MessageDialog(
                null,
                DialogFlags.Modal,
                MessageType.Info,
                ButtonsType.Ok,
                "Error logging in to OMERO."
                );

                    dialog.Title = "Error logging in to OMERO.";
                    dialog.Run();
            }
            
        }
        #endregion


    }
}
