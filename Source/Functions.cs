using Newtonsoft.Json;
using Gtk;
using System;
using System.Collections.Generic;
using System.IO;
using java.nio.channels;
using Gdk;

namespace BioGTK
{
    public class Functions : Gtk.Window
    {
        //public static InputSimulator input = new InputSimulator();
        Function func = new Function();
        public Function Func
        {
            get
            {
                return func;
            }
            set
            {
                func = value;
            }
        }

        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;
        int line = 0;
#pragma warning disable 649
        [Builder.Object]
        private Button okBut;
        [Builder.Object]
        private Button cancelBut;
        [Builder.Object]
        private TextView textBox;
        [Builder.Object]
        private ComboBox funcsBox;
        [Builder.Object]
        private Entry nameBox;
        [Builder.Object]
        private ComboBox keysBox;
        [Builder.Object]
        private ComboBox stateBox;
        [Builder.Object]
        private ComboBox modifierBox;
        [Builder.Object]
        private SpinButton valBox;
        [Builder.Object]
        private Entry menuPath;
        [Builder.Object]
        private Entry contextMenuPath;
        [Builder.Object]
        private RadioButton imagejRadioBut;
        [Builder.Object]
        private RadioButton bioRadioBut;
        [Builder.Object]
        private Button setMacroFileBut;
        [Builder.Object]
        private Button setScriptFileBut;
        [Builder.Object]
        private Label fileLabel;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        /// <summary> Default Shared Constructor. </summary>
        /// <returns> A TestForm1. </returns>
        public static Functions Create()
        {
            Builder builder = new Builder(null, "BioGTK.Glade.Functions.glade", null);
            return new Functions(builder, builder.GetObject("functions").Handle);
        }

        /// <summary> Specialised constructor for use only by derived class. </summary>
        /// <param name="builder"> The builder. </param>
        /// <param name="handle">  The handle. </param>
        protected Functions(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            SetupHandlers();
            Init();
        }

        #endregion

        #region Handlers

        /// <summary> Sets up the handlers. </summary>
        protected void SetupHandlers()
        {
            funcsBox.Changed += FuncsBox_Changed;
            keysBox.Changed += KeysBox_Changed;
            stateBox.Changed += StateBox_Changed;
            modifierBox.Changed += ModifiersBox_Changed;
            okBut.Clicked += OkBut_Clicked;
            cancelBut.Clicked += CancelBut_Clicked;
            setMacroFileBut.Clicked += SetMacroFileBut_Clicked;
            setScriptFileBut.Clicked += SetScriptFileBut_Clicked;
            valBox.ChangeValue += ValBox_ChangeValue;
            textBox.Buffer.Changed += Buffer_Changed;
            imagejRadioBut.Clicked += ImagejRadioBut_Clicked;
            bioRadioBut.Clicked += BioRadioBut_Clicked;
            this.DeleteEvent += Functions_DeleteEvent;
        }

        private void Functions_DeleteEvent(object o, DeleteEventArgs args)
        {
            args.RetVal = true;
            Hide();
        }

        private void BioRadioBut_Clicked(object sender, EventArgs e)
        {
            func.FuncType = Function.FunctionType.Script;
        }

        private void ImagejRadioBut_Clicked(object sender, EventArgs e)
        {
            func.FuncType = Function.FunctionType.ImageJ;
        }

        private void Buffer_Changed(object sender, EventArgs e)
        {
            func.Script = textBox.Buffer.Text;
            if (bioRadioBut.Active)
                func.FuncType = Function.FunctionType.Script;
            else
                func.FuncType = Function.FunctionType.ImageJ;
        }

        private void ValBox_ChangeValue(object o, ChangeValueArgs args)
        {
            Func.Value = valBox.Value;
        }

        private void SetScriptFileBut_Clicked(object sender, EventArgs e)
        {
            Gtk.FileChooserDialog filechooser =
      new Gtk.FileChooserDialog("Choose the script file to open",
          this,
          FileChooserAction.Open,
          "Cancel", ResponseType.Cancel,
          "Open", ResponseType.Ok);
            if (filechooser.Run() != (int)ResponseType.Ok)
                return;
            func.File = filechooser.Filename;
            fileLabel.Text = func.File;
            func.FuncType = Function.FunctionType.Script;
        }

        private void SetMacroFileBut_Clicked(object sender, EventArgs e)
        {
            Gtk.FileChooserDialog filechooser =
      new Gtk.FileChooserDialog("Choose the ImageJ macro file to open",
          this,
          FileChooserAction.Open,
          "Cancel", ResponseType.Cancel,
          "Open", ResponseType.Ok);
            if (filechooser.Run() != (int)ResponseType.Ok)
                return;
            func.File = filechooser.Filename;
            fileLabel.Text = func.File;
            func.FuncType = Function.FunctionType.ImageJ;
        }

        private void CancelBut_Clicked(object sender, EventArgs e)
        {
            Close();
        }

        private void OkBut_Clicked(object sender, EventArgs e)
        {
            func.Name = nameBox.Text;
            func.MenuPath = menuPath.Text;
            func.ContextPath = contextMenuPath.Text;
            func.Value = valBox.Value;
            if (!Function.Functions.ContainsKey(func.Name))
            {
                Function.Functions.Add(func.Name, func);
            }
            else
            {
                Function.Functions[func.Name] = func;
            }
            if (func.MenuPath != null && func.MenuPath != "")
            {
                if (func.MenuPath.EndsWith("/"))
                    func.MenuPath = func.MenuPath.TrimEnd('/');
                if (func.MenuPath.EndsWith(func.Name) && func.MenuPath.Contains("/"))
                    func.MenuPath.Remove(func.MenuPath.IndexOf('/'), func.MenuPath.Length - func.MenuPath.IndexOf('/'));
                App.AddMenu(func.MenuPath);
            }
            if (func.ContextPath != null && func.ContextPath != "")
            {
                if (func.ContextPath.EndsWith("/"))
                    func.ContextPath = func.ContextPath.TrimEnd('/');
                if (func.ContextPath.EndsWith(func.Name) && func.ContextPath.Contains("/"))
                    func.ContextPath.Remove(func.ContextPath.IndexOf('/'), func.ContextPath.Length - func.ContextPath.IndexOf('/'));
                App.AddContextMenu(func.ContextPath);
            }
            func.Save();
            Init();
            Close();
        }

        private void ModifiersBox_Changed(object sender, EventArgs e)
        {
            Func.Modifier = (Gdk.ModifierType)modifierBox.Active;
            Func.FuncType = Function.FunctionType.Key;
        }

        private void StateBox_Changed(object sender, EventArgs e)
        {
            Func.State = (Function.ButtonState)stateBox.Active;
            Func.FuncType = Function.FunctionType.Key;
        }

        private void KeysBox_Changed(object sender, EventArgs e)
        {
            Func.Key = (Gdk.Key)keysBox.Active;
            Func.FuncType = Function.FunctionType.Key;
        }

        private void FuncsBox_Changed(object sender, EventArgs e)
        {
            func = Function.Functions[funcsBox.ActiveId];
            UpdateItems();
        }

        #endregion

        private void Init()
        {
            textBox.Buffer.Text = func.Script;
            var funcs = new ListStore(typeof(string));
            // Add items to the ListStore
            foreach (Function item in Function.Functions.Values)
            {
                funcs.AppendValues(item.Name);
            }
            // Set the model for the ComboBox
            funcsBox.Model = funcs;
            // Set the text column to display
            var renderer = new CellRendererText();
            funcsBox.PackStart(renderer, false);
            funcsBox.AddAttribute(renderer, "text", 0);

            nameBox.Text = func.Name;

            var states = new ListStore(typeof(string));
            //We add button states to buttonState box.
            foreach (Function.ButtonState val in Enum.GetValues(typeof(Function.ButtonState)))
            {
                states.AppendValues(val.ToString());
            }
            // Set the model for the ComboBox
            stateBox.Model = states;
            // Set the text column to display
            var renderer2 = new CellRendererText();
            stateBox.PackStart(renderer2, false);
            stateBox.AddAttribute(renderer2, "text", 0);
            stateBox.Active = (int)func.State;

            //We add keys to keys box.
            var keys = new ListStore(typeof(string));
            foreach (Gdk.Key val in Enum.GetValues(typeof(Gdk.Key)))
            {
                keys.AppendValues(val.ToString());
            }
            keysBox.Model = keys;
            // Set the text column to display
            var renderer3 = new CellRendererText();
            keysBox.PackStart(renderer3, false);
            keysBox.AddAttribute(renderer3, "text", 0);
            //We add keys to keys box.
            var mods = new ListStore(typeof(string));
            foreach (ModifierType val in Enum.GetValues(typeof(Gdk.ModifierType)))
            {
                mods.AppendValues(val.ToString());
            }
            modifierBox.Model = mods;
            // Set the text column to display
            var renderer4 = new CellRendererText();
            modifierBox.PackStart(renderer4, false);
            modifierBox.AddAttribute(renderer4, "text", 0);
            valBox.Value = func.Value;
            menuPath.Text = func.MenuPath;
            contextMenuPath.Text = func.ContextPath;
        }
        private void UpdateItems()
        {
            textBox.Buffer.Text = func.Script;
            nameBox.Text = func.Name;
            stateBox.Active = (int)func.State;
            keysBox.Active = (int)func.Key;
            modifierBox.Active = (int)func.Modifier;
            valBox.Value = func.Value;
            if (func.FuncType == Function.FunctionType.ImageJ)
                imagejRadioBut.Active = true;
            else
                imagejRadioBut.Active = false;
            menuPath.Text = func.MenuPath;
            contextMenuPath.Text = func.ContextPath;
        }

    }

    public class Function
    {
        public static Dictionary<string, Function> Functions = new Dictionary<string, Function>();
        public enum FunctionType
        {
            Key,
            Microscope,
            Objective,
            StoreCoordinate,
            NextCoordinate,
            PreviousCoordinate,
            NextSnapCoordinate,
            PreviousSnapCoordinate,
            Recording,
            Property,
            ImageJ,
            Script,
            None
        }
        public enum ButtonState
        {
            Pressed = 0,
            Released = 1,
        }

        private ButtonState buttonState = ButtonState.Pressed;
        public ButtonState State
        {
            get
            {
                return buttonState;
            }
            set
            {
                buttonState = value;
            }
        }

        private Gdk.Key key;
        public Gdk.Key Key
        {
            get
            {
                return key;
            }
            set
            {
                key = value;
                FuncType = FunctionType.Key;
            }
        }

        private Gdk.ModifierType modifier;
        public Gdk.ModifierType Modifier
        {
            get
            {
                return modifier;
            }
            set
            {
                FuncType = FunctionType.Key;
                modifier = value;
            }
        }

        private FunctionType functionType;
        public FunctionType FuncType
        {
            get
            {
                return functionType;
            }
            set
            {
                functionType = value;
            }
        }

        private string file;
        public string File
        {
            get { return file; }
            set { file = value; }
        }

        private string script;
        public string Script
        {
            get { return script; }
            set
            {
                script = value;
            }
        }

        private string name;
        public string Name
        {
            get
            {
                return name;
            }
            set
            {
                name = value;
            }
        }
        private string menuPath;
        public string MenuPath
        {
            get
            {
                return menuPath;
            }
            set
            {
                menuPath = value;
            }
        }
        private string contextPath;
        public string ContextPath
        {
            get
            {
                return contextPath;
            }
            set
            {
                contextPath = value;
            }
        }

        private double val;
        public double Value
        {
            get
            {
                return val;
            }
            set
            {
                val = value;
            }
        }
        private string microscope;
        public string Microscope
        {
            get
            {
                return microscope;
            }
            set
            {
                microscope = value;
            }
        }
        public override string ToString()
        {
            return name + ", " + MenuPath;
        }
        public static Function Parse(string s)
        {
            if (s == "")
                return new Function();
            return JsonConvert.DeserializeObject<Function>(s);
        }

        public string Serialize()
        {
            return JsonConvert.SerializeObject(this);
        }
        public object PerformFunction(bool imagej)
        {
            if (FuncType == FunctionType.Key && Script != "")
                if (imagej)
                {
                    FuncType = FunctionType.ImageJ;
                }
                else
                    FuncType = FunctionType.Script;
            if (FuncType == Function.FunctionType.Script)
            {
                return Scripting.RunString(script);
            }
            if (FuncType == Function.FunctionType.ImageJ)
            {
                ImageJ.RunOnImage(script, false, BioConsole.onTab, BioConsole.useBioformats, BioConsole.resultInNewTab);
            }
            return null;
        }

        public static void InitializeMainMenu()
        {
            string st = Environment.CurrentDirectory + "/Functions";
            if (!Directory.Exists(st))
                Directory.CreateDirectory(st);
            string[] sts = Directory.GetFiles(st);
            for (int i = 0; i < sts.Length; i++)
            {
                string fs = System.IO.File.ReadAllText(sts[i]);
                Function f = Function.Parse(fs);
                if (!Functions.ContainsKey(f.Name))
                    Functions.Add(f.Name, f);
                App.AddMenu(f.MenuPath);
            }
        }
        public static void InitializeContextMenu()
        {
            string st = Environment.CurrentDirectory + "/Functions";
            if (!Directory.Exists(st))
                Directory.CreateDirectory(st);
            string[] sts = Directory.GetFiles(st);
            for (int i = 0; i < sts.Length; i++)
            {
                string fs = System.IO.File.ReadAllText(sts[i]);
                Function f = Function.Parse(fs);
                if (!Functions.ContainsKey(f.Name))
                    Functions.Add(f.Name, f);
                App.AddContextMenu(f.ContextPath);
            }
        }
        public static void SaveAll()
        {
            string st = Environment.CurrentDirectory;

            if (!Directory.Exists(st + "/Functions"))
            {
                Directory.CreateDirectory(st + "/Functions");
            }
            foreach (Function f in Functions.Values)
            {
                System.IO.File.WriteAllText(st + "/Functions/" + f.Name + ".func", f.Serialize());
            }
        }
        public void Save()
        {
            string st = Environment.CurrentDirectory;
            if (!Directory.Exists(st + "/Functions"))
            {
                Directory.CreateDirectory(st + "/Functions");
            }
            System.IO.File.WriteAllText(st + "/Functions/" + Name + ".func", Serialize());
        }
    }

}
