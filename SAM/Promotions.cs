using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
    public enum PromotionType
    {
        Point,
        Box
    }

    public abstract class Promotion
    {
        public abstract float[] GetInput();
        public abstract float[] GetLable();
        public PromotionType mType;
        public ForeGround foreGround;
    }

    /* The PointPromotion class represents a promotion with X and Y coordinates and an operation type. */
    public class PointPromotion: Promotion
    {
        public PointPromotion(ForeGround optype)
        {
            this.mType = PromotionType.Point;
            this.foreGround = optype;
        }
        public int X { get; set; }
        public int Y { get; set; }
        public override float[] GetInput()
        {
            return new float[2] { X ,Y};
        }
        public override float[] GetLable()
        {
            if (this.foreGround == ForeGround.foreground)
            {
                return new float[1] { 1 };
            }
            else
            {
                return new float[1] { 0 };
            }          
        }
    }
    public enum ForeGround
    {
        foreground,
        background
    }
   
    /* The BoxPromotion class represents a promotion that involves a box shape, defined by a left-up
    point and a right-bottom point. */
    class BoxPromotion : Promotion
    {
        public BoxPromotion()
        {
            this.mLeftUp = new PointPromotion(ForeGround.foreground);
            this.mRightBottom = new PointPromotion(ForeGround.foreground);
            this.mType = PromotionType.Box;
        }
        public override float[] GetInput()
        {
            return new float[4] { this.mLeftUp.X, 
                this.mLeftUp.Y, 
                this.mRightBottom.X, 
                this.mRightBottom.Y };
        }
        public override float[] GetLable()
        {
            return new float[2] { 2,3 };
        }
        public PointPromotion mLeftUp { get; set; }
        public PointPromotion mRightBottom { get; set; }

    }

    /* The MaskPromotion class represents a mask with a specified width and height. */
    class MaskPromotion
    {
        public MaskPromotion(int wid,int hei)
        {
            this.mWidth = wid;
            this.mHeight = hei;
            this.mMask = new float[this.mWidth,this.mHeight];
        }

        float[,] mMask { get; set; }
        public int mWidth { get; set; }
        public int mHeight { get; set; }
    }
}
