# BioGTK
![alt text](https://github.com/BiologyTools/Bio/blob/master/banner.jpg)

A .NET application & library for editing & annotating various microscopy image formats. Supports all bioformats supported images. Integrates with ImageJ, running ImageJ filters & macro functions. Check out the wiki for [library usage.](https://github.com/BiologyTools/BioGTK/#Usage) or check out the [documentation.](https://biologytools.github.io/) Supports Windows, Linux and Mac. For Windows & Mac see installation instructions. For Discussion check out the new Discord Server. https://discord.gg/tdeyc6fgpv

[![NuGet version (BioGTK)](https://img.shields.io/nuget/v/BioGTK.svg?style=flat-square)](https://www.nuget.org/packages/BioGTK/3.3.1)
[![NuGet version (BioGTK)](https://img.shields.io/nuget/dt/BioGTK?color=g)](https://www.nuget.org/packages/BioGTK/3.3.1) [![DOI](https://zenodo.org/badge/DOI/10.5281/zenodo.8122239.svg)](https://doi.org/10.5281/zenodo.8122239)
## Features

- C# scripting with sample tool-script and other sample scripts in "/Scripts/" folder. [See samples](https://github.com/BioMicroscopy/BioImage-Scripts)

- Supports running ImageJ macro commands on images open in Bio. Console to run ImageJ macro commands and Bio C# scripts.

- Supports Pyramidal images with multiple resolutions. Like whole slide images.

- Multiple view modes like Emission, and Filtered. ROI's shown for each channel can be configured in ROI Manager.

- Supports drawing shapes & colors onto 16 bit & 48 bit images, unlike System.Drawing.Graphics.

- Convenient viewing of image stacks with scroll wheel moving Z-plane and mouse side buttons scrolling C-planes.

- Editing & saving ROI's in images to OME format image stacks.

- Copy & Paste to quickly annotate images and name them easily by right click.

- Select multiple points by holding down control key, and move them by holding down control key. 

- Exporting ROI's from each OME image in a folder of images to CSV.

- Easy segmentation with Segment Anything (SAM). Required model files downloadable from [Releases.](https://github.com/BiologyTools/BioGTK/releases/tag/SAM-Models)

## Dependencies
- [BioFormats.NET6](https://github.com/BiologyTools/BioFormatsNET6)
- [IKVM](http://www.ikvm.net/)
- [AForge](http://www.aforgenet.com/)
- [LibTiff.Net](https://bitmiracle.com/libtiff/)
- [Cs-script](https://github.com/oleg-shilo/cs-script/blob/master/LICENSE)
- [ImageJ](https://imagej.nih.gov/ij/) (Only needed when running ImageJ macro commands)
- [ScottPlot](https://oxyplot.github.io/)
- [LibVips](https://www.libvips.org/install.html) (Optional)
- [Segment-Anything-CSharp](https://github.com/AIDajiangtang/Segment-Anything-CSharp) (Optional)

## Licenses
- BioGTK [GPL3](https://www.gnu.org/licenses/gpl-3.0.en.html)
- AForge [LGPL](http://www.aforgenet.com/framework/license.html)
- BioFormats.NET6 [GPL3](https://www.gnu.org/licenses/gpl-3.0.en.html)
- [IKVM](https://github.com/gluck/ikvm/blob/master/LICENSE)
- LibTiff.Net [BSD](https://bitmiracle.com/libtiff/)
- Cs-script [MIT](https://github.com/oleg-shilo/cs-script/blob/master/LICENSE)
- ScottPlot [MIT](https://github.com/ScottPlot/ScottPlot/blob/main/LICENSE)
- LibVips [LGPL]([https://www.libvips.org/install.html](https://github.com/libvips/libvips/blob/master/LICENSE))
- Segment-Anything-CSharp [Apache License 2.0](https://github.com/AIDajiangtang/Segment-Anything-CSharp/blob/main/LICENSE)

## Scripting
-  Save scripts into "StartupPath/Scripts" with ".cs" ending.
-  Open script editor and recorder from menu.
-  Scripts saved in Scripts folder will be loaded into script runner.
-  Use Script recorder to record program function calls and script runner to turn recorder text into working scripts. (See sample [scripts](https://github.com/BioMicroscopy/BioImage-Scripts)

## Mac Installation
- Install Mac package manager [homebrew.](https://brew.sh/)
- From brew install [GTK3.](https://formulae.brew.sh/formula/gtk+3#default)
- Download the BioGTK application for either OSX-x64 or OSX-Arm from releases.
- Make the file executable by opening terminal in the extracted folder and running "chmod 755 BioGTKApp"
- Optionally install [LibVips](https://www.libvips.org/install.html) package for whole-slide support with homebrew.
- Optionally install Onnx Runtime by running "brew install onnxruntime" for running SAM.

## Windows Installation
- Install package manager [MSYS2.](https://github.com/GtkSharp/GtkSharp/wiki/Installing-Gtk-on-Windows) to install package GTK3. (Required for GTK Apps.)
- Download the BioGTK Windows installer from releases.
- Optionally install [LibVips](https://www.libvips.org/install.html) for increased performance when opening pyramidal images. Make sure to define environmental variable $VIPS_HOME as instructed [here.](https://github.com/kleisauke/net-vips/issues/3)

## Linux Installation
- Just download the latest tarball(tar.gz) from Releases as linux already includes GTK3 package.
- Optionally install [LibVips](https://www.libvips.org/install.html) for increased performance when opening pyramidal images.

## Usage
```
//If you want to initialize the application call app initialize. 
//This will initialize Bioformats library as well as the rest of the application.
App.Initialize();

//Or you can create a new NodeView which will initialize the application
//as well as parse any command line arguments.
NodeView nodes = NodeView.Create(new string[]{"file"});

//You can also call BioImage.Initialize to 
//initialize just the Bioformats library.
BioImage.Initialize();

//Once initialized you can open OME, ImageJ tiff files, and Bio Tiff files with:
BioImage b = BioImage.OpenFile("file");

//Or if you want to use specifically the OME image reader you can use BioImage.OpenOME
BioImage b = BioImage.OpenOME("file");

//If you are working with a pyramidal image you can open a portion of a tiled image with OpenOME.
//BioImage.OpenOME(string file, int serie, bool tab, bool addToImages, bool tile, int tilex, int tiley, int tileSizeX, int tileSizeY)

//You can specify whether to open in a newtab as well as whether to add the image to 
//the Images.images table. As well as specify whether to open as a tile with the specified 
//tile X,Y position & tile width & height.    
BioImage.OpenOME("file",0,false,false,true,0,0,600,600);
//This will open a portion of the image as a tile and won't add it to the Images table.

//Once you have opened a tiled image with BioImage.OpenOME you can call the 
//GetTile(BioImage b, ZCT coord, int serie, int tilex, int tiley, int tileSizeX, int tileSizeY) method
// to quickly get another tile from different portion of the image. For BioGTK & BioLib
Bitmap bm = GetTile(b, new ZCT(0,0,0), 0, 100, 100, 600, 600);

//You can display an image with the ImageView control which can display
// Pyramidal, Whole-Slide, and Series of images.
ImageView v = ImageView.Create(b);

//To get the current coordinate of the ImageView you can call GetCoordinate.
ZCT cord = v.GetCoordinate();
//or to set the current coordinate
v.SetCoordinate(new ZCT(1,1,1));

//To create a point as well as any other ROI type you can call the ROI create methods.
ROI p = ROI.CreatePoint(cord, 0, 0);
ROI rect = ROI.CreateRectangle(cord, 0, 0, 100, 100);

//Usage of Graphics class for 16 & 48 bit images as well as regular bit depth images
//is very similar to System.Graphics.
//We create a new Graphics object by passing the Bitmap for BioGTK & BioLib and BufferInfo for BioCore
Graphics g = Graphics.FromImage(b.Buffers[0]);

//Then we create a pen by passing a ColorS which represent a Color with, 
//a higher bit depth (unsigned short) rather than a byte.
g.pen = new Pen(new ColorS(ushort.MaxValue, ushort.MaxValue, ushort.MaxValue));

//Then we can call the familiar methods DrawLine, DrawPolygon, FillPolygon etc.
g.DrawLine(0,0,100,100);
//Finally we dispose the Graphics object.
g.Dispose();

//Then to update the image in the viewer once we have made changes to the image we call:
v.UpdateImage();
//This will update the images of the viewer in the current coordinate plane.
v.UpdateView();

//We can also save the resulting image given the ID of the image in the Images table.
//All images opened with BioImage.OpenFile or BioImage.OpenOME are added to the 
//Images.images table with the filename as an ID.
BioImage.SaveFile("file","path");

//To convert between different pixel formats we can call for example To24Bit.
b.To24Bit();
```

## Sample Tool Script
```
//css_reference BioGTK.dll; 
using System; 
using BioGTK;
using System.Threading;
using AForge;
public class Loader {

//Point ROI Tool Example
public string Load()
{
	int ind = 1;
	do
	{
		BioGTK.Scripting.State s = BioGTK.Scripting.GetState();
		if (s != null)
		{
			if (!s.processed)
			{
				if (s.type == BioGTK.Scripting.Event.Down && s.buts == 1)
				{
					ZCT cord = BioGTK.App.viewer.GetCoordinate();
					BioGTK.Scripting.LogLine(cord.ToString() + " Coordinate");
					BioGTK.ROI an = BioGTK.ROI.CreatePoint(cord, s.p.X, s.p.Y);
					BioGTK.ImageView.SelectedImage.Annotations.Add(an);
					BioGTK.Scripting.LogLine(cord.ToString() + " Coordinate");
					an.Text = "Point" + ind;
					ind++;
					BioGTK.Scripting.LogLine(s.ToString() + " Point");
					//ImageView.viewer.UpdateOverlay();
				}
				else
				if (s.type == BioGTK.Scripting.Event.Up)
				{
					BioGTK.Scripting.LogLine(s.ToString());
				}
				else
				if (s.type == BioGTK.Scripting.Event.Move)
				{
					BioGTK.Scripting.LogLine(s.ToString());
				}
				s.processed = true;
			}
		}
		if(BioGTK.Scripting.Exit("points.cs"))
		{	
			return "OK";
		}
	} while (true);
	return "OK";
}
}
```
