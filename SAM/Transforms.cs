using System;
using System.Collections.Generic;
using AForge;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using MathNet.Numerics.Statistics;
using Gdk;

namespace BioGTK
{

    class Transforms
    {
        public Transforms(int target_length)
        {
            this.mTargetLength = target_length;
        }

        /// The function takes a BioImage object, resizes it, calculates the mean and standard deviation
        /// of each channel, and returns a transformed image.
        /// 
        /// @param BioImage The BioImage parameter represents an image object that contains the image
        /// data. It likely has properties such as SizeX and SizeY, which represent the width and height
        /// of the image, respectively.
        /// 
        /// @return The method is returning a float array called "transformedImg".
        public float[] ApplyImage(Bitmap b)
        {
            int neww = 0;
            int newh = 0;
            this.GetPreprocessShape(b.SizeX, b.SizeY, this.mTargetLength, ref neww, ref newh);

            float[,,] resizeImg = this.Resize(b,neww,newh);

            float[] means = new float[resizeImg.GetLength(0)];
            for (int i = 0; i < resizeImg.GetLength(0); i++)
            {
                float[] data = new float[resizeImg.GetLength(1) * resizeImg.GetLength(2)];
                for (int j = 0; j < resizeImg.GetLength(1); j++)
                {
                    for (int k = 0; k < resizeImg.GetLength(2); k++)
                    {
                        data[j * resizeImg.GetLength(2) + k] = resizeImg[i, j, k];
                    }
                }
                means[i] = (float)MathNet.Numerics.Statistics.Statistics.Mean(data);
            }

            float[] stdDev = new float[resizeImg.GetLength(0)];
            for (int i = 0; i < resizeImg.GetLength(0); i++)
            {
                float[] data = new float[resizeImg.GetLength(1) * resizeImg.GetLength(2)];
                for (int j = 0; j < resizeImg.GetLength(1); j++)
                {
                    for (int k = 0; k < resizeImg.GetLength(2); k++)
                    {
                        data[j * resizeImg.GetLength(2) + k] = resizeImg[i, j, k];
                    }
                }
                stdDev[i] = (float)MathNet.Numerics.Statistics.Statistics.StandardDeviation(data);
            }


            float[] transformedImg = new float[3 * this.mTargetLength * this.mTargetLength];
            for (int i = 0; i < neww; i++)
            {
                for (int j = 0; j < newh; j++)
                {
                    int index = j * this.mTargetLength + i;
                    transformedImg[index] = (resizeImg[0, i, j] - means[0]) / stdDev[0];
                    transformedImg[this.mTargetLength * this.mTargetLength + index] = (resizeImg[1, i, j] - means[1]) / stdDev[1];
                    transformedImg[2 * this.mTargetLength * this.mTargetLength + index] = (resizeImg[2, i, j] - means[2]) / stdDev[2];
                }
            }

            return transformedImg;
        }
        /// The function takes an input width and height, resizes an image using bilinear interpolation,
        /// and returns the resized image as a float array.
        /// 
        /// @param originalImage The original Bitmap image.
        /// @param w The width of the resized image.
        /// @param h The parameter "h" in the Resize method represents the desired height of the resized
        /// image.
        /// 
        /// @return The method is returning a 3-dimensional float array representing the resized image.
        float[,,] Resize(Bitmap originalImage, int w,int h)
        {
            AForge.Imaging.Filters.ResizeBilinear res = new AForge.Imaging.Filters.ResizeBilinear(w, h);
            Bitmap resizedImage = res.Apply(originalImage.ImageRGB);
            //resizedImage.Save("resized.jpg", System.Drawing.Imaging.ImageFormat.Jpeg);
            float[,,] newimg = new float[3, w, h];
            for (int i = 0; i < w; i++)
            {
                for (int j = 0; j < h; j++)
                {
                    newimg[0, i, j] = resizedImage.GetPixel(i, j).Rf * 255;
                    newimg[1, i, j] = resizedImage.GetPixel(i, j).Gf * 255;
                    newimg[2, i, j] = resizedImage.GetPixel(i, j).Bf * 255;
                }
            }
            //originalImage.Dispose();
            resizedImage.Dispose();
            return newimg;
        }

        /// The function takes in a point and its original width and height, applies a scaling factor
        /// based on a target length, and returns a new point with adjusted coordinates.
        /// 
        /// @param PointPromotion PointPromotion is a class that represents a point with additional
        /// properties related to promotions. It has properties like X and Y coordinates, and an Optype
        /// property.
        /// @param orgw The original width of the shape.
        /// @param orgh The parameter "orgh" represents the original height of the shape.
        /// 
        /// @return The method is returning a new instance of the PointPromotion class, which is
        /// assigned to the variable newpointp.
        public PointPromotion ApplyCoords(PointPromotion org_point, int orgw, int orgh)
        {
            int neww = 0;
            int newh = 0;
            this.GetPreprocessShape(orgw, orgh, this.mTargetLength, ref neww, ref newh);
            PointPromotion newpointp = new PointPromotion(org_point.m_Optype);
            float scalx = (float)neww / (float)orgw;
            float scaly = (float)newh / (float)orgh;
            newpointp.X = (int)(org_point.X * scalx);
            newpointp.Y = (int)(org_point.Y * scaly);

            return newpointp;
        }
        /// The function ApplyBox takes a BoxPromotion object, along with its original width and height,
        /// and applies coordinate transformations to return a new BoxPromotion object.
        /// 
        /// @param BoxPromotion The BoxPromotion class represents a box with coordinates for the
        /// top-left and bottom-right corners.
        /// @param orgw The orgw parameter represents the original width of the box.
        /// @param orgh The parameter "orgh" represents the original height of the box.
        /// 
        /// @return The method is returning a BoxPromotion object.
        public BoxPromotion ApplyBox(BoxPromotion org_box, int orgw, int orgh)
        {
            BoxPromotion box = new BoxPromotion();

            PointPromotion left = this.ApplyCoords((org_box as BoxPromotion).mLeftUp, orgw, orgh);
            PointPromotion lefrightt = this.ApplyCoords((org_box as BoxPromotion).mRightBottom, orgw, orgh);

            box.mLeftUp = left;
            box.mRightBottom = lefrightt;
            return box;
        }

        /// The function calculates the new width and height of an image based on the desired long side
        /// length and the original width and height.
        /// 
        /// @param oldw The width of the original image.
        /// @param oldh The height of the original image.
        /// @param long_side_length The desired length of the longer side of the new shape.
        /// @param neww The new width of the shape after preprocessing.
        /// @param newh The new height of the image after preprocessing.
        void GetPreprocessShape(int oldw, int oldh, int long_side_length, ref int neww, ref int newh)
        {
            float scale = long_side_length * 1.0f / Math.Max(oldh, oldw);
            float newht = oldh * scale;
            float newwt = oldw * scale;

            neww = (int)(newwt);//+0.5
            newh = (int)(newht);//+0.5
        }
        int mTargetLength;
    }
}