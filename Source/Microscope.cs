using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;

using System.Diagnostics;
using System.Threading;
using System.Reflection;
using System.IO;
using AForge;

namespace BioGTK
{
    public class Stage
    {
        /* Defining the stage and axis types. */
        public static Type stageType;
        public static Type axisType;
        public static object xAxis;
        public static object yAxis;
        public static double minX;
        public static double maxX;
        public static double minY;
        public static double maxY;
        /* The constructor of the class Stage. */
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
        /// It waits for a certain amount of time before moving on to the next line of code
        private void MoveWait()
        {
            Thread.Sleep(moveWait);
        }
/// This function sets the position of the object to the given coordinates
/// 
/// @param px The x position of the object
/// @param py The y-coordinate of the point.
        public void SetPosition(double px, double py)
        {
            x = px;
            y = py;
        }
        /// This function sets the position of the object to the given x and y coordinates
        /// 
        /// @param px The new x position of the object
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

    /* The class is a wrapper for a list of objectives. */
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


    /* The class is a wrapper for the TLShutter class in the 

    The class is used to control the shutter. 

    The class is used in the following way: 

    1. Create an instance of the class. 
    2. Call the GetPosition() method to get the current position of the shutter. 
    3. Call the SetPosition() method to set the position of the shutter. 

    The GetPosition() method returns a short. 

    The SetPosition() method takes an int as an argument. 

    The SetPosition() method sets the position of the shutter. 

    The position of the shutter is an int. 

    The position of the shutter is set to 0 when the shutter is closed. 

    The position of the shutter is set to 1 when the shutter is open. 

    The position of the shutter is */

    /* The class is a wrapper for the TLShutter class in the Thorlabs.MotionControl.DeviceManager.dll */
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

    /* It's a class that has a static variable that holds the position of the shutter. */
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
        /* Defining an enum. */
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
        /// It sets the limits of the stage and focus, and then creates new instances of the stage,
        /// focus, objectives, and shutters
        /// 
        /// @return The return value is the value of the last expression evaluated in the function.
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
        /// Get the current position of the stage and the current focus position, and return a Point3D
        /// object containing the X, Y, and Z coordinates
        /// 
        /// @return A Point3D object.
        public static Point3D GetPosition()
        {
            if (Stage == null)
                return new Point3D(0,0,0);
            PointD p = Stage.GetPosition();
            double f = Focus.GetFocus();
            return new Point3D(p.X, p.Y, f);
        }

        /// It sets the position of the stage and focus to the values in the Point3D object
        /// 
        /// @param Point3D a class that contains 3 doubles, X, Y, and Z.
        public static void SetPosition(Point3D p)
        {
            Stage.SetPosition(p.X,p.Y);
            Focus.SetFocus(p.Z);
            Microscope.redraw = true;
        }

        /// The function SetPosition() takes a PointD object as an argument and sets the position of the
        /// stage to the X and Y coordinates of the PointD object
        /// 
        /// @param PointD 
        public static void SetPosition(PointD p)
        {
            Stage.SetPosition(p.X, p.Y);
            Microscope.redraw = true;
        }
        /// If the shutter is closed, open it
        public static void OpenRL()
        {
            //If shutter is closed we open it.
            if (RLShutter.GetPosition() == 0)
                RLShutter.SetPosition(1);
        }

        /// If the shutter is closed, open it
        public static void OpenTL()
        {
            //If shutter is closed we open it.
            if (TLShutter.GetPosition() == 0)
                TLShutter.SetPosition(1);
        }

        /// If the shutter is open, then we close it
        public static void CloseRL()
        {
            //If shutter is open then we close it.
            if (RLShutter.GetPosition() == 0)
                RLShutter.SetPosition(1);
        }

        /// If the shutter is open, then we close it
        public static void CloseTL()
        {
            //If shutter is open then we close it.
            if (TLShutter.GetPosition() == 0)
                TLShutter.SetPosition(1);
        }

       /// Set the position of the shutter to the value of the variable tl
       /// 
       /// @param tl The position of the shutter.
        public static void SetTL(uint tl)
        {
            TLShutter.SetPosition((short)tl);
        }

        /// The function takes a uint (unsigned integer) as an argument and sets the position of the
        /// RLShutter to the value of the argument
        /// 
        /// @param tr the position of the shutter
        public static void SetRL(uint tr)
        {
            RLShutter.SetPosition((short)tr);
        }

        /// GetTL() returns the position of the TLShutter object.
        /// 
        /// @return The position of the shutter.
        public static int GetTL()
        {
            return TLShutter.GetPosition();
        }

        /// It returns the position of the RLShutter object
        /// 
        /// @return The position of the RLShutter.
        public static int GetRL()
        {
            return RLShutter.GetPosition();
        }

        /// Move the stage up by a distance d
        /// 
        /// @param d The distance to move the stage up.
        public static void MoveUp(double d)
        {
            Stage.MoveUp(d);
        }

        /// MoveRight(double d) moves the stage right by d
        /// 
        /// @param d The distance to move the stage in millimeters.
        public static void MoveRight(double d)
        {
            Stage.MoveRight(d);
        }

       /// Move the stage down by the specified distance
       /// 
       /// @param d The distance to move down.
        public static void MoveDown(double d)
        {
            Stage.MoveDown(d);
        }

        /// Move the stage left by the specified distance
        /// 
        /// @param d The distance to move the stage in mm.
        public static void MoveLeft(double d)
        {
            Stage.MoveLeft(d);
        }
       /// Move the field up by the height of the current objective
        public static void MoveFieldUp()
        {
            Stage.MoveUp(Objectives.GetObjective().ViewHeight);
        }

       /// Move the field right by the width of the view
        public static void MoveFieldRight()
        {
            Stage.MoveRight(Objectives.GetObjective().ViewWidth);
        }

       /// Move the field down by the height of the objective
        public static void MoveFieldDown()
        {
            Stage.MoveDown(Objectives.GetObjective().ViewHeight);
        }

       /// Move the field left by the width of the view
        public static void MoveFieldLeft()
        {
            Stage.MoveLeft(Objectives.GetObjective().ViewWidth);
        }

       /// It sets the focus of the camera to the distance specified by the parameter
       /// 
       /// @param d The control to focus on.
        public static void SetFocus(double d)
        {
            Focus.SetFocus(d);
        }

        /// It returns the current focus of the camera
        /// 
        /// @return The focus of the camera.
        public static double GetFocus()
        {
            return Focus.GetFocus();
        }
    }

}
