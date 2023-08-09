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
    /// <summary>
    ///  Resizes images to the longest side 'target_length', as well as provides
    ///  methods for resizing coordinates and boxes. Provides methods for
    ///  transforming both numpy array and batched torch tensors.
    /// </summary>
    class Transforms
    {
        public Transforms(int target_length)
        {
            this.mTargetLength = target_length;
        }

        public float[] ApplyImage(BioImage b)
        {
            int neww = 0;
            int newh = 0;
            this.GetPreprocessShape(b.SizeX, b.SizeY, this.mTargetLength, ref neww, ref newh);

            float[,,] resizeImg = this.Resize(neww,newh);

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
        float[,,] Resize(int w,int h)
        {
            Bitmap originalImage = ImageView.SelectedBuffer;
            AForge.Imaging.Filters.ResizeBilinear res = new AForge.Imaging.Filters.ResizeBilinear(w, h);
            Bitmap resizedImage = res.Apply(originalImage.ImageRGB);
            //resizedImage.Save("resized.jpg", System.Drawing.Imaging.ImageFormat.Jpeg);
            float[,,] newimg = new float[3, w, h];
            for (int i = 0; i < w; i++)
            {
                for (int j = 0; j < h; j++)
                {
                    newimg[0, i, j] = resizedImage.GetPixel(i, j).R;
                    newimg[1, i, j] = resizedImage.GetPixel(i, j).G;
                    newimg[2, i, j] = resizedImage.GetPixel(i, j).B;
                }
            }
            //originalImage.Dispose();
            resizedImage.Dispose();
            return newimg;
        }

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
        public BoxPromotion ApplyBox(BoxPromotion org_box, int orgw, int orgh)
        {
            BoxPromotion box = new BoxPromotion();

            PointPromotion left = this.ApplyCoords((org_box as BoxPromotion).mLeftUp, orgw, orgh);
            PointPromotion lefrightt = this.ApplyCoords((org_box as BoxPromotion).mRightBottom, orgw, orgh);

            box.mLeftUp = left;
            box.mRightBottom = lefrightt;
            return box;
        }

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