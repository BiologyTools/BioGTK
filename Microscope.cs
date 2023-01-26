using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.Drawing;
using System.Diagnostics;
using System.Threading;
using System.Reflection;
using System.IO;
using AForge;

namespace BioGTK
{
    public class Stage
    {
        public static Type stageType;
        public static Type axisType;
        public static object xAxis;
        public static object yAxis;
        public static double minX;
        public static double maxX;
        public static double minY;
        public static double maxY;
        public Stage()
        {
            PointD d = GetPosition();
            x = GetPositionX();
            y = GetPositionY();
            UpdateSWLimit();
        }

        private double x;
        private double y;
        public double X
        {
            get
            {
                return x;
            }
        }
        public double Y
        {
            get
            {
                return y;
            }
        }
        public int moveWait = 250;
        private void MoveWait()
        {
            Thread.Sleep(moveWait);
        }
        public void SetPosition(double px, double py)
        {
            x = px;
            y = py;
        }
        public void SetPositionX(double px)
        {
            x = px;
            SetPosition(px, y);
        }
        public void SetPositionY(double py)
        {
            y = py;
            SetPosition(x, py);
        }
        public double GetPositionX()
        {
            x = GetPosition().X;
            return x;
        }
        public double GetPositionY()
        {
            y = GetPosition().Y;
            return y;
        }
        public PointD GetPosition()
        {
            return new PointD(x, y);
        }
        public void MoveUp(double m)
        {
            double y = GetPositionY() - m;
            SetPositionY(y);
        }
        public void MoveDown(double m)
        {
            double y = GetPositionY() + m;
            SetPositionY(y);
        }
        public void MoveRight(double m)
        {
            double x = GetPositionX() + m;
            SetPositionX(x);
        }
        public void MoveLeft(double m)
        {
            double x = GetPositionX() - m;
            SetPositionX(x);
        }
        public void UpdateSWLimit()
        {
            
        }
        public void SetSWLimit(double xmin, double xmax, double ymin, double ymax)
        {
        }
    }

    public class Focus
    {
        public static Type focusType;
        public static Type axisType;
        public static object focus;
        public static double upperLimit;
        public static double lowerLimit;
        private static double z;
        public Focus()
        {
        }
        public void SetFocus(double f)
        {
        }
        public double GetFocus()
        {
        return z;
        }
        public PointD GetSWLimit()
        {
            return new PointD(lowerLimit, upperLimit);
        }
        public void SetSWLimit(double xd, double yd)
        {
            upperLimit = xd;
            lowerLimit = yd;
        }
    }

    public class Objectives
    {
        public List<Objective> List = new List<Objective>();
        public static Type changerType = null;
        public static object changer;

        public Objectives()
        {

        }
        public class Objective
        {
            public Dictionary<string, object> config = new Dictionary<string, object>();
            public string Name = "";
            public string UniqueName = "";
            public string ElementType = "";
            public bool Oil = false;
            public int Magnification = 0;
            public float NumericAperture = 0;
            public int Index;
            public string Modes = "";
            public string Features = "";
            public double LocateExposure = 50;
            public int WorkingDistance = 0;
            public double AcquisitionExposure = 50;
            public string Configuration = "";
            public double MoveAmountL = 40;
            public double MoveAmountR = 10;
            public double FocusMoveAmount = 0.02;
            public double ViewWidth;
            public double ViewHeight;
            public Objective(object o, int index)
            {
                Index = index;
            }
            public Objective()
            {
            }
            public override string ToString()
            {
                return Name.ToString() + " " + Index;
            }
        }
        private int index;
        public int moveWait = 1000;
        private void MoveWait()
        {
            Thread.Sleep(moveWait);
        }
        public int Index
        {
            get
            {
                return GetPosition();
            }
            set
            {
                SetPosition(value);
            }
        }
        public void SetPosition(int index)
        {
            this.index = index;
        }
        public int GetPosition()
        {
            return index;
        }
        public Objective GetObjective()
        {
            return List[index];
        }
    }

    public class TLShutter
    {
        public static Type tlType;
        public static object tlShutter = null;
        public static int position;
        public TLShutter()
        {

        }
        public short GetPosition()
        {
            return (short)position;
        }
        public void SetPosition(int p)
        {
            position = p;
        }
    }

    public class RLShutter
    {
        public static Type rlType;
        public static object rlShutter = null;
        public static int position;
        public RLShutter()
        {
        }
        public short GetPosition()
        {
            return (short)position;
        }
        public void SetPosition(int p)
        {
            position = p;
        }
    }

    public static class Microscope
    {
        public enum Actions
        {
            StageUp,
            StageRight,
            StageDown,
            StageLeft,
            StageFieldUp,
            StageFieldRight,
            StageFieldDown,
            StageFieldLeft,
            FocusUp,
            FocusDown,
            TL,
            RL,
            Acquisition,
            Locate,
        }
        public static bool redraw = false;
        public static Focus Focus = null;
        public static Stage Stage = null;
        public static Objectives Objectives = null;
        public static TLShutter TLShutter = null;
        public static RLShutter RLShutter = null;
        public static double UpperLimit, LowerLimit, fInterVal;
        public static object CmdSetMode = null;
        public static bool initialized = false;
        public static bool ArrowKeysEnabled = true;
        public static Point3D defaultPos = new Point3D(30000, 30000, 23900);
        public static Assembly dll = null;
        public static Dictionary<string, Type> Types = new Dictionary<string, Type>();
        public static object root = null;
        public static void Initialize()
        {
            if (initialized)
                return;
            Point3D.SetLimits(Stage.minX, Stage.maxX, Stage.minY, Stage.maxY, Focus.lowerLimit, Focus.upperLimit);
            PointD.SetLimits(Stage.minX, Stage.maxX, Stage.minY, Stage.maxY);
            Focus = new Focus();
            Stage = new Stage();
            Objectives = new Objectives();
            TLShutter = new TLShutter();
            RLShutter = new RLShutter();
            initialized = true;
        }
        public static Point3D GetPosition()
        {
            if (Stage == null)
                return new Point3D(0,0,0);
            PointD p = Stage.GetPosition();
            double f = Focus.GetFocus();
            return new Point3D(p.X, p.Y, f);
        }

        public static void SetPosition(Point3D p)
        {
            Stage.SetPosition(p.X,p.Y);
            Focus.SetFocus(p.Z);
            Microscope.redraw = true;
        }

        public static void SetPosition(PointD p)
        {
            Stage.SetPosition(p.X, p.Y);
            Microscope.redraw = true;
        }
        public static void OpenRL()
        {
            //If shutter is closed we open it.
            if (RLShutter.GetPosition() == 0)
                RLShutter.SetPosition(1);
        }

        public static void OpenTL()
        {
            //If shutter is closed we open it.
            if (TLShutter.GetPosition() == 0)
                TLShutter.SetPosition(1);
        }

        public static void CloseRL()
        {
            //If shutter is open then we close it.
            if (RLShutter.GetPosition() == 0)
                RLShutter.SetPosition(1);
        }

        public static void CloseTL()
        {
            //If shutter is open then we close it.
            if (TLShutter.GetPosition() == 0)
                TLShutter.SetPosition(1);
        }

        public static void SetTL(uint tl)
        {
            TLShutter.SetPosition((short)tl);
        }

        public static void SetRL(uint tr)
        {
            RLShutter.SetPosition((short)tr);
        }

        public static int GetTL()
        {
            return TLShutter.GetPosition();
        }

        public static int GetRL()
        {
            return RLShutter.GetPosition();
        }

        public static void MoveUp(double d)
        {
            Stage.MoveUp(d);
        }

        public static void MoveRight(double d)
        {
            Stage.MoveRight(d);
        }

        public static void MoveDown(double d)
        {
            Stage.MoveDown(d);
        }

        public static void MoveLeft(double d)
        {
            Stage.MoveLeft(d);
        }
        public static void MoveFieldUp()
        {
            Stage.MoveUp(Objectives.GetObjective().ViewHeight);
        }

        public static void MoveFieldRight()
        {
            Stage.MoveRight(Objectives.GetObjective().ViewWidth);
        }

        public static void MoveFieldDown()
        {
            Stage.MoveDown(Objectives.GetObjective().ViewHeight);
        }

        public static void MoveFieldLeft()
        {
            Stage.MoveLeft(Objectives.GetObjective().ViewWidth);
        }

        public static void SetFocus(double d)
        {
            Focus.SetFocus(d);
        }

        public static double GetFocus()
        {
            return Focus.GetFocus();
        }
    }

}
